using Microsoft.EntityFrameworkCore;
using SistemadeVentasSupermercado.Tests.Infraestructura;
using SistemadeVentasSupermercado.Web.Core;
using SistemadeVentasSupermercado.Web.Data;
using SistemadeVentasSupermercado.Web.Data.Entities;
using SistemadeVentasSupermercado.Web.DTOs;
using SistemadeVentasSupermercado.Web.Services.Implementations;

namespace SistemadeVentasSupermercado.Tests
{
    /// <summary>Escanear en el punto de venta: buscar por código exacto y explicar por qué no se puede vender.</summary>
    [Collection(ColeccionBaseDeDatos.Nombre)]
    public class SaleBarcodeTests
    {
        private readonly BaseDeDatosFixture _fixture;

        public SaleBarcodeTests(BaseDeDatosFixture fixture)
        {
            _fixture = fixture;
        }

        private static string NuevoCodigo() => "78" + Random.Shared.NextInt64(10_000_000_000, 99_999_999_999);

        private async Task<Response<BarcodeLookupDTO>> EscanearAsync(string? codigo)
        {
            await using DataContext context = _fixture.CrearContexto();
            return await new SalesService(context).FindByBarcodeAsync(codigo);
        }

        [Fact]
        public async Task Escanear_un_codigo_existente_devuelve_el_producto_con_su_precio_de_la_base()
        {
            string codigo = NuevoCodigo();
            Product producto = await _fixture.CrearProductoAsync("Escaneado", 3200m, 7m, codigo: codigo);

            Response<BarcodeLookupDTO> respuesta = await EscanearAsync(codigo);

            Assert.True(respuesta.IsSuccess, respuesta.Message);
            Assert.Equal(BarcodeLookupStatus.Found, respuesta.Result!.Status);
            Assert.Equal(producto.Id, respuesta.Result.Product!.Id);
            Assert.Equal(3200m, respuesta.Result.Product.Price);
            Assert.Equal("und", respuesta.Result.Product.UnitAbbreviation);
            Assert.False(respuesta.Result.Product.AllowsDecimals);
        }

        [Fact]
        public async Task Escanear_quita_espacios_alrededor_del_codigo()
        {
            string codigo = NuevoCodigo();
            await _fixture.CrearProductoAsync("Con espacios", 1000m, 2m, codigo: codigo);

            Response<BarcodeLookupDTO> respuesta = await EscanearAsync($"  {codigo}\t");

            Assert.Equal(BarcodeLookupStatus.Found, respuesta.Result!.Status);
        }

        [Fact]
        public async Task Producto_por_peso_escaneado_admite_decimales()
        {
            string codigo = NuevoCodigo();
            await _fixture.CrearProductoAsync("Queso escaneado", 20000m, 3.5m, UnitOfMeasure.Kilogram, codigo: codigo);

            Response<BarcodeLookupDTO> respuesta = await EscanearAsync(codigo);

            Assert.Equal(BarcodeLookupStatus.Found, respuesta.Result!.Status);
            Assert.True(respuesta.Result.Product!.AllowsDecimals);
        }

        [Theory]
        [InlineData("0000000000000")]
        [InlineData("Arroz")]   // un nombre no es un código: el punto de venta lo busca después por nombre
        [InlineData("77/01")]   // caracteres no permitidos: tampoco es un código
        [InlineData("   ")]
        public async Task Codigo_inexistente_devuelve_NotFound(string codigo)
        {
            Response<BarcodeLookupDTO> respuesta = await EscanearAsync(codigo);

            Assert.True(respuesta.IsSuccess, respuesta.Message);
            Assert.Equal(BarcodeLookupStatus.NotFound, respuesta.Result!.Status);
            Assert.Null(respuesta.Result.Product);
            Assert.Contains("No hay ningún producto con el código", respuesta.Message);
        }

        [Fact]
        public async Task Producto_sin_stock_no_se_agrega_y_el_mensaje_lo_dice()
        {
            string codigo = NuevoCodigo();
            Product producto = await _fixture.CrearProductoAsync("Agotado", 1500m, 0m, codigo: codigo);

            Response<BarcodeLookupDTO> respuesta = await EscanearAsync(codigo);

            Assert.Equal(BarcodeLookupStatus.OutOfStock, respuesta.Result!.Status);
            Assert.Null(respuesta.Result.Product);
            Assert.Contains(producto.Name, respuesta.Message);
            Assert.Contains("no tiene stock", respuesta.Message);
        }

        [Fact]
        public async Task Producto_de_categoria_desactivada_no_se_agrega()
        {
            Guid categoriaId = Guid.NewGuid();
            await using (DataContext context = _fixture.CrearContexto())
            {
                context.Categories.Add(new Category { Id = categoriaId, Name = $"Desactivada {Guid.NewGuid():N}"[..20], IsActive = false });
                await context.SaveChangesAsync();
            }
            string codigo = NuevoCodigo();
            await _fixture.CrearProductoAsync("Inactivo", 1500m, 5m, categoriaId: categoriaId, codigo: codigo);

            Response<BarcodeLookupDTO> respuesta = await EscanearAsync(codigo);

            Assert.Equal(BarcodeLookupStatus.InactiveCategory, respuesta.Result!.Status);
            Assert.Null(respuesta.Result.Product);
            Assert.Contains("categoría desactivada", respuesta.Message);
        }

        [Fact]
        public async Task El_buscador_por_nombre_tambien_encuentra_por_codigo_exacto()
        {
            string codigo = NuevoCodigo();
            Product producto = await _fixture.CrearProductoAsync("Buscable", 1000m, 4m, codigo: codigo);

            await using DataContext context = _fixture.CrearContexto();
            Response<List<PosProductDTO>> respuesta = await new SalesService(context).SearchProductsAsync(codigo);

            Assert.True(respuesta.IsSuccess, respuesta.Message);
            Assert.Contains(respuesta.Result!, p => p.Id == producto.Id);
        }

        [Fact]
        public async Task Vender_dos_veces_el_mismo_producto_escaneado_descuenta_el_stock()
        {
            // El punto de venta suma 1 por cada escaneo y envía solo ProductId y cantidad.
            string codigo = NuevoCodigo();
            Product producto = await _fixture.CrearProductoAsync("Dos escaneos", 1000m, 5m, codigo: codigo);
            Guid id = (await EscanearAsync(codigo)).Result!.Product!.Id;

            await using (DataContext context = _fixture.CrearContexto())
            {
                Response<SaleResultDTO> venta = await new SalesService(context).CreateAsync(new CreateSaleDTO
                {
                    PaymentMethod = PaymentMethod.Cash,
                    Items = { new SaleLineDTO { ProductId = id, Quantity = 2 } },
                    IdempotencyKey = Guid.NewGuid()
                }, _fixture.CajeroId);
                Assert.True(venta.IsSuccess, venta.Message);
            }

            await using DataContext verificar = _fixture.CrearContexto();
            Assert.Equal(3m, await verificar.Products.Where(p => p.Id == producto.Id).Select(p => p.Stock).SingleAsync());
        }
    }
}
