using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.DependencyInjection;
using SistemadeVentasSupermercado.Tests.Infraestructura;
using SistemadeVentasSupermercado.Web;
using SistemadeVentasSupermercado.Web.Core;
using SistemadeVentasSupermercado.Web.Data;
using SistemadeVentasSupermercado.Web.Data.Entities;
using SistemadeVentasSupermercado.Web.DTOs;
using SistemadeVentasSupermercado.Web.Services.Abstractions;
using SistemadeVentasSupermercado.Web.Services.Implementations;

namespace SistemadeVentasSupermercado.Tests
{
    /// <summary>PIN de autorización de supervisor: crearlo, quién puede autorizar, errores y bloqueo.</summary>
    [Collection(ColeccionBaseDeDatos.Nombre)]
    public class AuthorizationPinTests : IDisposable
    {
        private const string Clave = "Clave2026Segura";
        private const string Pin = "428317";
        private const string PinMalo = "905162";

        private readonly BaseDeDatosFixture _fixture;
        private readonly RelojFijo _reloj = new(DateTimeOffset.UtcNow);
        private readonly ServiceProvider _servicios;

        public AuthorizationPinTests(BaseDeDatosFixture fixture)
        {
            _fixture = fixture;

            ServiceCollection services = new();
            services.AddLogging();
            services.AddHttpContextAccessor();
            services.AddDataProtection();
            services.AddScoped(_ => _fixture.CrearContexto());
            services.AddIdentity<User, IdentityRole>(CustomConfiguration.ConfigureIdentityOptions)
                    .AddEntityFrameworkStores<DataContext>()
                    .AddDefaultTokenProviders();
            services.AddSingleton<TimeProvider>(_reloj);
            services.AddScoped<IUserActivityRecorder, UserActivityRecorder>();
            services.AddScoped<IAuthorizationPinService, AuthorizationPinService>();
            services.AddScoped<UsersService>();
            _servicios = services.BuildServiceProvider();
        }

        public void Dispose() => _servicios.Dispose();

        private sealed class RelojFijo(DateTimeOffset ahora) : TimeProvider
        {
            public DateTimeOffset Ahora { get; set; } = ahora;
            public override DateTimeOffset GetUtcNow() => Ahora;
        }

        // ---------- Ayudantes ----------

        private async Task<T> ConAsync<TServicio, T>(Func<TServicio, Task<T>> accion) where TServicio : notnull
        {
            using IServiceScope scope = _servicios.CreateScope();
            return await accion(scope.ServiceProvider.GetRequiredService<TServicio>());
        }

        /// <summary>Crea un rol propio de la prueba con esos permisos del módulo Ventas.</summary>
        private async Task<Guid> RolAsync(params string[] permisos)
        {
            await using DataContext context = _fixture.CrearContexto();
            SistemaVentasRole rol = new() { Id = Guid.NewGuid(), Name = $"Rol PIN {Guid.NewGuid().ToString("N")[..8]}" };
            context.SistemaVentasRoles.Add(rol);

            foreach (string nombre in permisos)
            {
                Permission? permiso = await context.Permissions.FirstOrDefaultAsync(p => p.Name == nombre);
                if (permiso is null)
                {
                    permiso = new Permission { Id = Guid.NewGuid(), Name = nombre, Description = nombre, Module = "Ventas" };
                    context.Permissions.Add(permiso);
                }
                context.RolePermissions.Add(new RolePermission { PermissionId = permiso.Id, SistemaVentasRoleId = rol.Id });
            }

            await context.SaveChangesAsync();
            return rol.Id;
        }

        private async Task<User> CrearAsync(Guid rolId)
        {
            string marca = Guid.NewGuid().ToString("N")[..8];
            User usuario = new()
            {
                Id = Guid.NewGuid().ToString(),
                UserName = $"pin-{marca}@pruebas.local",
                Email = $"pin-{marca}@pruebas.local",
                Document = $"PIN-{marca}",
                FirstName = "Persona",
                LastName = marca,
                SistemaVentasRoleId = rolId
            };

            using IServiceScope scope = _servicios.CreateScope();
            UserManager<User> manager = scope.ServiceProvider.GetRequiredService<UserManager<User>>();
            IdentityResult resultado = await manager.CreateAsync(usuario, Clave);
            Assert.True(resultado.Succeeded);
            return usuario;
        }

        private async Task<User> SupervisorAsync() => await CrearAsync(await RolAsync("cancelSales", "createReturns"));
        private async Task<User> CajeroAsync() => await CrearAsync(await RolAsync("createSales"));

        private async Task<User> SupervisorConPinAsync()
        {
            User supervisor = await SupervisorAsync();
            Response<bool> guardado = await GuardarPinAsync(supervisor, Pin);
            Assert.True(guardado.IsSuccess, guardado.Message);
            return supervisor;
        }

        private Task<Response<bool>> GuardarPinAsync(User usuario, string pin, string clave = Clave, string? confirmacion = null)
            => ConAsync<IAuthorizationPinService, Response<bool>>(s => s.SetPinAsync(usuario.UserName!,
                new SetAuthorizationPinDTO { CurrentPassword = clave, Pin = pin, ConfirmPin = confirmacion ?? pin }));

        private Task<Response<AuthorizerDTO>> VerificarAsync(User autoriza, string pin, User pide,
            SupervisorOperation operacion = SupervisorOperation.CancelSale)
            => ConAsync<IAuthorizationPinService, Response<AuthorizerDTO>>(
                s => s.VerifyAsync(operacion, autoriza.Id, pin, pide.UserName!));

        private async Task<UserAuthorizationPin?> FilaAsync(User usuario)
        {
            await using DataContext context = _fixture.CrearContexto();
            return await context.UserAuthorizationPins.AsNoTracking().FirstOrDefaultAsync(p => p.UserId == usuario.Id);
        }

        private async Task<User> LeerAsync(User usuario)
        {
            await using DataContext context = _fixture.CrearContexto();
            return await context.Users.AsNoTracking().SingleAsync(u => u.Id == usuario.Id);
        }

        private async Task CambiarAsync(User usuario, Action<User> cambio)
        {
            await using DataContext context = _fixture.CrearContexto();
            User guardado = await context.Users.SingleAsync(u => u.Id == usuario.Id);
            cambio(guardado);
            await context.SaveChangesAsync();
        }

        private async Task<List<UserActivity>> RechazosAsync(User pide)
        {
            await using DataContext context = _fixture.CrearContexto();
            return await context.UserActivities.AsNoTracking()
                .Where(a => a.Type == UserActivityType.SupervisorAuthorizationFailed && a.ActorUserId == pide.Id)
                .ToListAsync();
        }

        // ---------- Reglas del PIN ----------

        [Theory]
        [InlineData("000000")]
        [InlineData("777777")]
        [InlineData("123456")]
        [InlineData("654321")]
        [InlineData("890123")]
        [InlineData("121212")]
        [InlineData("123123")]
        public void PinFacil_SeReconoce(string pin) => Assert.True(AuthorizationPinRules.IsTooEasy(pin));

        [Theory]
        [InlineData("428317")]
        [InlineData("112233")]
        [InlineData("905162")]
        public void PinNormal_NoEsFacil(string pin) => Assert.False(AuthorizationPinRules.IsTooEasy(pin));

        [Theory]
        [InlineData(null)]
        [InlineData("")]
        [InlineData("12345")]
        [InlineData("1234567")]
        [InlineData("12a456")]
        [InlineData(" 42831")]
        [InlineData("４２８３１７")] // dígitos de ancho completo: no son del 0 al 9
        public void PinConMalFormato_NoSeAcepta(string? pin) => Assert.False(AuthorizationPinRules.HasValidFormat(pin));

        // ---------- Crear y cambiar ----------

        [Fact]
        public async Task Guardar_NoGuardaElPin_SoloSuHash_YQuedaEnElHistorial()
        {
            User supervisor = await SupervisorConPinAsync();

            UserAuthorizationPin fila = (await FilaAsync(supervisor))!;
            Assert.DoesNotContain(Pin, fila.PinHash);
            Assert.Equal(0, fila.FailedAttempts);

            await using DataContext context = _fixture.CrearContexto();
            UserActivity registro = await context.UserActivities.AsNoTracking()
                .SingleAsync(a => a.Type == UserActivityType.AuthorizationPinChanged && a.ActorUserId == supervisor.Id);
            Assert.Null(registro.Details);
        }

        [Fact]
        public async Task Guardar_SinPermisoParaAutorizar_SeRechaza()
        {
            User cajero = await CajeroAsync();

            Response<bool> respuesta = await GuardarPinAsync(cajero, Pin);

            Assert.False(respuesta.IsSuccess);
            Assert.Null(await FilaAsync(cajero));
        }

        [Fact]
        public async Task Guardar_ConContrasenaMala_SeRechaza_YCuentaParaElBloqueoDeLaCuenta()
        {
            User supervisor = await SupervisorAsync();

            Response<bool> respuesta = await GuardarPinAsync(supervisor, Pin, clave: "OtraClave2026");

            Assert.False(respuesta.IsSuccess);
            Assert.Null(await FilaAsync(supervisor));
            Assert.Equal(1, (await LeerAsync(supervisor)).AccessFailedCount);
        }

        [Theory]
        [InlineData("123456", "123456")]
        [InlineData("1234", "1234")]
        [InlineData("428317", "428318")]
        public async Task Guardar_PinNoValido_SeRechaza(string pin, string confirmacion)
        {
            User supervisor = await SupervisorAsync();

            Response<bool> respuesta = await GuardarPinAsync(supervisor, pin, confirmacion: confirmacion);

            Assert.False(respuesta.IsSuccess);
            Assert.Null(await FilaAsync(supervisor));
        }

        [Fact]
        public async Task Cambiar_ElPinViejoDejaDeServir_YSeQuitaElBloqueo()
        {
            User supervisor = await SupervisorConPinAsync();
            User cajero = await CajeroAsync();
            for (int i = 0; i < AuthorizationPinRules.MaxFailedAttempts; i++)
                await VerificarAsync(supervisor, PinMalo, cajero);

            Assert.True((await GuardarPinAsync(supervisor, "719304")).IsSuccess);

            Assert.False((await VerificarAsync(supervisor, Pin, cajero)).IsSuccess);
            Assert.True((await VerificarAsync(supervisor, "719304", cajero)).IsSuccess);
        }

        // ---------- Quién puede autorizar ----------

        [Fact]
        public async Task Lista_SoloQuienPuedeEsaOperacion_YTienePin()
        {
            User conTodo = await SupervisorConPinAsync();
            User soloDevuelve = await CrearAsync(await RolAsync("createReturns"));
            await GuardarPinAsync(soloDevuelve, Pin);
            User sinPin = await SupervisorAsync();

            List<string> anulan = await ListaAsync(SupervisorOperation.CancelSale, sinPin);
            List<string> devuelven = await ListaAsync(SupervisorOperation.CreateReturn, sinPin);
            List<string> sinElMismo = await ListaAsync(SupervisorOperation.CancelSale, conTodo);

            Assert.Contains(conTodo.Id, anulan);
            Assert.DoesNotContain(soloDevuelve.Id, anulan);
            Assert.DoesNotContain(sinPin.Id, anulan);
            Assert.Contains(soloDevuelve.Id, devuelven);
            Assert.DoesNotContain(conTodo.Id, sinElMismo);
        }

        private async Task<List<string>> ListaAsync(SupervisorOperation operacion, User pide)
        {
            List<AuthorizerDTO> lista = await ConAsync<IAuthorizationPinService, List<AuthorizerDTO>>(
                s => s.GetAuthorizersAsync(operacion, pide.UserName!));
            return lista.Select(a => a.UserId).ToList();
        }

        [Theory]
        [InlineData("desactivado")]
        [InlineData("bloqueado")]
        [InlineData("claveTemporal")]
        [InlineData("sinPermiso")]
        public async Task QuienDejaDeCumplir_NoAutoriza_AunqueElPinSeaCorrecto(string caso)
        {
            User supervisor = await SupervisorConPinAsync();
            User cajero = await CajeroAsync();
            Guid rolSinPermiso = await RolAsync("createSales");

            await CambiarAsync(supervisor, u =>
            {
                if (caso == "desactivado") u.DeactivatedAt = DateTime.UtcNow;
                if (caso == "bloqueado") u.LockoutEnd = _reloj.Ahora.AddMinutes(10);
                if (caso == "claveTemporal") u.MustChangePassword = true;
                if (caso == "sinPermiso") u.SistemaVentasRoleId = rolSinPermiso;
            });

            Response<AuthorizerDTO> respuesta = await VerificarAsync(supervisor, Pin, cajero);

            Assert.False(respuesta.IsSuccess);
            Assert.DoesNotContain(supervisor.Id, await ListaAsync(SupervisorOperation.CancelSale, cajero));
            Assert.Single(await RechazosAsync(cajero));
        }

        [Fact]
        public async Task ElAdmin_PuedeAutorizar_SinPermisosMarcados()
        {
            await using DataContext context = _fixture.CrearContexto();
            SistemaVentasRole? admin = await context.SistemaVentasRoles
                .FirstOrDefaultAsync(r => r.Name == Env.SUPER_ADMIN_ROLE_NAME);
            if (admin is null)
            {
                admin = new SistemaVentasRole { Id = Guid.NewGuid(), Name = Env.SUPER_ADMIN_ROLE_NAME };
                context.SistemaVentasRoles.Add(admin);
                await context.SaveChangesAsync();
            }

            User dueno = await CrearAsync(admin.Id);
            try
            {
                Assert.True((await GuardarPinAsync(dueno, Pin)).IsSuccess);
                Assert.True((await VerificarAsync(dueno, Pin, await CajeroAsync(), SupervisorOperation.CreateReturn)).IsSuccess);
            }
            finally
            {
                // Otras pruebas cuentan los administradores activos.
                await CambiarAsync(dueno, u => u.DeactivatedAt = DateTime.UtcNow);
            }
        }

        [Fact]
        public async Task NadieSeAutorizaASiMismo()
        {
            User supervisor = await SupervisorConPinAsync();

            Response<AuthorizerDTO> respuesta = await VerificarAsync(supervisor, Pin, supervisor);

            Assert.False(respuesta.IsSuccess);
        }

        [Fact]
        public async Task UnIdInventado_SeRechaza_YSeRegistraSinPersona()
        {
            User cajero = await CajeroAsync();

            Response<AuthorizerDTO> respuesta = await ConAsync<IAuthorizationPinService, Response<AuthorizerDTO>>(
                s => s.VerifyAsync(SupervisorOperation.CancelSale, Guid.NewGuid().ToString(), Pin, cajero.UserName!));

            Assert.False(respuesta.IsSuccess);
            Assert.Null(Assert.Single(await RechazosAsync(cajero)).TargetUserId);
        }

        // ---------- Errores y bloqueo ----------

        [Fact]
        public async Task PinCorrecto_Autoriza_YReiniciaElContador()
        {
            User supervisor = await SupervisorConPinAsync();
            User cajero = await CajeroAsync();
            await VerificarAsync(supervisor, PinMalo, cajero);

            Response<AuthorizerDTO> respuesta = await VerificarAsync(supervisor, Pin, cajero);

            Assert.True(respuesta.IsSuccess, respuesta.Message);
            Assert.Equal(supervisor.Id, respuesta.Result!.UserId);
            Assert.Equal(0, (await FilaAsync(supervisor))!.FailedAttempts);
        }

        [Fact]
        public async Task CincoErrores_BloqueanElPin_PeroNoElIngreso()
        {
            User supervisor = await SupervisorConPinAsync();
            User cajero = await CajeroAsync();

            for (int i = 0; i < AuthorizationPinRules.MaxFailedAttempts; i++)
                Assert.False((await VerificarAsync(supervisor, PinMalo, cajero)).IsSuccess);

            Response<AuthorizerDTO> conElBueno = await VerificarAsync(supervisor, Pin, cajero);

            Assert.False(conElBueno.IsSuccess);
            Assert.Contains("bloqueado", conElBueno.Message);
            User cuenta = await LeerAsync(supervisor);
            Assert.Null(cuenta.LockoutEnd);
            Assert.Equal(0, cuenta.AccessFailedCount);

            // En el historial: una línea para ese minuto, con quién estaba en la sesión y sin el PIN.
            UserActivity registro = Assert.Single(await RechazosAsync(cajero));
            Assert.Equal(6, registro.AttemptCount);
            Assert.Equal(supervisor.Id, registro.TargetUserId);
            Assert.Contains(cajero.Email!, registro.ActorName);
            Assert.Null(registro.Details);
            Assert.DoesNotContain(PinMalo, registro.GroupKey);
        }

        [Fact]
        public async Task PasadosLos15Minutos_VuelveAServir_ConCincoIntentosNuevos()
        {
            User supervisor = await SupervisorConPinAsync();
            User cajero = await CajeroAsync();
            for (int i = 0; i < AuthorizationPinRules.MaxFailedAttempts; i++)
                await VerificarAsync(supervisor, PinMalo, cajero);

            _reloj.Ahora += AuthorizationPinRules.LockoutTime + TimeSpan.FromSeconds(1);

            Response<AuthorizerDTO> otroError = await VerificarAsync(supervisor, PinMalo, cajero);
            Assert.Contains("restantes: 4", otroError.Message);
            Assert.True((await VerificarAsync(supervisor, Pin, cajero)).IsSuccess);
        }

        [Fact]
        public async Task MuchosIntentosALaVez_SoloCincoLlegananAComparar()
        {
            User supervisor = await SupervisorConPinAsync();
            User cajero = await CajeroAsync();

            Response<AuthorizerDTO>[] respuestas = await Task.WhenAll(
                Enumerable.Range(0, 20).Select(_ => VerificarAsync(supervisor, PinMalo, cajero)));

            Assert.All(respuestas, r => Assert.False(r.IsSuccess));
            Assert.Equal(AuthorizationPinRules.MaxFailedAttempts,
                respuestas.Count(r => r.Message!.StartsWith("PIN incorrecto")));
            Assert.Equal(AuthorizationPinRules.MaxFailedAttempts, (await FilaAsync(supervisor))!.FailedAttempts);
        }

        [Fact]
        public async Task VerificarDentroDeUnaTransaccion_EsUnErrorDeProgramacion()
        {
            User supervisor = await SupervisorConPinAsync();
            User cajero = await CajeroAsync();
            using IServiceScope scope = _servicios.CreateScope();
            DataContext context = scope.ServiceProvider.GetRequiredService<DataContext>();
            await using IDbContextTransaction transaction = await context.Database.BeginTransactionAsync();

            await Assert.ThrowsAsync<InvalidOperationException>(() => scope.ServiceProvider
                .GetRequiredService<IAuthorizationPinService>()
                .VerifyAsync(SupervisorOperation.CancelSale, supervisor.Id, Pin, cajero.UserName!));
        }

        [Fact]
        public async Task RestablecerLaContrasena_BorraElPin()
        {
            User supervisor = await SupervisorConPinAsync();
            User quienRestablece = await CajeroAsync();

            Response<TemporaryPasswordDTO> respuesta = await ConAsync<UsersService, Response<TemporaryPasswordDTO>>(
                s => s.ResetPasswordAsync(Guid.Parse(supervisor.Id), quienRestablece.UserName!));

            Assert.True(respuesta.IsSuccess, respuesta.Message);
            Assert.Null(await FilaAsync(supervisor));
        }
    }
}
