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
    /// Pruebas de SalesService.CreateAsync contra una base SQL Server real (SistemaVentasTests).
    /// Cada prueba crea sus propios productos, así ninguna depende de otra.
    /// </summary>
    [Collection(ColeccionBaseDeDatos.Nombre)]
    public class SalesServiceTests
    {
        private readonly BaseDeDatosFixture _fixture;

        public SalesServiceTests(BaseDeDatosFixture fixture)
        {
            _fixture = fixture;
        }

        // ---------- Ayudantes ----------

        private Task<Product> CrearProductoAsync(string nombre, decimal precio, decimal stock,
            UnitOfMeasure unidad = UnitOfMeasure.Unit, Guid? id = null)
            => _fixture.CrearProductoAsync(nombre, precio, stock, unidad, id);

        /// <summary>Registra una venta con un contexto y un servicio nuevos (como una petición web).</summary>
        private async Task<Response<SaleResultDTO>> VenderAsync(CreateSaleDTO venta)
        {
            await using DataContext context = _fixture.CrearContexto();
            SalesService servicio = new SalesService(context);
            return await servicio.CreateAsync(venta, _fixture.CajeroId);
        }

        private static CreateSaleDTO Venta(params (Guid productoId, decimal cantidad)[] lineas) => new CreateSaleDTO
        {
            PaymentMethod = PaymentMethod.Cash,
            Items = lineas.Select(l => new SaleLineDTO { ProductId = l.productoId, Quantity = l.cantidad }).ToList(),
            IdempotencyKey = Guid.NewGuid() // cada venta, un cobro distinto
        };

        private async Task<decimal> StockActualAsync(Guid productoId)
        {
            await using DataContext context = _fixture.CrearContexto();
            return await context.Products.Where(p => p.Id == productoId).Select(p => p.Stock).SingleAsync();
        }

        private async Task<int> LineasVendidasAsync(Guid productoId)
        {
            await using DataContext context = _fixture.CrearContexto();
            return await context.SaleItems.CountAsync(i => i.ProductId == productoId);
        }

        private async Task<int> TotalVentasAsync()
        {
            await using DataContext context = _fixture.CrearContexto();
            return await context.Sales.CountAsync();
        }

        // ---------- 1. Venta simple ----------

        [Fact]
        public async Task Venta_simple_descuenta_stock_y_guarda_copia_de_los_datos()
        {
            Product arroz = await CrearProductoAsync("Arroz", 2500m, 10m);
            Product leche = await CrearProductoAsync("Leche", 1200m, 5m);

            Response<SaleResultDTO> respuesta = await VenderAsync(Venta((arroz.Id, 3m), (leche.Id, 2m)));

            Assert.True(respuesta.IsSuccess, respuesta.Message);
            Assert.NotNull(respuesta.Result);
            Assert.True(respuesta.Result!.Number > 0);
            Assert.Equal(9900m, respuesta.Result.Total); // 3 × 2500 + 2 × 1200

            Assert.Equal(7m, await StockActualAsync(arroz.Id));
            Assert.Equal(3m, await StockActualAsync(leche.Id));

            await using DataContext context = _fixture.CrearContexto();
            Sale venta = await context.Sales.Include(s => s.Items).SingleAsync(s => s.Id == respuesta.Result.Id);

            Assert.Equal(_fixture.CajeroId, venta.UserId);
            Assert.Equal(SaleStatus.Completed, venta.Status);
            Assert.Equal(2, venta.Items.Count);

            SaleItem lineaArroz = venta.Items.Single(i => i.ProductId == arroz.Id);
            Assert.Equal(arroz.Name, lineaArroz.ProductName);
            Assert.Equal(2500m, lineaArroz.UnitPrice);
            Assert.Equal(3m, lineaArroz.Quantity);
            Assert.Equal(UnitOfMeasure.Unit, lineaArroz.Unit);
            Assert.Equal(7500m, lineaArroz.Total);
        }

        // ---------- 2. Stock insuficiente ----------

        [Fact]
        public async Task Stock_insuficiente_falla_y_no_cambia_el_stock()
        {
            Product aceite = await CrearProductoAsync("Aceite", 9000m, 2m);
            int ventasAntes = await TotalVentasAsync();

            Response<SaleResultDTO> respuesta = await VenderAsync(Venta((aceite.Id, 3m)));

            Assert.False(respuesta.IsSuccess);
            Assert.Contains("Stock insuficiente", respuesta.Message);
            Assert.Equal(2m, await StockActualAsync(aceite.Id));
            Assert.Equal(ventasAntes, await TotalVentasAsync());
        }

        // ---------- 3. Producto por unidad con decimales ----------

        [Fact]
        public async Task Producto_por_unidad_rechaza_cantidades_decimales()
        {
            Product jabon = await CrearProductoAsync("Jabón", 3000m, 10m, UnitOfMeasure.Unit);

            Response<SaleResultDTO> respuesta = await VenderAsync(Venta((jabon.Id, 1.5m)));

            Assert.False(respuesta.IsSuccess);
            Assert.Contains("número entero", respuesta.Message);
            Assert.Equal(10m, await StockActualAsync(jabon.Id));
        }

        // ---------- 4. Producto por peso con decimales ----------

        [Fact]
        public async Task Producto_por_peso_acepta_decimales_y_redondea_el_total()
        {
            Product queso = await CrearProductoAsync("Queso", 12345.67m, 5m, UnitOfMeasure.Kilogram);

            Response<SaleResultDTO> respuesta = await VenderAsync(Venta((queso.Id, 1.250m)));

            Assert.True(respuesta.IsSuccess, respuesta.Message);
            // 12345.67 × 1.250 = 15432.0875 → se redondea a 2 decimales
            Assert.Equal(15432.09m, respuesta.Result!.Total);
            Assert.Equal(3.750m, await StockActualAsync(queso.Id));
        }

        // ---------- 5. Líneas repetidas ----------

        [Fact]
        public async Task Lineas_repetidas_del_mismo_producto_se_unen_en_una()
        {
            Product huevos = await CrearProductoAsync("Huevos", 500m, 20m);

            Response<SaleResultDTO> respuesta = await VenderAsync(Venta((huevos.Id, 2m), (huevos.Id, 3m)));

            Assert.True(respuesta.IsSuccess, respuesta.Message);
            Assert.Equal(2500m, respuesta.Result!.Total);
            Assert.Equal(15m, await StockActualAsync(huevos.Id));

            await using DataContext context = _fixture.CrearContexto();
            SaleItem linea = await context.SaleItems.SingleAsync(i => i.SaleId == respuesta.Result.Id);
            Assert.Equal(5m, linea.Quantity);
        }

        // ---------- 6. Rollback ----------

        /// <summary>
        /// El servicio procesa las líneas ordenadas por ProductId. Para probar el rollback de verdad,
        /// la línea válida debe ir PRIMERO (y descontar stock) y la que falla DESPUÉS.
        /// </summary>
        private static (Guid primero, Guid segundo) DosIdsOrdenados()
        {
            Guid a = Guid.NewGuid(), b = Guid.NewGuid();
            return a.CompareTo(b) < 0 ? (a, b) : (b, a);
        }

        [Fact]
        public async Task Si_una_linea_falla_por_stock_se_deshace_toda_la_venta()
        {
            (Guid idValido, Guid idSinStock) = DosIdsOrdenados();
            Product pan = await CrearProductoAsync("Pan", 300m, 5m, id: idValido);
            Product cafe = await CrearProductoAsync("Café", 15000m, 1m, id: idSinStock);
            int ventasAntes = await TotalVentasAsync();

            Response<SaleResultDTO> respuesta = await VenderAsync(Venta((pan.Id, 2m), (cafe.Id, 3m)));

            Assert.False(respuesta.IsSuccess);
            Assert.Contains("Stock insuficiente", respuesta.Message);
            Assert.Equal(5m, await StockActualAsync(pan.Id));   // el descuento del pan se deshizo
            Assert.Equal(1m, await StockActualAsync(cafe.Id));
            Assert.Equal(ventasAntes, await TotalVentasAsync());
        }

        [Fact]
        public async Task Si_una_linea_falla_por_decimales_se_deshace_toda_la_venta()
        {
            (Guid idValido, Guid idPorUnidad) = DosIdsOrdenados();
            Product azucar = await CrearProductoAsync("Azúcar", 4000m, 8m, UnitOfMeasure.Kilogram, idValido);
            Product galletas = await CrearProductoAsync("Galletas", 2000m, 8m, UnitOfMeasure.Unit, idPorUnidad);

            Response<SaleResultDTO> respuesta = await VenderAsync(Venta((azucar.Id, 1.5m), (galletas.Id, 0.5m)));

            Assert.False(respuesta.IsSuccess);
            Assert.Equal(8m, await StockActualAsync(azucar.Id));
            Assert.Equal(8m, await StockActualAsync(galletas.Id));
        }

        // ---------- 7. Venta vacía y método de pago inválido ----------

        [Fact]
        public async Task Venta_vacia_se_rechaza()
        {
            int ventasAntes = await TotalVentasAsync();

            Response<SaleResultDTO> respuesta = await VenderAsync(Venta());

            Assert.False(respuesta.IsSuccess);
            Assert.Contains("al menos un producto", respuesta.Message);
            Assert.Equal(ventasAntes, await TotalVentasAsync());
        }

        [Fact]
        public async Task Metodo_de_pago_invalido_se_rechaza()
        {
            Product sal = await CrearProductoAsync("Sal", 1000m, 5m);
            CreateSaleDTO venta = Venta((sal.Id, 1m));
            venta.PaymentMethod = (PaymentMethod)99;

            Response<SaleResultDTO> respuesta = await VenderAsync(venta);

            Assert.False(respuesta.IsSuccess);
            Assert.Contains("Método de pago", respuesta.Message);
            Assert.Equal(5m, await StockActualAsync(sal.Id));
        }

        // ---------- Extras ----------

        [Theory]
        [InlineData(0)]
        [InlineData(-1)]
        public async Task Cantidad_cero_o_negativa_se_rechaza(int cantidad)
        {
            Product atun = await CrearProductoAsync("Atún", 6000m, 5m);

            Response<SaleResultDTO> respuesta = await VenderAsync(Venta((atun.Id, cantidad)));

            Assert.False(respuesta.IsSuccess);
            Assert.Contains("mayores que cero", respuesta.Message);
            Assert.Equal(5m, await StockActualAsync(atun.Id));
        }

        [Fact]
        public async Task Producto_inexistente_se_rechaza()
        {
            int ventasAntes = await TotalVentasAsync();

            Response<SaleResultDTO> respuesta = await VenderAsync(Venta((Guid.NewGuid(), 1m)));

            Assert.False(respuesta.IsSuccess);
            Assert.Contains("ya no existe", respuesta.Message);
            Assert.Equal(ventasAntes, await TotalVentasAsync());
        }

        [Fact]
        public async Task Producto_de_categoria_desactivada_no_se_vende()
        {
            Product viejo = await _fixture.CrearProductoAsync("Descontinuado", 1000m, 5m,
                categoriaId: _fixture.CategoriaInactivaId);

            Response<SaleResultDTO> respuesta = await VenderAsync(Venta((viejo.Id, 1m)));

            Assert.False(respuesta.IsSuccess);
            Assert.Contains("categoría desactivada", respuesta.Message);
            Assert.Equal(5m, await StockActualAsync(viejo.Id));
        }

        [Fact]
        public async Task Cliente_inexistente_se_rechaza()
        {
            Product frijol = await CrearProductoAsync("Fríjol", 5500m, 5m);
            CreateSaleDTO venta = Venta((frijol.Id, 1m));
            venta.ClientId = Guid.NewGuid();

            Response<SaleResultDTO> respuesta = await VenderAsync(venta);

            Assert.False(respuesta.IsSuccess);
            Assert.Contains("cliente", respuesta.Message);
            Assert.Equal(5m, await StockActualAsync(frijol.Id));
        }

        // ---------- 8 y 9. Varios cajeros al mismo tiempo ----------

        /// <summary>
        /// Lanza N ventas en paralelo; cada "cajero" tiene su propio DataContext.
        /// </summary>
        private async Task<Response<SaleResultDTO>[]> VenderEnParaleloAsync(int cajeros, Guid productoId, decimal cantidad)
        {
            // Todas las tareas esperan esta señal para arrancar casi al mismo tiempo.
            TaskCompletionSource salida = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

            Task<Response<SaleResultDTO>>[] tareas = Enumerable.Range(0, cajeros)
                .Select(_ => Task.Run(async () =>
                {
                    await salida.Task;
                    return await VenderAsync(Venta((productoId, cantidad)));
                }))
                .ToArray();

            salida.SetResult();
            return await Task.WhenAll(tareas);
        }

        [Fact]
        public async Task Dos_cajeros_por_la_ultima_unidad_solo_uno_la_vende()
        {
            Product gaseosa = await CrearProductoAsync("Gaseosa", 3500m, 1m);

            Response<SaleResultDTO>[] respuestas = await VenderEnParaleloAsync(2, gaseosa.Id, 1m);

            Assert.Single(respuestas, r => r.IsSuccess);
            Response<SaleResultDTO> perdedor = respuestas.Single(r => !r.IsSuccess);
            Assert.Contains("Stock insuficiente", perdedor.Message);

            Assert.Equal(0m, await StockActualAsync(gaseosa.Id));
            Assert.Equal(1, await LineasVendidasAsync(gaseosa.Id));
        }

        [Fact]
        public async Task Veinte_ventas_simultaneas_con_stock_diez_venden_exactamente_diez()
        {
            Product panela = await CrearProductoAsync("Panela", 2800m, 10m);

            Response<SaleResultDTO>[] respuestas = await VenderEnParaleloAsync(20, panela.Id, 1m);

            Assert.Equal(10, respuestas.Count(r => r.IsSuccess));

            // Todos los fallos deben ser por stock: un deadlock u otro error aparecería aquí.
            Response<SaleResultDTO>[] fallos = respuestas.Where(r => !r.IsSuccess).ToArray();
            Assert.Equal(10, fallos.Length);
            Assert.All(fallos, f => Assert.Contains("Stock insuficiente", f.Message));

            Assert.Equal(0m, await StockActualAsync(panela.Id));
            Assert.Equal(10, await LineasVendidasAsync(panela.Id));

            // Cada venta exitosa recibió un número distinto.
            Assert.Equal(10, respuestas.Where(r => r.IsSuccess).Select(r => r.Result!.Number).Distinct().Count());
        }
    }
}
