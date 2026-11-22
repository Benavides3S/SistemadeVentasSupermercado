using SistemadeVentasSupermercado.Tests.Infraestructura;
using SistemadeVentasSupermercado.Web.Core;
using SistemadeVentasSupermercado.Web.Data;
using SistemadeVentasSupermercado.Web.Data.Entities;
using SistemadeVentasSupermercado.Web.DTOs;
using SistemadeVentasSupermercado.Web.Services.Implementations;

namespace SistemadeVentasSupermercado.Tests
{
    /// <summary>
    /// Pruebas de los reportes. Insertan ventas con fechas fijas de 2020 (cada prueba en un mes
    /// distinto) para no mezclarse con las ventas de "hoy" que crean las demás pruebas.
    /// </summary>
    [Collection(ColeccionBaseDeDatos.Nombre)]
    public class ReportsServiceTests
    {
        private readonly BaseDeDatosFixture _fixture;

        public ReportsServiceTests(BaseDeDatosFixture fixture)
        {
            _fixture = fixture;
        }

        // ---------- Ayudantes ----------

        private static DateTime Utc(int year, int month, int day, int hour, int minute = 0)
            => new DateTime(year, month, day, hour, minute, 0, DateTimeKind.Utc);

        /// <summary>Inserta una venta con fecha fija (sin pasar por el servicio). Devuelve sus líneas.</summary>
        private async Task<List<SaleItem>> InsertarVentaAsync(DateTime fechaUtc, PaymentMethod metodo,
            SaleStatus estado, params (Product producto, decimal cantidad)[] lineas)
        {
            await using DataContext context = _fixture.CrearContexto();

            Sale venta = new Sale
            {
                Id = Guid.NewGuid(),
                UserId = _fixture.CajeroId,
                PaymentMethod = metodo,
                Status = estado,
                CreatedAtUtc = fechaUtc
            };

            foreach ((Product producto, decimal cantidad) in lineas)
            {
                venta.Items.Add(new SaleItem
                {
                    Id = Guid.NewGuid(),
                    ProductId = producto.Id,
                    ProductName = producto.Name,
                    UnitPrice = producto.Price,
                    Quantity = cantidad,
                    Unit = producto.Unit,
                    Total = Math.Round(producto.Price * cantidad, 2)
                });
            }
            venta.Total = venta.Items.Sum(i => i.Total);

            context.Sales.Add(venta);
            await context.SaveChangesAsync();
            return venta.Items.ToList();
        }

        private async Task InsertarDevolucionAsync(DateTime fechaUtc, SaleItem linea, decimal cantidad)
        {
            await using DataContext context = _fixture.CrearContexto();

            SaleReturn devolucion = new SaleReturn
            {
                Id = Guid.NewGuid(),
                SaleId = linea.SaleId,
                UserId = _fixture.CajeroId,
                CreatedAtUtc = fechaUtc,
                Reason = "Prueba de reporte",
                RefundMethod = PaymentMethod.Cash
            };
            devolucion.Items.Add(new SaleReturnItem
            {
                Id = Guid.NewGuid(),
                SaleItemId = linea.Id,
                ProductId = linea.ProductId,
                ProductName = linea.ProductName,
                Quantity = cantidad,
                UnitPrice = linea.UnitPrice,
                Unit = linea.Unit,
                Total = Math.Round(linea.UnitPrice * cantidad, 2),
                Restocked = true
            });
            devolucion.Total = devolucion.Items.Sum(i => i.Total);

            context.SaleReturns.Add(devolucion);
            await context.SaveChangesAsync();
        }

        private async Task<T> ConReportesAsync<T>(Func<ReportsService, Task<T>> accion)
        {
            await using DataContext context = _fixture.CrearContexto();
            return await accion(new ReportsService(context));
        }

        // ---------- Ventas por día ----------

        [Fact]
        public async Task La_venta_cuenta_en_el_dia_de_Colombia_no_en_el_de_UTC()
        {
            Product pan = await _fixture.CrearProductoAsync("Pan", 1000m, 100m);
            // 16/01 04:59 UTC = 15/01 23:59 en Colombia → cuenta el 15.
            await InsertarVentaAsync(Utc(2020, 1, 16, 4, 59), PaymentMethod.Cash, SaleStatus.Completed, (pan, 1m));
            // 16/01 05:00 UTC = 16/01 00:00 en Colombia → cuenta el 16.
            await InsertarVentaAsync(Utc(2020, 1, 16, 5, 0), PaymentMethod.Cash, SaleStatus.Completed, (pan, 2m));

            Response<DailySalesReportDTO> respuesta = await ConReportesAsync(r =>
                r.GetDailySalesAsync(new DateOnly(2020, 1, 15), new DateOnly(2020, 1, 16)));

            Assert.True(respuesta.IsSuccess, respuesta.Message);
            DailySalesRowDTO dia15 = respuesta.Result!.Days.Single(d => d.Date == new DateOnly(2020, 1, 15));
            DailySalesRowDTO dia16 = respuesta.Result.Days.Single(d => d.Date == new DateOnly(2020, 1, 16));
            Assert.Equal((1, 1000m), (dia15.SalesCount, dia15.SalesTotal));
            Assert.Equal((1, 2000m), (dia16.SalesCount, dia16.SalesTotal));
        }

        [Fact]
        public async Task Totales_por_metodo_sin_anuladas_y_devoluciones_el_dia_que_se_hacen()
        {
            Product cafe = await _fixture.CrearProductoAsync("Café", 10000m, 100m);
            DateTime dia1 = Utc(2020, 2, 10, 15);   // 10 a. m. en Colombia
            DateTime dia3 = Utc(2020, 2, 12, 15);

            List<SaleItem> enEfectivo = await InsertarVentaAsync(dia1, PaymentMethod.Cash, SaleStatus.Completed, (cafe, 2m)); // 20.000
            await InsertarVentaAsync(dia1, PaymentMethod.Card, SaleStatus.Completed, (cafe, 1m));                             // 10.000
            await InsertarVentaAsync(dia1, PaymentMethod.Transfer, SaleStatus.Completed, (cafe, 3m));                         // 30.000
            await InsertarVentaAsync(dia1, PaymentMethod.Cash, SaleStatus.Cancelled, (cafe, 5m));                             // anulada: no cuenta
            await InsertarDevolucionAsync(dia3, enEfectivo[0], 1m);                                                          // −10.000 el día 3

            Response<DailySalesReportDTO> respuesta = await ConReportesAsync(r =>
                r.GetDailySalesAsync(new DateOnly(2020, 2, 10), new DateOnly(2020, 2, 12)));

            DailySalesReportDTO reporte = respuesta.Result!;
            Assert.Equal(3, reporte.Days.Count); // incluye el día 11, sin movimiento

            DailySalesRowDTO d1 = reporte.Days[0];
            Assert.Equal(3, d1.SalesCount);
            Assert.Equal(60000m, d1.SalesTotal);
            Assert.Equal((20000m, 10000m, 30000m), (d1.CashTotal, d1.CardTotal, d1.TransferTotal));
            Assert.Equal(0m, d1.ReturnsTotal);

            DailySalesRowDTO d2 = reporte.Days[1];
            Assert.Equal((0, 0m, 0m), (d2.SalesCount, d2.SalesTotal, d2.NetTotal));

            DailySalesRowDTO d3 = reporte.Days[2];
            Assert.Equal((0, 1, 10000m), (d3.SalesCount, d3.ReturnsCount, d3.ReturnsTotal));
            Assert.Equal(-10000m, d3.NetTotal);

            Assert.Equal(60000m, reporte.Totals.SalesTotal);
            Assert.Equal(10000m, reporte.Totals.ReturnsTotal);
            Assert.Equal(50000m, reporte.Totals.NetTotal);
        }

        [Fact]
        public async Task Rango_invalido_o_demasiado_largo_se_rechaza()
        {
            Response<DailySalesReportDTO> alReves = await ConReportesAsync(r =>
                r.GetDailySalesAsync(new DateOnly(2020, 3, 10), new DateOnly(2020, 3, 1)));
            Response<List<TopProductDTO>> muyLargo = await ConReportesAsync(r =>
                r.GetTopProductsAsync(new DateOnly(2020, 1, 1), new DateOnly(2021, 1, 2)));
            Response<DailySalesReportDTO> unAnio = await ConReportesAsync(r =>
                r.GetDailySalesAsync(new DateOnly(2020, 1, 1), new DateOnly(2020, 12, 31))); // 366 días (bisiesto)

            Assert.False(alReves.IsSuccess);
            Assert.Contains("posterior", alReves.Message);
            Assert.False(muyLargo.IsSuccess);
            Assert.Contains("366", muyLargo.Message);
            Assert.True(unAnio.IsSuccess, unAnio.Message);
            Assert.Equal(366, unAnio.Result!.Days.Count);
        }

        // ---------- Más vendidos ----------

        [Fact]
        public async Task Mas_vendidos_por_dinero_neto_descontando_devoluciones()
        {
            Product arroz = await _fixture.CrearProductoAsync("Arroz", 2000m, 1000m);                        // por unidad
            Product carne = await _fixture.CrearProductoAsync("Carne", 30000m, 1000m, UnitOfMeasure.Kilogram); // por peso
            Product sal = await _fixture.CrearProductoAsync("Sal", 1000m, 1000m);
            DateTime dia = Utc(2020, 4, 5, 15);

            await InsertarVentaAsync(dia, PaymentMethod.Cash, SaleStatus.Completed, (arroz, 20m));          // 40.000
            List<SaleItem> conCarne = await InsertarVentaAsync(dia, PaymentMethod.Cash, SaleStatus.Completed, (carne, 2.5m)); // 75.000
            await InsertarVentaAsync(dia, PaymentMethod.Cash, SaleStatus.Completed, (sal, 3m));            //  3.000
            await InsertarVentaAsync(dia, PaymentMethod.Cash, SaleStatus.Cancelled, (sal, 500m));          // anulada: no cuenta
            await InsertarDevolucionAsync(Utc(2020, 4, 6, 15), conCarne[0], 1.5m);                         // −45.000 → carne neto 30.000

            Response<List<TopProductDTO>> respuesta = await ConReportesAsync(r =>
                r.GetTopProductsAsync(new DateOnly(2020, 4, 1), new DateOnly(2020, 4, 30)));

            Assert.True(respuesta.IsSuccess, respuesta.Message);
            List<TopProductDTO> ranking = respuesta.Result!;
            Assert.Equal(new[] { arroz.Id, carne.Id, sal.Id }, ranking.Select(p => p.ProductId));

            TopProductDTO filaCarne = ranking[1];
            Assert.Equal(UnitOfMeasure.Kilogram, filaCarne.Unit);
            Assert.Equal(2.5m, filaCarne.QuantitySold);
            Assert.Equal(1.5m, filaCarne.QuantityReturned);
            Assert.Equal(1m, filaCarne.NetQuantity);
            Assert.Equal(30000m, filaCarne.NetTotal);

            Assert.Equal(3m, ranking[2].QuantitySold); // la venta anulada de sal no cuenta
        }

        [Fact]
        public async Task Mas_vendidos_respeta_el_limite()
        {
            DateTime dia = Utc(2020, 5, 5, 15);
            for (int i = 1; i <= 4; i++)
            {
                Product p = await _fixture.CrearProductoAsync($"Producto{i}", 1000m * i, 100m);
                await InsertarVentaAsync(dia, PaymentMethod.Card, SaleStatus.Completed, (p, 1m));
            }

            Response<List<TopProductDTO>> respuesta = await ConReportesAsync(r =>
                r.GetTopProductsAsync(new DateOnly(2020, 5, 1), new DateOnly(2020, 5, 31), top: 2));

            Assert.Equal(2, respuesta.Result!.Count);
            Assert.Equal(new[] { 4000m, 3000m }, respuesta.Result.Select(p => p.NetTotal));
        }
    }
}
