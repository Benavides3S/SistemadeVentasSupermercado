using Microsoft.EntityFrameworkCore;
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
    /// El sistema reconoce al administrador por el NOMBRE del rol ("Admin"). Por eso el rol Admin no se
    /// puede editar ni renombrar, y ningún otro rol puede llamarse así (sin importar mayúsculas o espacios).
    /// </summary>
    [Collection(ColeccionBaseDeDatos.Nombre)]
    public class RolesProtectionTests
    {
        private readonly BaseDeDatosFixture _fixture;

        public RolesProtectionTests(BaseDeDatosFixture fixture)
        {
            _fixture = fixture;
        }

        private async Task<SistemaVentasRole> RolAsync(string nombre)
        {
            await using DataContext context = _fixture.CrearContexto();
            SistemaVentasRole? rol = await context.SistemaVentasRoles.FirstOrDefaultAsync(r => r.Name == nombre);
            if (rol is null)
            {
                rol = new SistemaVentasRole { Id = Guid.NewGuid(), Name = nombre };
                context.SistemaVentasRoles.Add(rol);
                await context.SaveChangesAsync();
            }
            return rol;
        }

        private async Task<Response<SistemaVentaRoleDTO>> EditarAsync(Guid id, string nombre)
        {
            await using DataContext context = _fixture.CrearContexto();
            return await new RolesService(context).EditAsync(new SistemaVentaRoleDTO { Id = id, Name = nombre });
        }

        private async Task<string> NombreGuardadoAsync(Guid id)
        {
            await using DataContext context = _fixture.CrearContexto();
            return (await context.SistemaVentasRoles.AsNoTracking().SingleAsync(r => r.Id == id)).Name;
        }

        [Fact]
        public async Task El_rol_Admin_no_se_puede_renombrar()
        {
            SistemaVentasRole admin = await RolAsync(Env.SUPER_ADMIN_ROLE_NAME);

            Response<SistemaVentaRoleDTO> respuesta = await EditarAsync(admin.Id, "Otro nombre");

            Assert.False(respuesta.IsSuccess);
            Assert.Equal(Env.SUPER_ADMIN_ROLE_NAME, await NombreGuardadoAsync(admin.Id));
        }

        [Theory]
        [InlineData("Admin")]
        [InlineData("ADMIN")]
        [InlineData(" admin ")]
        public async Task Otro_rol_no_se_puede_renombrar_a_Admin(string nombre)
        {
            SistemaVentasRole rol = await RolAsync($"Rol renombrar {Guid.NewGuid().ToString("N")[..8]}");

            Response<SistemaVentaRoleDTO> respuesta = await EditarAsync(rol.Id, nombre);

            Assert.False(respuesta.IsSuccess);
            Assert.Contains("reservado", respuesta.Message);
            Assert.Equal(rol.Name, await NombreGuardadoAsync(rol.Id));
        }

        [Theory]
        [InlineData("Admin")]
        [InlineData("aDmIn")]
        public async Task No_se_puede_crear_un_rol_llamado_Admin(string nombre)
        {
            await using DataContext context = _fixture.CrearContexto();
            int antes = await context.SistemaVentasRoles.CountAsync();

            Response<SistemaVentaRoleDTO> respuesta =
                await new RolesService(context).CreateAsync(new SistemaVentaRoleDTO { Name = nombre });

            Assert.False(respuesta.IsSuccess);
            Assert.Contains("reservado", respuesta.Message);
            Assert.Equal(antes, await context.SistemaVentasRoles.CountAsync());
        }

        [Fact]
        public async Task Un_rol_normal_si_se_puede_renombrar()
        {
            SistemaVentasRole rol = await RolAsync($"Rol normal {Guid.NewGuid().ToString("N")[..8]}");
            string nuevo = $"Supervisor {Guid.NewGuid().ToString("N")[..8]}";

            Response<SistemaVentaRoleDTO> respuesta = await EditarAsync(rol.Id, nuevo);

            Assert.True(respuesta.IsSuccess, respuesta.Message);
            Assert.Equal(nuevo, await NombreGuardadoAsync(rol.Id));
        }
    }
}
