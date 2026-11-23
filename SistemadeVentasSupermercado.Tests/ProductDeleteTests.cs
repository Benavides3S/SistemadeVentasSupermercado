using Microsoft.EntityFrameworkCore;
using SistemadeVentasSupermercado.Tests.Infraestructura;
using SistemadeVentasSupermercado.Web.Core;
using SistemadeVentasSupermercado.Web.Data;
using SistemadeVentasSupermercado.Web.Data.Entities;
using SistemadeVentasSupermercado.Web.DTOs;
using SistemadeVentasSupermercado.Web.Services.Implementations;

namespace SistemadeVentasSupermercado.Tests
{
    /// <summary>Borrar un producto: sin historial se borra; con ventas, compras o movimientos se rechaza con un mensaje claro.</summary>
    [Collection(ColeccionBaseDeDatos.Nombre)]
    public class ProductDeleteTests
    {
        private readonly BaseDeDatosFixture _fixture;

        public ProductDeleteTests(BaseDeDatosFixture fixture)
        {
            _fixture = fixture;
        }

        private async Task<Response<object>> BorrarAsync(Guid productoId)
        {
            await using DataContext context = _fixture.CrearContexto();
            return await new ProductsService(context).DeleteAsync(productoId);
        }

        private async Task<bool> ExisteAsync(Guid productoId)
        {
            await using DataContext context = _fixture.CrearContexto();
            return await context.Products.AnyAsync(p => p.Id == productoId);
        }

        [Fact]
        public async Task Un_producto_sin_historial_se_borra()
        {
            Product producto = await _fixture.CrearProductoAsync("Sin historial", 1000m, 0m);

            Response<object> respuesta = await BorrarAsync(producto.Id);

            Assert.True(respuesta.IsSuccess, respuesta.Message);
            Assert.False(await ExisteAsync(producto.Id));
        }

        [Fact]
        public async Task Un_producto_vendido_no_se_borra_y_el_mensaje_es_claro()
        {
            Product producto = await _fixture.CrearProductoAsync("Vendido", 1000m, 5m);
            await using (DataContext context = _fixture.CrearContexto())
            {
                Response<SaleResultDTO> venta = await new SalesService(context).CreateAsync(new CreateSaleDTO
                {
                    PaymentMethod = PaymentMethod.Cash,
                    Items = { new SaleLineDTO { ProductId = producto.Id, Quantity = 1m } },
                    IdempotencyKey = Guid.NewGuid()
                }, _fixture.CajeroId);
                Assert.True(venta.IsSuccess, venta.Message);
            }

            Response<object> respuesta = await BorrarAsync(producto.Id);

            Assert.False(respuesta.IsSuccess);
            Assert.Contains("no se puede borrar", respuesta.Message);
            Assert.DoesNotContain("FK_", respuesta.Message);
            Assert.True(await ExisteAsync(producto.Id));
        }

        [Fact]
        public async Task Un_producto_con_un_ajuste_de_inventario_no_se_borra()
        {
            Product producto = await _fixture.CrearProductoAsync("Ajustado", 1000m, 5m);
            await using (DataContext context = _fixture.CrearContexto())
            {
                Response<AdjustInventoryResultDTO> ajuste = await new InventoryService(context).AdjustAsync(new AdjustInventoryDTO
                {
                    ProductId = producto.Id,
                    Direction = AdjustmentDirection.Decrease,
                    Quantity = 1m,
                    Reason = AdjustmentReason.Waste,
                    IdempotencyKey = Guid.NewGuid()
                }, _fixture.CajeroId);
                Assert.True(ajuste.IsSuccess, ajuste.Message);
            }

            Response<object> respuesta = await BorrarAsync(producto.Id);

            Assert.False(respuesta.IsSuccess);
            Assert.Contains("no se puede borrar", respuesta.Message);
            Assert.True(await ExisteAsync(producto.Id));
        }
    }
}
