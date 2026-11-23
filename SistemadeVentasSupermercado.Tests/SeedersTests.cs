using Microsoft.EntityFrameworkCore;
using SistemadeVentasSupermercado.Tests.Infraestructura;
using SistemadeVentasSupermercado.Web.Data;
using SistemadeVentasSupermercado.Web.Data.Entities;
using SistemadeVentasSupermercado.Web.Data.Seeders;

namespace SistemadeVentasSupermercado.Tests
{
    /// <summary>
    /// Limpieza de restos de la plantilla al arrancar: permisos de "Secciones"/"Blogs", roles viejos
    /// (solo si nadie los usa), roles de ejemplo Cajero y Supervisor, y productos de ejemplo solo en desarrollo.
    /// </summary>
    [Collection(ColeccionBaseDeDatos.Nombre)]
    public class SeedersTests
    {
        private readonly BaseDeDatosFixture _fixture;

        public SeedersTests(BaseDeDatosFixture fixture)
        {
            _fixture = fixture;
        }

        // Estas pruebas no crean usuarios con el seeder, así que no necesitan IUsersService.
        private static UserRolesSeeder RolesSeeder(DataContext context) => new UserRolesSeeder(context, null!);

        private static Permission NuevoPermiso(string modulo) => new Permission
        {
            Id = Guid.NewGuid(),
            Name = $"perm{Guid.NewGuid():N}"[..20],
            Description = "Permiso de prueba",
            Module = modulo
        };

        private async Task<SistemaVentasRole> RolConPermisosAsync(string nombre, params Permission[] permisos)
        {
            await using DataContext context = _fixture.CrearContexto();
            SistemaVentasRole rol = new SistemaVentasRole { Id = Guid.NewGuid(), Name = nombre };
            context.SistemaVentasRoles.Add(rol);
            context.Permissions.AddRange(permisos);
            foreach (Permission p in permisos)
                context.RolePermissions.Add(new RolePermission { PermissionId = p.Id, SistemaVentasRoleId = rol.Id });
            await context.SaveChangesAsync();
            return rol;
        }

        /// <summary>Borra un rol (y sus permisos asignados) si existe y nadie lo usa, para empezar de cero.</summary>
        private async Task BorrarRolSiExisteAsync(string nombre)
        {
            await using DataContext context = _fixture.CrearContexto();
            SistemaVentasRole? rol = await context.SistemaVentasRoles.FirstOrDefaultAsync(r => r.Name == nombre);
            if (rol is null) return;
            await context.RolePermissions.Where(rp => rp.SistemaVentasRoleId == rol.Id).ExecuteDeleteAsync();
            await context.SistemaVentasRoles.Where(r => r.Id == rol.Id).ExecuteDeleteAsync();
        }

        private async Task<List<string>> PermisosDelRolAsync(string nombre)
        {
            await using DataContext context = _fixture.CrearContexto();
            return await context.RolePermissions
                                .Where(rp => rp.SistemaVentasRole.Name == nombre)
                                .Select(rp => rp.Permission.Name)
                                .OrderBy(n => n)
                                .ToListAsync();
        }

        [Fact]
        public async Task Borra_los_permisos_de_Secciones_y_Blogs_sin_tocar_los_demas()
        {
            Permission seccion = NuevoPermiso("Secciones");
            Permission blog = NuevoPermiso("Blogs");
            Permission otro = NuevoPermiso("Modulo de pruebas");
            SistemaVentasRole rol = await RolConPermisosAsync($"Rol mixto {Guid.NewGuid():N}"[..20], seccion, blog, otro);

            await using (DataContext context = _fixture.CrearContexto())
                await new PermissionsSeeder(context).RemoveObsoleteAsync();

            await using DataContext verificar = _fixture.CrearContexto();
            Assert.False(await verificar.Permissions.AnyAsync(p => p.Module == "Secciones" || p.Module == "Blogs"));
            Assert.True(await verificar.Permissions.AnyAsync(p => p.Id == otro.Id));
            List<Guid> asignados = await verificar.RolePermissions.Where(rp => rp.SistemaVentasRoleId == rol.Id)
                                                                  .Select(rp => rp.PermissionId).ToListAsync();
            Assert.Equal([otro.Id], asignados);
        }

        [Fact]
        public async Task Borra_un_rol_obsoleto_sin_usuarios_y_conserva_uno_que_tiene_usuarios()
        {
            await BorrarRolSiExisteAsync("Basic");
            await BorrarRolSiExisteAsync("Gestor de contenido");
            SistemaVentasRole sinUsuarios = await RolConPermisosAsync("Basic", NuevoPermiso("Modulo de pruebas"));
            SistemaVentasRole conUsuarios = await RolConPermisosAsync("Gestor de contenido");

            string marca = Guid.NewGuid().ToString("N")[..8];
            User usuario = new User
            {
                Id = Guid.NewGuid().ToString(),
                UserName = $"obsoleto-{marca}@pruebas.local",
                Email = $"obsoleto-{marca}@pruebas.local",
                Document = $"OBS-{marca}",
                FirstName = "Usuario",
                LastName = marca,
                SistemaVentasRoleId = conUsuarios.Id
            };
            await using (DataContext context = _fixture.CrearContexto())
            {
                context.Users.Add(usuario);
                await context.SaveChangesAsync();
            }

            try
            {
                await using (DataContext context = _fixture.CrearContexto())
                    await RolesSeeder(context).RemoveObsoleteRolesAsync();

                await using DataContext verificar = _fixture.CrearContexto();
                Assert.False(await verificar.SistemaVentasRoles.AnyAsync(r => r.Id == sinUsuarios.Id));
                Assert.False(await verificar.RolePermissions.AnyAsync(rp => rp.SistemaVentasRoleId == sinUsuarios.Id));
                Assert.True(await verificar.SistemaVentasRoles.AnyAsync(r => r.Id == conUsuarios.Id));
            }
            finally
            {
                await using DataContext limpiar = _fixture.CrearContexto();
                await limpiar.Users.Where(u => u.Id == usuario.Id).ExecuteDeleteAsync();
                await BorrarRolSiExisteAsync("Gestor de contenido");
            }
        }

        [Fact]
        public async Task Crea_Cajero_y_Supervisor_con_sus_permisos_y_no_pisa_cambios_posteriores()
        {
            await using (DataContext context = _fixture.CrearContexto())
                await new PermissionsSeeder(context).SeedAsync();
            await BorrarRolSiExisteAsync(UserRolesSeeder.CASHIER_ROLE_NAME);
            await BorrarRolSiExisteAsync(UserRolesSeeder.SUPERVISOR_ROLE_NAME);

            await using (DataContext context = _fixture.CrearContexto())
                await RolesSeeder(context).SeedRolesAsync();

            Assert.Equal(UserRolesSeeder.CashierPermissions.OrderBy(n => n),
                         await PermisosDelRolAsync(UserRolesSeeder.CASHIER_ROLE_NAME));
            Assert.Equal(UserRolesSeeder.SupervisorPermissions.OrderBy(n => n),
                         await PermisosDelRolAsync(UserRolesSeeder.SUPERVISOR_ROLE_NAME));

            // El dueño le quita un permiso al Cajero; al volver a arrancar, el seeder no se lo devuelve.
            await using (DataContext context = _fixture.CrearContexto())
            {
                await context.RolePermissions
                             .Where(rp => rp.SistemaVentasRole.Name == UserRolesSeeder.CASHIER_ROLE_NAME
                                          && rp.Permission.Name == "manageCashSessions")
                             .ExecuteDeleteAsync();
            }

            await using (DataContext context = _fixture.CrearContexto())
                await RolesSeeder(context).SeedRolesAsync();

            Assert.Equal(["createSales"], await PermisosDelRolAsync(UserRolesSeeder.CASHIER_ROLE_NAME));
        }

        [Fact]
        public async Task Fuera_de_desarrollo_crea_las_categorias_pero_no_los_productos_de_ejemplo()
        {
            await using (DataContext context = _fixture.CrearContexto())
                await new ProductsSeeder(context).SeedAsync(crearProductos: false);

            await using DataContext verificar = _fixture.CrearContexto();
            Assert.True(await verificar.Categories.AnyAsync(c => c.Name == "Granos"));
            Assert.False(await verificar.Products.AnyAsync(p => p.Name == "Arroz Diana 500g"));
        }
    }
}
