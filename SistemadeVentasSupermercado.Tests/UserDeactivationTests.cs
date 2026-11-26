using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using SistemadeVentasSupermercado.Tests.Infraestructura;
using SistemadeVentasSupermercado.Web;
using SistemadeVentasSupermercado.Web.Core;
using SistemadeVentasSupermercado.Web.Core.Pagination;
using SistemadeVentasSupermercado.Web.Data;
using SistemadeVentasSupermercado.Web.Data.Entities;
using SistemadeVentasSupermercado.Web.DTOs;
using SistemadeVentasSupermercado.Web.Services.Abstractions;
using SistemadeVentasSupermercado.Web.Services.Implementations;

namespace SistemadeVentasSupermercado.Tests
{
    /// <summary>Reglas para desactivar, reactivar y desbloquear usuarios.</summary>
    [Collection(ColeccionBaseDeDatos.Nombre)]
    public class UserDeactivationTests : IAsyncLifetime
    {
        private readonly BaseDeDatosFixture _fixture;
        private readonly ServiceProvider _servicios;

        public UserDeactivationTests(BaseDeDatosFixture fixture)
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
            services.AddScoped<IUserActivityRecorder, UserActivityRecorder>();
            services.AddScoped<UsersService>();
            _servicios = services.BuildServiceProvider();
        }

        // Cada prueba parte sin otros administradores activos (otras clases dejan los suyos en la base).
        public async Task InitializeAsync() => await DesactivarTodosLosAdminAsync();

        public async Task DisposeAsync()
        {
            await DesactivarTodosLosAdminAsync();
            await _servicios.DisposeAsync();
        }

        private async Task DesactivarTodosLosAdminAsync()
        {
            await using DataContext context = _fixture.CrearContexto();
            DateTime ahora = DateTime.UtcNow;
            await context.Users
                .Where(u => u.SistemaVentasRole!.Name == Env.SUPER_ADMIN_ROLE_NAME && u.DeactivatedAt == null)
                .ExecuteUpdateAsync(s => s.SetProperty(u => u.DeactivatedAt, ahora));
        }

        // ---------- Ayudantes ----------

        private async Task<T> ConServicioAsync<T>(Func<UsersService, Task<T>> accion)
        {
            using IServiceScope scope = _servicios.CreateScope();
            return await accion(scope.ServiceProvider.GetRequiredService<UsersService>());
        }

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
                UserName = $"d-{marca}@pruebas.local",
                Email = $"d-{marca}@pruebas.local",
                Document = $"DES-{marca}",
                FirstName = "Usuario",
                LastName = marca,
                SistemaVentasRoleId = await RolAsync(rol)
            };

            await using DataContext context = _fixture.CrearContexto();
            context.Users.Add(usuario);
            usuario.NormalizedEmail = usuario.Email.ToUpperInvariant();
            usuario.NormalizedUserName = usuario.UserName.ToUpperInvariant();
            usuario.SecurityStamp = Guid.NewGuid().ToString();
            await context.SaveChangesAsync();
            return usuario;
        }

        private Task<User> CrearAdminAsync() => CrearAsync(Env.SUPER_ADMIN_ROLE_NAME);
        private Task<User> CrearCajeroAsync() => CrearAsync("Cajero desactivar");

        private async Task<User> LeerAsync(User usuario)
        {
            await using DataContext context = _fixture.CrearContexto();
            return await context.Users.AsNoTracking().SingleAsync(u => u.Id == usuario.Id);
        }

        private async Task<int> AdminsActivosAsync()
        {
            await using DataContext context = _fixture.CrearContexto();
            return await context.Users.CountAsync(u => u.SistemaVentasRole!.Name == Env.SUPER_ADMIN_ROLE_NAME
                                                       && u.DeactivatedAt == null);
        }

        private Task<Response<UserDeactivationDTO>> DesactivarAsync(User usuario, User quien)
            => ConServicioAsync(s => s.DeactivateAsync(Guid.Parse(usuario.Id), quien.UserName!));

        private async Task BloquearAsync(User usuario)
        {
            await using DataContext context = _fixture.CrearContexto();
            await context.Users.Where(u => u.Id == usuario.Id).ExecuteUpdateAsync(s => s
                .SetProperty(u => u.LockoutEnd, DateTimeOffset.UtcNow.AddMinutes(10))
                .SetProperty(u => u.LockoutEnabled, true));
        }

        // ---------- Desactivar ----------

        [Fact]
        public async Task Desactivar_GuardaLaFecha_YCambiaLosSellos()
        {
            User admin = await CrearAdminAsync();
            User cajero = await CrearCajeroAsync();
            User antes = await LeerAsync(cajero);

            Response<UserDeactivationDTO> respuesta = await DesactivarAsync(cajero, admin);

            Assert.True(respuesta.IsSuccess, respuesta.Message);
            Assert.Null(respuesta.Result!.OpenCashSessionNumber);
            User despues = await LeerAsync(cajero);
            Assert.NotNull(despues.DeactivatedAt);
            Assert.NotEqual(antes.SecurityStamp, despues.SecurityStamp);
            Assert.NotEqual(antes.ConcurrencyStamp, despues.ConcurrencyStamp);
        }

        [Fact]
        public async Task Desactivar_ConCajaAbierta_SePermiteYDevuelveElTurno()
        {
            User admin = await CrearAdminAsync();
            User cajero = await CrearCajeroAsync();
            CashSessionDTO turno;
            await using (DataContext context = _fixture.CrearContexto())
                turno = (await new CashSessionService(context).OpenAsync(cajero.Id, new OpenCashSessionDTO())).Result!;

            Response<UserDeactivationDTO> respuesta = await DesactivarAsync(cajero, admin);

            Assert.True(respuesta.IsSuccess, respuesta.Message);
            Assert.Equal(turno.Number, respuesta.Result!.OpenCashSessionNumber);
            Assert.Equal(turno.OpenedAtUtc, respuesta.Result.OpenCashSessionOpenedAtUtc);

            // El turno sigue abierto, marcado, y otra persona puede cerrarlo.
            await using DataContext otro = _fixture.CrearContexto();
            CashSessionService caja = new(otro);
            Response<CashSessionDTO> cierre = await caja.CloseAsync(turno.Id,
                new CloseCashSessionDTO { CountedAmount = 0m }, admin.Id, canCloseOthers: true);
            Assert.True(cierre.IsSuccess, cierre.Message);
            Assert.True(cierre.Result!.CashierDeactivated);
        }

        [Fact]
        public async Task NadiePuedeDesactivarseASiMismo()
        {
            User admin = await CrearAdminAsync();
            await CrearAdminAsync();

            Response<UserDeactivationDTO> respuesta = await DesactivarAsync(admin, admin);

            Assert.False(respuesta.IsSuccess);
            Assert.Null((await LeerAsync(admin)).DeactivatedAt);
        }

        [Fact]
        public async Task SoloUnAdminDesactivaAUnAdmin()
        {
            User admin = await CrearAdminAsync();
            await CrearAdminAsync();
            User cajero = await CrearCajeroAsync();

            Response<UserDeactivationDTO> respuesta = await DesactivarAsync(admin, cajero);

            Assert.False(respuesta.IsSuccess);
            Assert.Contains("Solo un usuario", respuesta.Message);
            Assert.Null((await LeerAsync(admin)).DeactivatedAt);
        }

        [Fact]
        public async Task QuienEstaDesactivado_NoPuedeDesactivarANadie()
        {
            User admin = await CrearAdminAsync();
            User otro = await CrearAdminAsync();
            User cajero = await CrearCajeroAsync();
            Assert.True((await DesactivarAsync(otro, admin)).IsSuccess);

            Assert.False((await DesactivarAsync(cajero, otro)).IsSuccess);
            Assert.False((await DesactivarAsync(admin, otro)).IsSuccess);
            Assert.Equal(1, await AdminsActivosAsync());
        }

        [Fact]
        public async Task DosAdminQueSeDesactivanALaVez_SiempreQuedaUnoActivo()
        {
            for (int intento = 0; intento < 15; intento++)
            {
                await DesactivarTodosLosAdminAsync();
                User a = await CrearAdminAsync();
                User b = await CrearAdminAsync();

                Response<UserDeactivationDTO>[] respuestas = await Task.WhenAll(
                    Task.Run(() => DesactivarAsync(a, b)),
                    Task.Run(() => DesactivarAsync(b, a)));

                Assert.Single(respuestas, r => r.IsSuccess);
                Assert.Equal(1, await AdminsActivosAsync());
            }
        }

        [Fact]
        public async Task DesactivarYQuitarElRolALaVez_SiempreQuedaUnAdminActivo()
        {
            Guid rolCajero = await RolAsync("Cajero desactivar");

            for (int intento = 0; intento < 15; intento++)
            {
                await DesactivarTodosLosAdminAsync();
                User a = await CrearAdminAsync();
                User b = await CrearAdminAsync();
                UserDTO quitarRolAA = new()
                {
                    Id = Guid.Parse(a.Id), Document = a.Document, FirstName = a.FirstName, LastName = a.LastName,
                    PhoneNumber = "300", Email = a.Email!, SistemaVentasRoleId = rolCajero.ToString()
                };

                bool[] resultados = await Task.WhenAll(
                    Task.Run(async () => (await DesactivarAsync(b, a)).IsSuccess),
                    Task.Run(async () => (await ConServicioAsync(s => s.EditAsync(quitarRolAA, b.UserName!))).IsSuccess));

                Assert.Single(resultados, ok => ok);
                Assert.Equal(1, await AdminsActivosAsync());
            }
        }

        [Fact]
        public async Task UnAdminDesactivado_NoCuentaComoAdmin()
        {
            User admin = await CrearAdminAsync();
            User desactivado = await CrearAdminAsync();
            Assert.True((await DesactivarAsync(desactivado, admin)).IsSuccess);
            User cajero = await CrearCajeroAsync();

            // Tampoco para las demás acciones reservadas a un Admin.
            Response<TemporaryPasswordDTO> respuesta = await ConServicioAsync(
                s => s.ResetPasswordAsync(Guid.Parse(admin.Id), desactivado.UserName!));
            Assert.False(respuesta.IsSuccess);
            Assert.False((await ConServicioAsync(s => s.ReactivateAsync(Guid.Parse(desactivado.Id), cajero.UserName!))).IsSuccess);
        }

        // ---------- Reactivar, restablecer y desbloquear ----------

        [Fact]
        public async Task Reactivar_QuitaLaFechaYElBloqueo_YSoloUnAdminReactivaAUnAdmin()
        {
            User admin = await CrearAdminAsync();
            User otro = await CrearAdminAsync();
            User cajero = await CrearCajeroAsync();
            Assert.True((await DesactivarAsync(otro, admin)).IsSuccess);
            await BloquearAsync(otro);

            Assert.False((await ConServicioAsync(s => s.ReactivateAsync(Guid.Parse(otro.Id), cajero.UserName!))).IsSuccess);
            Assert.NotNull((await LeerAsync(otro)).DeactivatedAt);

            Response<string> respuesta = await ConServicioAsync(s => s.ReactivateAsync(Guid.Parse(otro.Id), admin.UserName!));

            Assert.True(respuesta.IsSuccess, respuesta.Message);
            User guardado = await LeerAsync(otro);
            Assert.Null(guardado.DeactivatedAt);
            Assert.Null(guardado.LockoutEnd);
        }

        [Fact]
        public async Task RestablecerLaContrasena_NoReactiva()
        {
            User admin = await CrearAdminAsync();
            User cajero = await CrearCajeroAsync();
            Assert.True((await DesactivarAsync(cajero, admin)).IsSuccess);

            Response<TemporaryPasswordDTO> respuesta = await ConServicioAsync(
                s => s.ResetPasswordAsync(Guid.Parse(cajero.Id), admin.UserName!));

            Assert.True(respuesta.IsSuccess, respuesta.Message);
            Assert.NotNull((await LeerAsync(cajero)).DeactivatedAt);
        }

        [Fact]
        public async Task Desbloquear_QuitaElBloqueoSinCambiarLaContrasena()
        {
            User admin = await CrearAdminAsync();
            User cajero = await CrearCajeroAsync();
            await BloquearAsync(cajero);
            User antes = await LeerAsync(cajero);

            Response<string> respuesta = await ConServicioAsync(s => s.UnlockAsync(Guid.Parse(cajero.Id), admin.UserName!));

            Assert.True(respuesta.IsSuccess, respuesta.Message);
            User despues = await LeerAsync(cajero);
            Assert.Null(despues.LockoutEnd);
            Assert.Equal(antes.PasswordHash, despues.PasswordHash);
        }

        [Fact]
        public async Task SoloUnAdminDesbloqueaAUnAdmin()
        {
            User admin = await CrearAdminAsync();
            User cajero = await CrearCajeroAsync();
            await BloquearAsync(admin);

            Response<string> respuesta = await ConServicioAsync(s => s.UnlockAsync(Guid.Parse(admin.Id), cajero.UserName!));

            Assert.False(respuesta.IsSuccess);
            Assert.NotNull((await LeerAsync(admin)).LockoutEnd);
        }

        [Fact]
        public async Task Lista_FiltraPorEstado_YTraeElRol()
        {
            User admin = await CrearAdminAsync();
            User bloqueado = await CrearCajeroAsync();
            User desactivado = await CrearCajeroAsync();
            await BloquearAsync(bloqueado);
            Assert.True((await DesactivarAsync(desactivado, admin)).IsSuccess);

            async Task<List<UserDTO>> BuscarAsync(User usuario, UserStatus? estado)
                => (await ConServicioAsync(s => s.GetPaginatedListAsync(
                        new PaginationRequest { Filter = usuario.Email }, estado))).Result!.List.ToList();

            UserDTO fila = Assert.Single(await BuscarAsync(bloqueado, UserStatus.Locked));
            Assert.Equal(UserStatus.Locked, fila.Status);
            Assert.Equal("Cajero desactivar", fila.RoleName);
            Assert.Empty(await BuscarAsync(bloqueado, UserStatus.Active));

            Assert.Equal(UserStatus.Deactivated, Assert.Single(await BuscarAsync(desactivado, null)).Status);
            Assert.Single(await BuscarAsync(desactivado, UserStatus.Deactivated));
            Assert.Empty(await BuscarAsync(desactivado, UserStatus.Active));
            Assert.Single(await BuscarAsync(admin, UserStatus.Active));
        }
    }
}
