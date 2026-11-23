using Microsoft.EntityFrameworkCore;
using SistemadeVentasSupermercado.Tests.Infraestructura;
using SistemadeVentasSupermercado.Web.Core;
using SistemadeVentasSupermercado.Web.Data;
using SistemadeVentasSupermercado.Web.Data.Entities;
using SistemadeVentasSupermercado.Web.DTOs;
using SistemadeVentasSupermercado.Web.Services.Implementations;

namespace SistemadeVentasSupermercado.Tests
{
    /// <summary>
    /// Editar un producto nunca cambia su stock: el stock solo se mueve con ventas, compras, devoluciones
    /// y ajustes de inventario (que quedan en el historial). Así una edición no puede pisar una venta
    /// ni cambiar el stock sin dejar rastro.
    /// </summary>
    [Collection(ColeccionBaseDeDatos.Nombre)]
    public class ProductEditStockTests
    {
        private readonly BaseDeDatosFixture _fixture;

        public ProductEditStockTests(BaseDeDatosFixture fixture)
        {
            _fixture = fixture;
        }

        private async Task<ProductDTO> AbrirFormularioAsync(Guid productoId)
        {
            await using DataContext context = _fixture.CrearContexto();
            Response<ProductDTO> respuesta = await new ProductsService(context).GetOneAsync(productoId);
            Assert.True(respuesta.IsSuccess, respuesta.Message);
            return respuesta.Result!;
        }

        private async Task<Response<ProductDTO>> GuardarAsync(ProductDTO formulario)
        {
            await using DataContext context = _fixture.CrearContexto();
            return await new ProductsService(context).EditAsync(formulario);
        }

        private async Task VenderAsync(Guid productoId, decimal cantidad)
        {
            await using DataContext context = _fixture.CrearContexto();
            Response<SaleResultDTO> venta = await new SalesService(context).CreateAsync(new CreateSaleDTO
            {
                PaymentMethod = PaymentMethod.Cash,
                Items = { new SaleLineDTO { ProductId = productoId, Quantity = cantidad } },
                IdempotencyKey = Guid.NewGuid()
            }, _fixture.CajeroId);
            Assert.True(venta.IsSuccess, venta.Message);
        }

        private async Task<Product> LeerProductoAsync(Guid productoId)
        {
            await using DataContext context = _fixture.CrearContexto();
            return await context.Products.AsNoTracking().SingleAsync(p => p.Id == productoId);
        }

        private async Task<int> MovimientosAsync(Guid productoId)
        {
            await using DataContext context = _fixture.CrearContexto();
            return await context.InventoryMovements.CountAsync(m => m.ProductId == productoId);
        }

        [Fact]
        public async Task Editar_el_precio_no_pisa_una_venta_hecha_mientras_se_editaba_y_no_deja_movimientos()
        {
            Product leche = await _fixture.CrearProductoAsync("Leche", 1200m, 10m);

            ProductDTO formulario = await AbrirFormularioAsync(leche.Id); // ve stock 10
            await VenderAsync(leche.Id, 3m);                              // un cajero vende 3 → 7

            formulario.Price = 1500m;
            Response<ProductDTO> respuesta = await GuardarAsync(formulario);

            Assert.True(respuesta.IsSuccess, respuesta.Message);
            Assert.Equal("Producto actualizado con éxito. Stock actual: 7", respuesta.Message);
            Assert.Equal(7m, respuesta.Result!.Stock);

            Product guardado = await LeerProductoAsync(leche.Id);
            Assert.Equal(1500m, guardado.Price);
            Assert.Equal(7m, guardado.Stock);
            Assert.Equal(1, await MovimientosAsync(leche.Id)); // solo el de la venta
        }

        [Fact]
        public async Task Un_formulario_manipulado_con_otro_stock_no_cambia_el_stock()
        {
            Product cafe = await _fixture.CrearProductoAsync("Café", 15000m, 10m);

            ProductDTO formulario = await AbrirFormularioAsync(cafe.Id);
            formulario.Stock = 9999m; // alguien envía un stock a mano

            Assert.True((await GuardarAsync(formulario)).IsSuccess);

            Assert.Equal(10m, (await LeerProductoAsync(cafe.Id)).Stock);
            Assert.Equal(0, await MovimientosAsync(cafe.Id));
        }

        [Fact]
        public async Task Editar_con_el_stock_en_cero_en_el_formulario_no_vacia_el_inventario()
        {
            // El formulario de edición ya no envía el stock: llega como 0 y no debe interpretarse como "dejar en 0".
            Product arroz = await _fixture.CrearProductoAsync("Arroz", 2500m, 8m, UnitOfMeasure.Kilogram);

            Response<ProductDTO> respuesta = await GuardarAsync(new ProductDTO
            {
                Id = arroz.Id, Name = arroz.Name, Price = 2600m, CategoryId = arroz.CategoryId
            });

            Assert.True(respuesta.IsSuccess, respuesta.Message);
            Product guardado = await LeerProductoAsync(arroz.Id);
            Assert.Equal(8m, guardado.Stock);
            Assert.Equal(UnitOfMeasure.Kilogram, guardado.Unit);
            Assert.Equal(0, await MovimientosAsync(arroz.Id));
        }

        [Fact]
        public async Task Editar_un_producto_inexistente_falla()
        {
            ProductDTO formulario = new ProductDTO
            {
                Id = Guid.NewGuid(), Name = $"Fantasma {Guid.NewGuid():N}", Price = 1000m, CategoryId = _fixture.CategoriaId
            };

            Response<ProductDTO> respuesta = await GuardarAsync(formulario);

            Assert.False(respuesta.IsSuccess);
            Assert.Contains("No existe el producto", respuesta.Message);
        }
    }
}
