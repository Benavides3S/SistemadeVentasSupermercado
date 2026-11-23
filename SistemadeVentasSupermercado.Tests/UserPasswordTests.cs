using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using SistemadeVentasSupermercado.Tests.Infraestructura;
using SistemadeVentasSupermercado.Web;
using SistemadeVentasSupermercado.Web.Core;
using SistemadeVentasSupermercado.Web.Data;
using SistemadeVentasSupermercado.Web.Data.Entities;
using SistemadeVentasSupermercado.Web.DTOs;
using SistemadeVentasSupermercado.Web.Services.Implementations;

namespace SistemadeVentasSupermercado.Tests
{
    /// <summary>
    /// Contraseñas: temporal al crear y al restablecer, cambio obligatorio, cambio por el propio usuario
    /// y bloqueo por intentos fallidos. Identity se arma con la MISMA política de la aplicación
    /// (CustomConfiguration.ConfigureIdentityOptions).
    /// </summary>
    [Collection(ColeccionBaseDeDatos.Nombre)]
    public class UserPasswordTests : IDisposable
    {
        private const string ClaveConocida = "Clave2026Segura";

        private readonly BaseDeDatosFixture _fixture;
        private readonly ServiceProvider _servicios;

        public UserPasswordTests(BaseDeDatosFixture fixture)
        {
            _fixture = fixture;

            ServiceCollection services = new ServiceCollection();
            services.AddLogging();
            services.AddHttpContextAccessor();
            services.AddDataProtection();
            services.AddScoped(_ => _fixture.CrearContexto());
            services.AddIdentity<User, IdentityRole>(CustomConfiguration.ConfigureIdentityOptions)
                    .AddEntityFrameworkStores<DataContext>()
                    .AddDefaultTokenProviders();
            services.AddScoped<UsersService>();
            _servicios = services.BuildServiceProvider();
        }

        public void Dispose() => _servicios.Dispose();

        // ---------- Ayudantes ----------

        /// <summary>Cada llamada usa su propio alcance (contexto nuevo), como una petición web.</summary>
        private async Task<T> ConServicioAsync<T>(Func<UsersService, UserManager<User>, Task<T>> accion)
        {
            using IServiceScope scope = _servicios.CreateScope();
            return await accion(scope.ServiceProvider.GetRequiredService<UsersService>(),
                                scope.ServiceProvider.GetRequiredService<UserManager<User>>());
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

        private Task<Guid> RolAdminAsync() => RolAsync(Env.SUPER_ADMIN_ROLE_NAME);
        private Task<Guid> RolCajeroAsync() => RolAsync("Cajero contraseñas");

        /// <summary>Crea un usuario con una contraseña conocida (como si ya la hubiera cambiado).</summary>
        private async Task<User> CrearUsuarioAsync(Guid rolId, bool debeCambiar = false)
        {
            string marca = Guid.NewGuid().ToString("N")[..8];
            User usuario = new User
            {
                Id = Guid.NewGuid().ToString(),
                UserName = $"u-{marca}@pruebas.local",
                Email = $"u-{marca}@pruebas.local",
                EmailConfirmed = true,
                Document = $"DOC-{marca}",
                FirstName = "Usuario",
                LastName = marca,
                SistemaVentasRoleId = rolId,
                MustChangePassword = debeCambiar
            };

            IdentityResult resultado = await ConServicioAsync((_, um) => um.CreateAsync(usuario, ClaveConocida));
            Assert.True(resultado.Succeeded, string.Join(" ", resultado.Errors.Select(e => e.Description)));
            return usuario;
        }

        private static UserDTO NuevoUsuario(Guid rolId)
        {
            string marca = Guid.NewGuid().ToString("N")[..8];
            return new UserDTO
            {
                Email = $"nuevo-{marca}@pruebas.local",
                Document = $"NUEVO-{marca}",
                FirstName = "Nuevo",
                LastName = marca,
                PhoneNumber = "3000000000",
                SistemaVentasRoleId = rolId.ToString()
            };
        }

        private async Task<User> LeerUsuarioAsync(string id)
        {
            await using DataContext context = _fixture.CrearContexto();
            return await context.Users.AsNoTracking().SingleAsync(u => u.Id == id);
        }

        private Task<bool> ClaveFuncionaAsync(string userId, string clave)
            => ConServicioAsync(async (_, um) => await um.CheckPasswordAsync((await um.FindByIdAsync(userId))!, clave));

        // ---------- Generador ----------

        [Fact]
        public async Task La_contrasena_generada_siempre_cumple_la_politica()
        {
            using IServiceScope scope = _servicios.CreateScope();
            UserManager<User> um = scope.ServiceProvider.GetRequiredService<UserManager<User>>();
            User cualquiera = new User
            {
                Document = "x", FirstName = "x", LastName = "x", SistemaVentasRoleId = Guid.Empty, UserName = "x@x.co"
            };

            HashSet<string> generadas = new HashSet<string>();
            for (int i = 0; i < 500; i++)
            {
                string clave = PasswordGenerator.Generate();
                generadas.Add(clave);

                Assert.Equal(PasswordGenerator.DefaultLength, clave.Length);
                Assert.DoesNotContain(clave, c => "IOl01".Contains(c)); // sin caracteres confusos

                foreach (IPasswordValidator<User> validador in um.PasswordValidators)
                {
                    IdentityResult r = await validador.ValidateAsync(um, cualquiera, clave);
                    Assert.True(r.Succeeded, $"'{clave}': " + string.Join(" ", r.Errors.Select(e => e.Description)));
                }
            }

            Assert.Equal(500, generadas.Count); // no se repiten
        }

        // ---------- Crear usuario ----------

        [Fact]
        public async Task Crear_usuario_genera_contrasena_temporal_y_obliga_a_cambiarla()
        {
            User admin = await CrearUsuarioAsync(await RolAdminAsync());
            UserDTO dto = NuevoUsuario(await RolCajeroAsync());

            Response<TemporaryPasswordDTO> respuesta = await ConServicioAsync((s, _) => s.CreateAsync(dto, admin.UserName!));

            Assert.True(respuesta.IsSuccess, respuesta.Message + " " + string.Join(" ", respuesta.Errors ?? new List<string>()));
            TemporaryPasswordDTO temporal = respuesta.Result!;
            Assert.True(temporal.IsNewUser);
            Assert.Equal(dto.Email, temporal.Email);
            Assert.NotEqual(dto.Document, temporal.TemporaryPassword); // ya no es el documento

            Assert.True(await ClaveFuncionaAsync(temporal.UserId, temporal.TemporaryPassword));
            Assert.False(await ClaveFuncionaAsync(temporal.UserId, dto.Document));

            User guardado = await LeerUsuarioAsync(temporal.UserId);
            Assert.True(guardado.MustChangePassword);
            Assert.True(guardado.LockoutEnabled);
            Assert.True(await ConServicioAsync((s, _) => s.MustChangePasswordAsync(guardado.UserName!)));
        }

        [Fact]
        public async Task Quien_no_es_Admin_no_puede_crear_un_Admin()
        {
            User cajero = await CrearUsuarioAsync(await RolCajeroAsync());
            UserDTO dto = NuevoUsuario(await RolAdminAsync());

            Response<TemporaryPasswordDTO> respuesta = await ConServicioAsync((s, _) => s.CreateAsync(dto, cajero.UserName!));

            Assert.False(respuesta.IsSuccess);
            Assert.Contains("Solo un usuario con rol 'Admin'", respuesta.Message);
            await using DataContext context = _fixture.CrearContexto();
            Assert.False(await context.Users.AnyAsync(u => u.Email == dto.Email));
        }

        [Fact]
        public async Task Un_Admin_si_puede_crear_otro_Admin()
        {
            User admin = await CrearUsuarioAsync(await RolAdminAsync());

            Response<TemporaryPasswordDTO> respuesta =
                await ConServicioAsync(async (s, _) => await s.CreateAsync(NuevoUsuario(await RolAdminAsync()), admin.UserName!));

            Assert.True(respuesta.IsSuccess, respuesta.Message);
        }

        [Fact]
        public async Task Crear_usuario_con_rol_inexistente_falla()
        {
            User admin = await CrearUsuarioAsync(await RolAdminAsync());

            Response<TemporaryPasswordDTO> respuesta =
                await ConServicioAsync((s, _) => s.CreateAsync(NuevoUsuario(Guid.NewGuid()), admin.UserName!));

            Assert.False(respuesta.IsSuccess);
            Assert.Contains("rol válido", respuesta.Message);
        }

        // ---------- Restablecer ----------

        [Fact]
        public async Task Restablecer_pone_una_temporal_nueva_desbloquea_y_obliga_a_cambiarla()
        {
            User admin = await CrearUsuarioAsync(await RolAdminAsync());
            User cajero = await CrearUsuarioAsync(await RolCajeroAsync());

            // La cuenta del cajero estaba bloqueada.
            await using (DataContext context = _fixture.CrearContexto())
            {
                await context.Users.Where(u => u.Id == cajero.Id).ExecuteUpdateAsync(s => s
                    .SetProperty(u => u.LockoutEnd, DateTimeOffset.UtcNow.AddMinutes(10))
                    .SetProperty(u => u.AccessFailedCount, 3));
            }

            Response<TemporaryPasswordDTO> respuesta =
                await ConServicioAsync((s, _) => s.ResetPasswordAsync(Guid.Parse(cajero.Id), admin.UserName!));

            Assert.True(respuesta.IsSuccess, respuesta.Message);
            Assert.False(respuesta.Result!.IsNewUser);

            Assert.False(await ClaveFuncionaAsync(cajero.Id, ClaveConocida));                  // la anterior ya no sirve
            Assert.True(await ClaveFuncionaAsync(cajero.Id, respuesta.Result.TemporaryPassword));

            User guardado = await LeerUsuarioAsync(cajero.Id);
            Assert.True(guardado.MustChangePassword);
            Assert.Null(guardado.LockoutEnd);
            Assert.Equal(0, guardado.AccessFailedCount);
        }

        [Fact]
        public async Task Quien_no_es_Admin_no_puede_restablecer_a_un_Admin()
        {
            User admin = await CrearUsuarioAsync(await RolAdminAsync());
            User cajero = await CrearUsuarioAsync(await RolCajeroAsync());

            Response<TemporaryPasswordDTO> respuesta =
                await ConServicioAsync((s, _) => s.ResetPasswordAsync(Guid.Parse(admin.Id), cajero.UserName!));

            Assert.False(respuesta.IsSuccess);
            Assert.Contains("Solo un usuario con rol 'Admin'", respuesta.Message);
            Assert.True(await ClaveFuncionaAsync(admin.Id, ClaveConocida)); // la clave del Admin no cambió
            Assert.False((await LeerUsuarioAsync(admin.Id)).MustChangePassword);
        }

        [Fact]
        public async Task Un_Admin_si_puede_restablecer_a_otro_Admin()
        {
            User admin = await CrearUsuarioAsync(await RolAdminAsync());
            User otroAdmin = await CrearUsuarioAsync(await RolAdminAsync());

            Response<TemporaryPasswordDTO> respuesta =
                await ConServicioAsync((s, _) => s.ResetPasswordAsync(Guid.Parse(otroAdmin.Id), admin.UserName!));

            Assert.True(respuesta.IsSuccess, respuesta.Message);
        }

        // ---------- Cambiar contraseña ----------

        [Fact]
        public async Task Cambiar_contrasena_apaga_el_cambio_obligatorio()
        {
            User usuario = await CrearUsuarioAsync(await RolCajeroAsync(), debeCambiar: true);

            Response<User> respuesta = await ConServicioAsync((s, _) => s.ChangePasswordAsync(usuario.UserName!,
                new ChangePasswordDTO { CurrentPassword = ClaveConocida, NewPassword = "MiClave2026", ConfirmPassword = "MiClave2026" }));

            Assert.True(respuesta.IsSuccess, respuesta.Message);
            Assert.False((await LeerUsuarioAsync(usuario.Id)).MustChangePassword);
            Assert.True(await ClaveFuncionaAsync(usuario.Id, "MiClave2026"));
            Assert.False(await ClaveFuncionaAsync(usuario.Id, ClaveConocida));
        }

        [Fact]
        public async Task Cambiar_contrasena_con_la_actual_incorrecta_falla_y_sigue_obligado()
        {
            User usuario = await CrearUsuarioAsync(await RolCajeroAsync(), debeCambiar: true);

            Response<User> respuesta = await ConServicioAsync((s, _) => s.ChangePasswordAsync(usuario.UserName!,
                new ChangePasswordDTO { CurrentPassword = "NoEsLaClave1", NewPassword = "MiClave2026", ConfirmPassword = "MiClave2026" }));

            Assert.False(respuesta.IsSuccess);
            Assert.Equal("La contraseña actual no es correcta.", respuesta.Message);
            Assert.True((await LeerUsuarioAsync(usuario.Id)).MustChangePassword);
            Assert.True(await ClaveFuncionaAsync(usuario.Id, ClaveConocida));
        }

        [Theory]
        [InlineData(ClaveConocida)]  // igual a la actual
        [InlineData("corta1A")]      // menos de 8 caracteres
        [InlineData("sinmayuscula1")]
        [InlineData("SINMINUSCULA1")]
        [InlineData("SinNumeros")]
        public async Task Cambiar_contrasena_rechaza_claves_que_no_sirven(string nueva)
        {
            User usuario = await CrearUsuarioAsync(await RolCajeroAsync(), debeCambiar: true);

            Response<User> respuesta = await ConServicioAsync((s, _) => s.ChangePasswordAsync(usuario.UserName!,
                new ChangePasswordDTO { CurrentPassword = ClaveConocida, NewPassword = nueva, ConfirmPassword = nueva }));

            Assert.False(respuesta.IsSuccess);
            Assert.True((await LeerUsuarioAsync(usuario.Id)).MustChangePassword);
            Assert.True(await ClaveFuncionaAsync(usuario.Id, ClaveConocida));
        }

        // ---------- Bloqueo ----------

        [Fact]
        public async Task Cinco_intentos_fallidos_bloquean_la_cuenta_aunque_luego_se_use_la_clave_correcta()
        {
            User usuario = await CrearUsuarioAsync(await RolCajeroAsync());

            for (int i = 1; i <= 4; i++)
            {
                Response<SignInResult> fallo = await ConServicioAsync((s, _) =>
                    s.LoginAsync(new LoginDTO { Email = usuario.Email!, Password = "NoEsLaClave1" }));
                Assert.False(fallo.IsSuccess);
                Assert.False(fallo.Result!.IsLockedOut, $"Se bloqueó antes de tiempo, en el intento {i}");
            }

            Response<SignInResult> quinto = await ConServicioAsync((s, _) =>
                s.LoginAsync(new LoginDTO { Email = usuario.Email!, Password = "NoEsLaClave1" }));
            Assert.True(quinto.Result!.IsLockedOut);

            Response<SignInResult> correcta = await ConServicioAsync((s, _) =>
                s.LoginAsync(new LoginDTO { Email = usuario.Email!, Password = ClaveConocida }));
            Assert.False(correcta.IsSuccess);
            Assert.True(correcta.Result!.IsLockedOut);

            User guardado = await LeerUsuarioAsync(usuario.Id);
            Assert.NotNull(guardado.LockoutEnd);
            Assert.InRange(guardado.LockoutEnd!.Value, DateTimeOffset.UtcNow.AddMinutes(14), DateTimeOffset.UtcNow.AddMinutes(16));
        }
    

        // ---------- Editar usuario (cambio de rol) ----------

        private static UserDTO EdicionDe(User usuario, Guid rolId) => new()
        {
            Id = Guid.Parse(usuario.Id),
            Email = usuario.Email!,
            Document = usuario.Document,
            FirstName = "Editado",
            LastName = usuario.LastName,
            PhoneNumber = "3110000000",
            SistemaVentasRoleId = rolId.ToString()
        };

        [Fact]
        public async Task Quien_no_es_Admin_no_puede_darse_el_rol_Admin()
        {
            User cajero = await CrearUsuarioAsync(await RolCajeroAsync());

            Response<UserDTO> respuesta =
                await ConServicioAsync(async (s, _) => await s.EditAsync(EdicionDe(cajero, await RolAdminAsync()), cajero.UserName!));

            Assert.False(respuesta.IsSuccess);
            Assert.Contains("Solo un usuario con rol 'Admin'", respuesta.Message);
            User guardado = await LeerUsuarioAsync(cajero.Id);
            Assert.Equal(await RolCajeroAsync(), guardado.SistemaVentasRoleId);
            Assert.Equal("Usuario", guardado.FirstName); // tampoco se guardó lo demás
        }

        [Fact]
        public async Task Quien_no_es_Admin_no_puede_dar_el_rol_Admin_a_otro()
        {
            User cajero = await CrearUsuarioAsync(await RolCajeroAsync());
            User otro = await CrearUsuarioAsync(await RolCajeroAsync());

            Response<UserDTO> respuesta =
                await ConServicioAsync(async (s, _) => await s.EditAsync(EdicionDe(otro, await RolAdminAsync()), cajero.UserName!));

            Assert.False(respuesta.IsSuccess);
            Assert.Equal(await RolCajeroAsync(), (await LeerUsuarioAsync(otro.Id)).SistemaVentasRoleId);
        }

        [Fact]
        public async Task Quien_no_es_Admin_no_puede_editar_a_un_Admin()
        {
            User admin = await CrearUsuarioAsync(await RolAdminAsync());
            User cajero = await CrearUsuarioAsync(await RolCajeroAsync());

            Response<UserDTO> respuesta =
                await ConServicioAsync(async (s, _) => await s.EditAsync(EdicionDe(admin, await RolCajeroAsync()), cajero.UserName!));

            Assert.False(respuesta.IsSuccess);
            User guardado = await LeerUsuarioAsync(admin.Id);
            Assert.Equal(await RolAdminAsync(), guardado.SistemaVentasRoleId);
            Assert.Equal("Usuario", guardado.FirstName);
        }

        [Fact]
        public async Task Quien_no_es_Admin_puede_editar_a_otro_sin_dar_el_rol_Admin()
        {
            User cajero = await CrearUsuarioAsync(await RolCajeroAsync());
            User otro = await CrearUsuarioAsync(await RolCajeroAsync());

            Response<UserDTO> respuesta =
                await ConServicioAsync(async (s, _) => await s.EditAsync(EdicionDe(otro, await RolCajeroAsync()), cajero.UserName!));

            Assert.True(respuesta.IsSuccess, respuesta.Message);
            Assert.Equal("Editado", (await LeerUsuarioAsync(otro.Id)).FirstName);
        }

        [Fact]
        public async Task Un_Admin_puede_dar_el_rol_Admin()
        {
            User admin = await CrearUsuarioAsync(await RolAdminAsync());
            User cajero = await CrearUsuarioAsync(await RolCajeroAsync());

            Response<UserDTO> respuesta =
                await ConServicioAsync(async (s, _) => await s.EditAsync(EdicionDe(cajero, await RolAdminAsync()), admin.UserName!));

            Assert.True(respuesta.IsSuccess, respuesta.Message);
            Assert.Equal(await RolAdminAsync(), (await LeerUsuarioAsync(cajero.Id)).SistemaVentasRoleId);
        }

        [Fact]
        public async Task Nadie_puede_cambiar_su_propio_rol()
        {
            User admin = await CrearUsuarioAsync(await RolAdminAsync());
            await CrearUsuarioAsync(await RolAdminAsync()); // hay otro Admin, así que no es el "único"

            Response<UserDTO> respuesta =
                await ConServicioAsync(async (s, _) => await s.EditAsync(EdicionDe(admin, await RolCajeroAsync()), admin.UserName!));

            Assert.False(respuesta.IsSuccess);
            Assert.Contains("tu propio rol", respuesta.Message);
            Assert.Equal(await RolAdminAsync(), (await LeerUsuarioAsync(admin.Id)).SistemaVentasRoleId);
        }

        [Fact]
        public async Task Editar_sus_propios_datos_sin_cambiar_el_rol_si_se_puede()
        {
            User cajero = await CrearUsuarioAsync(await RolCajeroAsync());

            Response<UserDTO> respuesta =
                await ConServicioAsync(async (s, _) => await s.EditAsync(EdicionDe(cajero, await RolCajeroAsync()), cajero.UserName!));

            Assert.True(respuesta.IsSuccess, respuesta.Message);
            Assert.Equal("Editado", (await LeerUsuarioAsync(cajero.Id)).FirstName);
        }

        [Fact]
        public async Task Editar_con_un_rol_inexistente_falla()
        {
            User admin = await CrearUsuarioAsync(await RolAdminAsync());
            User cajero = await CrearUsuarioAsync(await RolCajeroAsync());

            Response<UserDTO> respuesta =
                await ConServicioAsync((s, _) => s.EditAsync(EdicionDe(cajero, Guid.NewGuid()), admin.UserName!));

            Assert.False(respuesta.IsSuccess);
            Assert.Equal(await RolCajeroAsync(), (await LeerUsuarioAsync(cajero.Id)).SistemaVentasRoleId);
        }
    }
}
