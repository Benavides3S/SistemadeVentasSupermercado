using System.Net;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Newtonsoft.Json;
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
    /// <summary>Historial de actividad de usuarios: qué se registra, agrupación de fallos y transacciones.</summary>
    [Collection(ColeccionBaseDeDatos.Nombre)]
    public class UserActivityTests : IDisposable
    {
        private const string ClaveConocida = "Clave2026Segura";

        private readonly BaseDeDatosFixture _fixture;
        private readonly ServiceProvider _servicios;
        private readonly ServiceProvider _conRegistroQueFalla;

        public UserActivityTests(BaseDeDatosFixture fixture)
        {
            _fixture = fixture;
            _servicios = Armar<UserActivityRecorder>();
            _conRegistroQueFalla = Armar<RegistroQueFalla>();
        }

        public void Dispose()
        {
            _servicios.Dispose();
            _conRegistroQueFalla.Dispose();
        }

        private ServiceProvider Armar<TRegistro>() where TRegistro : class, IUserActivityRecorder
        {
            ServiceCollection services = new();
            services.AddLogging();
            services.AddHttpContextAccessor();
            services.AddDataProtection();
            services.AddScoped(_ => _fixture.CrearContexto());
            services.AddIdentity<User, IdentityRole>(CustomConfiguration.ConfigureIdentityOptions)
                    .AddEntityFrameworkStores<DataContext>()
                    .AddDefaultTokenProviders();
            services.AddScoped<IUserActivityRecorder, TRegistro>();
            services.AddScoped<UsersService>();
            services.AddScoped<RolesService>();
            return services.BuildServiceProvider();
        }

        /// <summary>Deja pendiente una fila que la base rechaza (0 intentos): falla al guardar.</summary>
        private sealed class RegistroQueFalla(DataContext context) : IUserActivityRecorder
        {
            public Task StageAsync(UserActivityType type, string? actorUserName, User? target = null,
                string? targetName = null, string? details = null)
            {
                context.UserActivities.Add(new UserActivity { Id = Guid.NewGuid(), Type = type, AttemptCount = 0 });
                return Task.CompletedTask;
            }

            public Task RecordAccessAsync(UserActivityType type, string userName) => Task.CompletedTask;
            public Task RecordFailedLoginAsync(UserActivityType type, User? target, string? typedEmail) => Task.CompletedTask;
            public Task RecordFailedAuthorizationAsync(User? authorizer, string requesterUserName) => Task.CompletedTask;
        }

        private sealed class RelojFijo(DateTimeOffset ahora) : TimeProvider
        {
            public DateTimeOffset Ahora { get; set; } = ahora;
            public override DateTimeOffset GetUtcNow() => Ahora;
        }

        // ---------- Ayudantes ----------

        private static async Task<T> ConAsync<TServicio, T>(ServiceProvider proveedor, Func<TServicio, Task<T>> accion)
            where TServicio : notnull
        {
            using IServiceScope scope = proveedor.CreateScope();
            return await accion(scope.ServiceProvider.GetRequiredService<TServicio>());
        }

        private Task<T> UsuariosAsync<T>(Func<UsersService, Task<T>> accion) => ConAsync(_servicios, accion);
        private Task<T> RolesAsync<T>(Func<RolesService, Task<T>> accion) => ConAsync(_servicios, accion);

        private async Task<Guid> RolAsync(string nombre)
        {
            await using DataContext context = _fixture.CrearContexto();
            SistemaVentasRole? rol = await context.SistemaVentasRoles.FirstOrDefaultAsync(r => r.Name == nombre);
            if (rol is null)
            {
                rol = new SistemaVentasRole { Id = Guid.NewGuid(), Name = nombre };
                context.SistemaVentasRoles.Add(rol);
                await context.SaveChangesAsync();
            }
            return rol.Id;
        }

        private async Task<User> CrearAsync(string rol)
        {
            string marca = Guid.NewGuid().ToString("N")[..8];
            User usuario = new()
            {
                Id = Guid.NewGuid().ToString(),
                UserName = $"h-{marca}@pruebas.local",
                Email = $"h-{marca}@pruebas.local",
                Document = $"HIS-{marca}",
                FirstName = "Persona",
                LastName = marca,
                PhoneNumber = "300",
                SistemaVentasRoleId = await RolAsync(rol)
            };

            using IServiceScope scope = _servicios.CreateScope();
            IdentityResult resultado = await scope.ServiceProvider.GetRequiredService<UserManager<User>>()
                .CreateAsync(usuario, ClaveConocida);
            Assert.True(resultado.Succeeded);
            return usuario;
        }

        private Task<User> CrearAdminAsync() => CrearAsync(Env.SUPER_ADMIN_ROLE_NAME);
        private Task<User> CrearEmpleadoAsync() => CrearAsync("Empleado historial");

        private async Task<List<UserActivity>> HistorialDeAsync(string userId)
        {
            await using DataContext context = _fixture.CrearContexto();
            return await context.UserActivities.AsNoTracking()
                .Where(a => a.TargetUserId == userId).OrderBy(a => a.Number).ToListAsync();
        }

        private async Task<List<UserActivity>> ConNombreAsync(string texto)
        {
            await using DataContext context = _fixture.CrearContexto();
            return await context.UserActivities.AsNoTracking()
                .Where(a => a.TargetName == texto).OrderBy(a => a.Number).ToListAsync();
        }

        private static UserDTO Formulario(User usuario, Guid? rol = null, string? nombres = null) => new()
        {
            Id = Guid.Parse(usuario.Id), Document = usuario.Document, FirstName = nombres ?? usuario.FirstName,
            LastName = usuario.LastName, PhoneNumber = usuario.PhoneNumber!, Email = usuario.Email!,
            SistemaVentasRoleId = (rol ?? usuario.SistemaVentasRoleId).ToString()
        };

        private async Task RegistrarFalloAsync(TimeProvider reloj, string? escrito, string? ip = null, User? usuario = null)
        {
            await using DataContext context = _fixture.CrearContexto();
            HttpContextAccessor http = new();
            if (ip is not null)
            {
                DefaultHttpContext peticion = new();
                peticion.Connection.RemoteIpAddress = IPAddress.Parse(ip);
                http.HttpContext = peticion;
            }

            await new UserActivityRecorder(context, http, NullLogger<UserActivityRecorder>.Instance, reloj)
                .RecordFailedLoginAsync(UserActivityType.LoginFailed, usuario, escrito);
        }

        private static RelojFijo Reloj(int minuto = 0)
            => new(new DateTimeOffset(2020, 5, 1, 15, minuto, 20, TimeSpan.Zero));

        private static string CorreoUnico() => $"nadie-{Guid.NewGuid():N}@pruebas.local";

        // ---------- Agrupación de fallos ----------

        [Fact]
        public async Task Fallos_Simultaneos_QuedanEnUnaSolaFila_SinPerderNinguno()
        {
            for (int ronda = 0; ronda < 5; ronda++)
            {
                string correo = CorreoUnico();
                RelojFijo reloj = Reloj();

                // Cada intento con su propio contexto, como 40 peticiones a la vez. La primera tanda compite por CREAR la fila.
                await Task.WhenAll(Enumerable.Range(0, 40)
                    .Select(_ => Task.Run(() => RegistrarFalloAsync(reloj, correo, "10.0.0.7"))));

                UserActivity fila = Assert.Single(await ConNombreAsync(correo));
                Assert.Equal(40, fila.AttemptCount);
                Assert.Equal(reloj.Ahora.UtcDateTime, fila.LastAttemptAtUtc);
                Assert.Equal("10.0.0.7", fila.IpAddress);
            }
        }

        [Fact]
        public async Task Fallos_GuardanLaHoraDelPrimeroYDelUltimo_YCadaMinutoEsOtraFila()
        {
            string correo = CorreoUnico();
            RelojFijo reloj = Reloj();
            DateTime primero = reloj.Ahora.UtcDateTime;

            await RegistrarFalloAsync(reloj, correo);
            reloj.Ahora = reloj.Ahora.AddSeconds(30);
            await RegistrarFalloAsync(reloj, correo.ToUpperInvariant()); // las mayúsculas no cambian el grupo
            reloj.Ahora = reloj.Ahora.AddSeconds(-10);                    // uno que llega con la hora anterior
            await RegistrarFalloAsync(reloj, correo);

            UserActivity fila = Assert.Single(await ConNombreAsync(correo));
            Assert.Equal(3, fila.AttemptCount);
            Assert.Equal(primero, fila.OccurredAtUtc);
            Assert.Equal(primero.AddSeconds(30), fila.LastAttemptAtUtc); // no retrocede

            await RegistrarFalloAsync(Reloj(minuto: 1), correo);
            Assert.Equal(2, (await ConNombreAsync(correo)).Count);
        }

        [Fact]
        public async Task Fallos_DeOtroCorreoOtraIp_VanAparte_YLaIpv4SeGuardaLimpia()
        {
            string correo = CorreoUnico();
            RelojFijo reloj = Reloj();

            await RegistrarFalloAsync(reloj, correo, "10.0.0.1");
            await RegistrarFalloAsync(reloj, correo, "::ffff:10.0.0.1"); // la misma, como la entrega Docker
            await RegistrarFalloAsync(reloj, correo, "10.0.0.2");

            List<UserActivity> filas = await ConNombreAsync(correo);
            Assert.Equal([("10.0.0.1", 2), ("10.0.0.2", 1)], filas.Select(f => (f.IpAddress!, f.AttemptCount)));
        }

        [Fact]
        public async Task TextosSinFormaDeCorreo_NoSeGuardan_YSeAgrupanSoloPorIp()
        {
            string ip = "10.9.8.7";
            RelojFijo reloj = Reloj(minuto: 5);
            string[] escritos = ["MiClaveSecreta123", "usuario", "a@b", "Clave@2026", "con espacio@correo.co", "", "x@y.123",
                                 new string('a', 300) + "@correo.co"];

            foreach (string escrito in escritos)
                await RegistrarFalloAsync(reloj, escrito, ip);

            await using DataContext context = _fixture.CrearContexto();
            UserActivity fila = Assert.Single(await context.UserActivities.Where(a => a.IpAddress == ip).ToListAsync());
            Assert.Equal(UserActivityRecorder.InvalidUserText, fila.TargetName);
            Assert.Equal(escritos.Length, fila.AttemptCount);
            Assert.DoesNotContain("MiClaveSecreta123", fila.GroupKey);
        }

        // ---------- Usuarios y contraseñas ----------

        [Fact]
        public async Task AccionesSobreUsuarios_SeRegistran_ConQuienYSobreQuien_SinContrasenas()
        {
            User admin = await CrearAdminAsync();
            string marca = Guid.NewGuid().ToString("N")[..8];
            UserDTO nuevo = new()
            {
                Email = $"alta-{marca}@pruebas.local", Document = $"ALTA-{marca}", FirstName = "Alta", LastName = marca,
                PhoneNumber = "300", SistemaVentasRoleId = (await RolAsync("Empleado historial")).ToString()
            };

            Response<TemporaryPasswordDTO> alta = await UsuariosAsync(s => s.CreateAsync(nuevo, admin.UserName!));
            Assert.True(alta.IsSuccess, alta.Message);
            Guid id = Guid.Parse(alta.Result!.UserId);

            User creado;
            await using (DataContext context = _fixture.CrearContexto())
                creado = await context.Users.AsNoTracking().SingleAsync(u => u.Id == alta.Result.UserId);

            Guid otroRol = await RolAsync("Otro rol historial");
            Assert.True((await UsuariosAsync(s => s.EditAsync(Formulario(creado, otroRol, "Renombrada"), admin.UserName!))).IsSuccess);
            Response<TemporaryPasswordDTO> reset = await UsuariosAsync(s => s.ResetPasswordAsync(id, admin.UserName!));
            Assert.True(reset.IsSuccess, reset.Message);

            const string claveNueva = "ClaveNueva2026Propia";
            Assert.True((await UsuariosAsync(s => s.ChangePasswordAsync(creado.UserName!, new ChangePasswordDTO
            {
                CurrentPassword = reset.Result!.TemporaryPassword, NewPassword = claveNueva, ConfirmPassword = claveNueva
            }))).IsSuccess);

            await using (DataContext context = _fixture.CrearContexto())
                await context.Users.Where(u => u.Id == creado.Id)
                    .ExecuteUpdateAsync(s => s.SetProperty(u => u.LockoutEnd, DateTimeOffset.UtcNow.AddMinutes(10)));
            Assert.True((await UsuariosAsync(s => s.UnlockAsync(id, admin.UserName!))).IsSuccess);
            Assert.True((await UsuariosAsync(s => s.DeactivateAsync(id, admin.UserName!))).IsSuccess);
            Assert.True((await UsuariosAsync(s => s.ReactivateAsync(id, admin.UserName!))).IsSuccess);

            List<UserActivity> historial = await HistorialDeAsync(creado.Id);

            Assert.Equal(
            [
                UserActivityType.UserCreated, UserActivityType.UserRoleChanged, UserActivityType.UserEdited,
                UserActivityType.PasswordReset, UserActivityType.PasswordChanged, UserActivityType.UserUnlocked,
                UserActivityType.UserDeactivated, UserActivityType.UserReactivated
            ], historial.Select(a => a.Type));

            // El cambio propio lo hace el mismo usuario; todo lo demás, el Admin.
            Assert.All(historial, a => Assert.Equal(
                a.Type == UserActivityType.PasswordChanged ? creado.Id : admin.Id, a.ActorUserId));
            Assert.Contains(admin.Email!, historial[0].ActorName);
            Assert.Contains(creado.Email!, historial[0].TargetName);
            Assert.Equal("Rol: Empleado historial → Otro rol historial", historial[1].Details);
            Assert.Equal("Cambió: Nombres", historial[2].Details);

            string todo = JsonConvert.SerializeObject(historial.Select(a => new { a.ActorName, a.TargetName, a.Details, a.GroupKey }));
            Assert.DoesNotContain(alta.Result.TemporaryPassword, todo);
            Assert.DoesNotContain(reset.Result!.TemporaryPassword, todo);
            Assert.DoesNotContain(claveNueva, todo);
        }

        [Fact]
        public async Task LoRechazado_YGuardarSinCambios_NoDejanRegistro()
        {
            User admin = await CrearAdminAsync();
            User empleado = await CrearEmpleadoAsync();
            Guid id = Guid.Parse(empleado.Id);

            Assert.False((await UsuariosAsync(s => s.DeactivateAsync(Guid.Parse(admin.Id), empleado.UserName!))).IsSuccess);
            Assert.False((await UsuariosAsync(s => s.ResetPasswordAsync(Guid.Parse(admin.Id), empleado.UserName!))).IsSuccess);
            Assert.False((await UsuariosAsync(s => s.UnlockAsync(id, admin.UserName!))).IsSuccess);     // no está bloqueado
            Assert.False((await UsuariosAsync(s => s.ReactivateAsync(id, admin.UserName!))).IsSuccess); // ya está activo
            Assert.False((await UsuariosAsync(s => s.ChangePasswordAsync(empleado.UserName!, new ChangePasswordDTO
            {
                CurrentPassword = "no es la clave", NewPassword = "OtraClave2026x", ConfirmPassword = "OtraClave2026x"
            }))).IsSuccess);
            Assert.True((await UsuariosAsync(s => s.EditAsync(Formulario(empleado), admin.UserName!))).IsSuccess);

            Assert.Empty(await HistorialDeAsync(empleado.Id));
            Assert.Empty(await HistorialDeAsync(admin.Id));
        }

        [Fact]
        public async Task MiPerfil_SeRegistraSoloSiCambiaAlgo()
        {
            User empleado = await CrearEmpleadoAsync();
            AccountUserDTO perfil = new()
            {
                Document = empleado.Document, FirstName = empleado.FirstName, LastName = empleado.LastName, PhoneNumber = "300"
            };

            Assert.True((await UsuariosAsync(s => s.UpdateUserAsync(perfil, empleado.UserName!))).IsSuccess);
            Assert.Empty(await HistorialDeAsync(empleado.Id));

            perfil.PhoneNumber = "311";
            Assert.True((await UsuariosAsync(s => s.UpdateUserAsync(perfil, empleado.UserName!))).IsSuccess);

            UserActivity fila = Assert.Single(await HistorialDeAsync(empleado.Id));
            Assert.Equal(UserActivityType.ProfileUpdated, fila.Type);
            Assert.Equal("Cambió: Teléfono", fila.Details);
        }

        [Fact]
        public async Task SiElRegistroFalla_ElCambioTambienSeDeshace()
        {
            User admin = await CrearAdminAsync();
            User empleado = await CrearEmpleadoAsync();
            User bloqueado = await CrearEmpleadoAsync();
            Guid id = Guid.Parse(empleado.Id);
            await using (DataContext context = _fixture.CrearContexto())
                await context.Users.Where(u => u.Id == bloqueado.Id)
                    .ExecuteUpdateAsync(s => s.SetProperty(u => u.LockoutEnd, DateTimeOffset.UtcNow.AddMinutes(10)));

            User antes;
            await using (DataContext context = _fixture.CrearContexto())
                antes = await context.Users.AsNoTracking().SingleAsync(u => u.Id == empleado.Id);

            string correoNuevo = CorreoUnico();
            UserDTO nuevo = new()
            {
                Email = correoNuevo, Document = $"F-{Guid.NewGuid().ToString("N")[..8]}", FirstName = "No", LastName = "Queda",
                PhoneNumber = "300", SistemaVentasRoleId = empleado.SistemaVentasRoleId.ToString()
            };

            Task<bool> ConFalla(Func<UsersService, Task<bool>> accion) => ConAsync(_conRegistroQueFalla, accion);

            Assert.False(await ConFalla(async s => (await s.CreateAsync(nuevo, admin.UserName!)).IsSuccess));
            Assert.False(await ConFalla(async s => (await s.ResetPasswordAsync(id, admin.UserName!)).IsSuccess));
            Assert.False(await ConFalla(async s => (await s.ChangePasswordAsync(empleado.UserName!, new ChangePasswordDTO
            {
                CurrentPassword = ClaveConocida, NewPassword = "OtraClave2026x", ConfirmPassword = "OtraClave2026x"
            })).IsSuccess));
            Assert.False(await ConFalla(async s => (await s.EditAsync(Formulario(empleado, nombres: "Cambiado"), admin.UserName!)).IsSuccess));
            Assert.False(await ConFalla(async s => (await s.UpdateUserAsync(new AccountUserDTO
            {
                Document = empleado.Document, FirstName = "Cambiado", LastName = empleado.LastName, PhoneNumber = "300"
            }, empleado.UserName!)).IsSuccess));
            Assert.False(await ConFalla(async s => (await s.DeactivateAsync(id, admin.UserName!)).IsSuccess));
            Assert.False(await ConFalla(async s => (await s.UnlockAsync(Guid.Parse(bloqueado.Id), admin.UserName!)).IsSuccess));

            await using DataContext final = _fixture.CrearContexto();
            Assert.False(await final.Users.AnyAsync(u => u.Email == correoNuevo));
            User despues = await final.Users.AsNoTracking().SingleAsync(u => u.Id == empleado.Id);
            Assert.Equal(antes.PasswordHash, despues.PasswordHash);
            Assert.Equal(antes.SecurityStamp, despues.SecurityStamp);
            Assert.Equal(antes.FirstName, despues.FirstName);
            Assert.False(despues.MustChangePassword);
            Assert.Null(despues.DeactivatedAt);
            Assert.NotNull((await final.Users.AsNoTracking().SingleAsync(u => u.Id == bloqueado.Id)).LockoutEnd);
        }

        // ---------- Roles ----------

        private async Task<(Guid Id, string Texto)> PermisoAsync(string descripcion)
        {
            await using DataContext context = _fixture.CrearContexto();
            string marca = Guid.NewGuid().ToString("N")[..8];
            Permission permiso = new() { Id = Guid.NewGuid(), Name = $"p{marca}", Description = descripcion, Module = "Historial" };
            context.Permissions.Add(permiso);
            await context.SaveChangesAsync();
            return (permiso.Id, $"Historial: {descripcion}");
        }

        [Fact]
        public async Task Roles_RegistranCreacion_PermisosAgregadosYQuitados_YEliminacion()
        {
            (Guid ver, string textoVer) = await PermisoAsync("Ver algo");
            (Guid borrar, string textoBorrar) = await PermisoAsync("Borrar algo");
            string nombre = $"Rol h {Guid.NewGuid().ToString("N")[..8]}";

            Assert.True((await RolesAsync(s => s.CreateAsync(new SistemaVentaRoleDTO
            {
                Name = nombre, PermissionIds = JsonConvert.SerializeObject(new[] { ver })
            }))).IsSuccess);

            Guid id;
            await using (DataContext context = _fixture.CrearContexto())
                id = await context.SistemaVentasRoles.Where(r => r.Name == nombre).Select(r => r.Id).SingleAsync();

            SistemaVentaRoleDTO cambio = new() { Id = id, Name = nombre, PermissionIds = JsonConvert.SerializeObject(new[] { borrar }) };
            Assert.True((await RolesAsync(s => s.EditAsync(cambio))).IsSuccess);
            Assert.True((await RolesAsync(s => s.EditAsync(cambio))).IsSuccess); // sin cambios: no se registra
            Assert.True((await RolesAsync(s => s.DeleteAsync(id))).IsSuccess);

            List<UserActivity> historial = await ConNombreAsync(nombre);
            Assert.Equal([UserActivityType.RoleCreated, UserActivityType.RoleEdited, UserActivityType.RoleDeleted],
                historial.Select(a => a.Type));
            Assert.Equal($"Permisos: {textoVer}", historial[0].Details);
            Assert.Equal($"Permisos agregados: {textoBorrar}. Permisos quitados: {textoVer}", historial[1].Details);
        }

        [Fact]
        public async Task Roles_SiElRegistroFalla_NoCambiaNada()
        {
            string nombre = $"Rol f {Guid.NewGuid().ToString("N")[..8]}";
            Guid id = await RolAsync(nombre);

            Task<bool> ConFalla(Func<RolesService, Task<bool>> accion) => ConAsync(_conRegistroQueFalla, accion);

            Assert.False(await ConFalla(async s => (await s.CreateAsync(new SistemaVentaRoleDTO { Name = nombre + " 2" })).IsSuccess));
            Assert.False(await ConFalla(async s => (await s.EditAsync(new SistemaVentaRoleDTO { Id = id, Name = nombre + " 3" })).IsSuccess));
            Assert.False(await ConFalla(async s => (await s.DeleteAsync(id)).IsSuccess));

            await using DataContext context = _fixture.CrearContexto();
            Assert.Equal([nombre], await context.SistemaVentasRoles.Where(r => r.Name.StartsWith(nombre)).Select(r => r.Name).ToListAsync());
        }

        // ---------- Consulta ----------

        [Fact]
        public async Task Consulta_FiltraPorTipo_DiasDeColombia_YTexto()
        {
            string correo = CorreoUnico();
            // 1 de marzo de 2020 a las 04:59 UTC todavía es 29 de febrero en Colombia; a las 05:00 ya es 1 de marzo.
            await RegistrarFalloAsync(new RelojFijo(new DateTimeOffset(2020, 3, 1, 4, 59, 0, TimeSpan.Zero)), correo);
            await RegistrarFalloAsync(new RelojFijo(new DateTimeOffset(2020, 3, 1, 5, 0, 0, TimeSpan.Zero)), correo);

            async Task<int> ContarAsync(UserActivityQueryDTO consulta)
            {
                await using DataContext context = _fixture.CrearContexto();
                var respuesta = await new UserActivityService(context).GetPaginatedListAsync(consulta);
                Assert.True(respuesta.IsSuccess, respuesta.Message);
                return respuesta.Result!.TotalCount;
            }

            DateOnly febrero = new(2020, 2, 29), marzo = new(2020, 3, 1);
            Assert.Equal(2, await ContarAsync(new() { Filter = correo }));
            Assert.Equal(1, await ContarAsync(new() { Filter = correo, From = marzo }));
            Assert.Equal(1, await ContarAsync(new() { Filter = correo, To = febrero }));
            Assert.Equal(2, await ContarAsync(new() { Filter = correo, From = febrero, To = marzo }));
            Assert.Equal(2, await ContarAsync(new() { Filter = correo, Type = UserActivityType.LoginFailed }));
            Assert.Equal(0, await ContarAsync(new() { Filter = correo, Type = UserActivityType.Logout }));

            await using DataContext otro = _fixture.CrearContexto();
            Assert.False((await new UserActivityService(otro).GetPaginatedListAsync(new() { From = marzo, To = febrero })).IsSuccess);
        }
    }
}
