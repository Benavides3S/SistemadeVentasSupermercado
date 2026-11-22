using Microsoft.EntityFrameworkCore;
using SistemadeVentasSupermercado.Tests.Infraestructura;
using SistemadeVentasSupermercado.Web.Data;

namespace SistemadeVentasSupermercado.Tests
{
    [Collection(ColeccionBaseDeDatos.Nombre)]
    public class BaseDeDatosTests
    {
        private readonly BaseDeDatosFixture _fixture;

        public BaseDeDatosTests(BaseDeDatosFixture fixture)
        {
            _fixture = fixture;
        }

        [Fact]
        public async Task La_base_de_pruebas_se_crea_con_todas_las_migraciones()
        {
            await using DataContext context = _fixture.CrearContexto();

            Assert.Equal(BaseDeDatosFixture.NombreBaseDePruebas, context.Database.GetDbConnection().Database);
            Assert.Empty(await context.Database.GetPendingMigrationsAsync());
            Assert.True(await context.Users.AnyAsync(u => u.Id == _fixture.CajeroId));
            Assert.True(await context.Categories.AnyAsync(c => c.Id == _fixture.CategoriaId));
        }
    }
}
