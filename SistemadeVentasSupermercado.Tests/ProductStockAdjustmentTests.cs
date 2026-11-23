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
    /// Editar un producto guarda el stock como ajuste (stock nuevo − stock que vio el administrador),
    /// para no pisar las ventas que hagan los cajeros mientras el formulario está abierto.
    /// </summary>
    [Collection(ColeccionBaseDeDatos.Nombre)]
    public class ProductStockAdjustmentTests
    {
        private readonly BaseDeDatosFixture _fixture;

        public ProductStockAdjustmentTests(BaseDeDatosFixture fixture)
        {
            _fixture = fixture;
        }

        // ---------- Ayudantes ----------

        /// <summary>Lo que recibe el administrador al abrir "Editar" (incluye StockOriginal).</summary>
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

        // ---------- Pruebas ----------

        [Fact]
        public async Task El_formulario_trae_el_stock_original()
        {
            Product arroz = await _fixture.CrearProductoAsync("Arroz", 2500m, 10m);

            ProductDTO formulario = await AbrirFormularioAsync(arroz.Id);

            Assert.Equal(10m, formulario.Stock);
            Assert.Equal(10m, formulario.StockOriginal);
        }

        [Fact]
        public async Task Editar_solo_el_precio_no_cambia_el_stock_aunque_haya_una_venta_mientras_tanto()
        {
            Product leche = await _fixture.CrearProductoAsync("Leche", 1200m, 10m);

            ProductDTO formulario = await AbrirFormularioAsync(leche.Id); // ve stock 10
            await VenderAsync(leche.Id, 3m);                              // un cajero vende 3 → 7

            formulario.Price = 1500m;                                     // solo cambia el precio
            Response<ProductDTO> respuesta = await GuardarAsync(formulario);

            Assert.True(respuesta.IsSuccess, respuesta.Message);
            Assert.Equal("Producto actualizado con éxito. Stock actual: 7", respuesta.Message);
            Assert.Equal(7m, respuesta.Result!.Stock);

            Product guardado = await LeerProductoAsync(leche.Id);
            Assert.Equal(1500m, guardado.Price);
            Assert.Equal(7m, guardado.Stock); // antes volvía a 10 y "reaparecían" 3 unidades
        }

        [Fact]
        public async Task Un_ajuste_se_suma_a_una_venta_hecha_mientras_se_editaba()
        {
            Product cafe = await _fixture.CrearProductoAsync("Café", 15000m, 10m);

            ProductDTO formulario = await AbrirFormularioAsync(cafe.Id); // ve stock 10
            await VenderAsync(cafe.Id, 3m);                              // un cajero vende 3 → 7

            formulario.Stock = 15m;                                      // llegaron 5: +5
            Response<ProductDTO> respuesta = await GuardarAsync(formulario);

            Assert.True(respuesta.IsSuccess, respuesta.Message);
            Assert.Equal("Producto actualizado. Stock ajustado +5; stock actual: 12", respuesta.Message);
            Assert.Equal(12m, (await LeerProductoAsync(cafe.Id)).Stock);
        }

        [Fact]
        public async Task Un_ajuste_negativo_valido_se_resta()
        {
            Product queso = await _fixture.CrearProductoAsync("Queso", 12000m, 5m, UnitOfMeasure.Kilogram);

            ProductDTO formulario = await AbrirFormularioAsync(queso.Id);
            formulario.Stock = 3.75m; // se dañaron 1,25 kg

            Response<ProductDTO> respuesta = await GuardarAsync(formulario);

            Assert.True(respuesta.IsSuccess, respuesta.Message);
            Assert.Equal("Producto actualizado. Stock ajustado -1,25; stock actual: 3,75", respuesta.Message);
            Assert.Equal(3.75m, (await LeerProductoAsync(queso.Id)).Stock);
        }

        [Fact]
        public async Task Ajustes_y_ventas_simultaneos_no_se_pisan()
        {
            Product panela = await _fixture.CrearProductoAsync("Panela", 2800m, 100m);
            ProductDTO formulario = await AbrirFormularioAsync(panela.Id); // ve stock 100
            formulario.Stock = 120m;                                       // +20

            TaskCompletionSource salida = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            List<Task> tareas = Enumerable.Range(0, 10)
                .Select(_ => Task.Run(async () => { await salida.Task; await VenderAsync(panela.Id, 1m); }))
                .ToList();
            Task<Response<ProductDTO>> ajuste = Task.Run(async () => { await salida.Task; return await GuardarAsync(formulario); });
            tareas.Add(ajuste);

            salida.SetResult();
            await Task.WhenAll(tareas);

            Assert.True(ajuste.Result.IsSuccess, ajuste.Result.Message);
            Assert.Equal(110m, (await LeerProductoAsync(panela.Id)).Stock); // 100 − 10 vendidas + 20
        }

        [Fact]
        public async Task Se_rechaza_un_ajuste_que_deja_el_stock_negativo_y_no_se_guarda_nada()
        {
            Product aceite = await _fixture.CrearProductoAsync("Aceite", 9000m, 5m);

            ProductDTO formulario = await AbrirFormularioAsync(aceite.Id); // ve stock 5
            await VenderAsync(aceite.Id, 3m);                              // un cajero vende 3 → 2

            formulario.Stock = 1m;                                         // −4: dejaría −2
            formulario.Price = 9500m;
            Response<ProductDTO> respuesta = await GuardarAsync(formulario);

            Assert.False(respuesta.IsSuccess);
            Assert.Contains("ajuste de -4 dejaría el stock negativo", respuesta.Message);
            Assert.Contains("Stock actual: 2", respuesta.Message);

            Product guardado = await LeerProductoAsync(aceite.Id);
            Assert.Equal(2m, guardado.Stock);
            Assert.Equal(9000m, guardado.Price); // tampoco se guardó el precio: todo o nada
        }

        [Fact]
        public async Task Editar_un_producto_inexistente_falla()
        {
            ProductDTO formulario = new ProductDTO
            {
                Id = Guid.NewGuid(), Name = $"Fantasma {Guid.NewGuid():N}", Price = 1000m,
                Stock = 5m, StockOriginal = 0m, CategoryId = _fixture.CategoriaId
            };

            Response<ProductDTO> respuesta = await GuardarAsync(formulario);

            Assert.False(respuesta.IsSuccess);
            Assert.Contains("No existe el producto", respuesta.Message);
        }
    }
}
