using Microsoft.EntityFrameworkCore;
using SistemadeVentasSupermercado.Tests.Infraestructura;
using SistemadeVentasSupermercado.Web.Core;
using SistemadeVentasSupermercado.Web.Data;
using SistemadeVentasSupermercado.Web.Data.Entities;
using SistemadeVentasSupermercado.Web.DTOs;
using SistemadeVentasSupermercado.Web.Services.Implementations;

namespace SistemadeVentasSupermercado.Tests
{
    /// <summary>Pruebas de SalesService.CancelAsync: estado, stock, caja y concurrencia.</summary>
    [Collection(ColeccionBaseDeDatos.Nombre)]
    public class SaleCancellationTests
    {
        private readonly BaseDeDatosFixture _fixture;

        public SaleCancellationTests(BaseDeDatosFixture fixture)
        {
            _fixture = fixture;
        }

        // ---------- Ayudantes ----------

        private async Task<Response<SaleResultDTO>> VenderAsync(string cajeroId, PaymentMethod metodo,
            params (Guid productoId, decimal cantidad)[] lineas)
        {
            await using DataContext context = _fixture.CrearContexto();
            return await new SalesService(context).CreateAsync(new CreateSaleDTO
            {
                PaymentMethod = metodo,
                Items = lineas.Select(l => new SaleLineDTO { ProductId = l.productoId, Quantity = l.cantidad }).ToList(),
                IdempotencyKey = Guid.NewGuid()
            }, cajeroId);
        }

        private Task<Response<SaleResultDTO>> VenderAsync(params (Guid productoId, decimal cantidad)[] lineas)
            => VenderAsync(_fixture.CajeroId, PaymentMethod.Cash, lineas);

        private async Task<Response<SaleDetailDTO>> AnularAsync(Guid ventaId, string motivo = "Error del cajero")
        {
            await using DataContext context = _fixture.CrearContexto();
            return await new SalesService(context).CancelAsync(ventaId, new CancelSaleDTO { Reason = motivo }, _fixture.CajeroId);
        }

        private async Task<decimal> StockActualAsync(Guid productoId)
        {
            await using DataContext context = _fixture.CrearContexto();
            return await context.Products.Where(p => p.Id == productoId).Select(p => p.Stock).SingleAsync();
        }

        private async Task<T> ConCajaAsync<T>(Func<CashSessionService, Task<T>> accion)
        {
            await using DataContext context = _fixture.CrearContexto();
            return await accion(new CashSessionService(context));
        }

        // ---------- Casos ----------

        [Fact]
        public async Task Anular_devuelve_el_stock_y_guarda_quien_cuando_y_por_que()
        {
            Product arroz = await _fixture.CrearProductoAsync("Arroz", 2500m, 10m);
            Product queso = await _fixture.CrearProductoAsync("Queso", 20000m, 5m, UnitOfMeasure.Kilogram);
            Response<SaleResultDTO> venta = await VenderAsync((arroz.Id, 3m), (queso.Id, 1.25m));
            Assert.Equal(7m, await StockActualAsync(arroz.Id));
            Assert.Equal(3.75m, await StockActualAsync(queso.Id));

            Response<SaleDetailDTO> respuesta = await AnularAsync(venta.Result!.Id, "  Cliente se arrepintió  ");

            Assert.True(respuesta.IsSuccess, respuesta.Message);
            SaleDetailDTO anulada = respuesta.Result!;
            Assert.Equal(SaleStatus.Cancelled, anulada.Status);
            Assert.Equal("Cliente se arrepintió", anulada.CancelReason);
            Assert.Equal("Cajero Pruebas", anulada.CancelledByName);
            Assert.NotNull(anulada.CancelledAtUtc);
            Assert.False(anulada.CanBeCancelled);

            Assert.Equal(10m, await StockActualAsync(arroz.Id));
            Assert.Equal(5m, await StockActualAsync(queso.Id)); // los decimales del peso también vuelven
        }

        [Theory]
        [InlineData("")]
        [InlineData("   ")]
        public async Task El_motivo_es_obligatorio(string motivo)
        {
            Product pan = await _fixture.CrearProductoAsync("Pan", 300m, 5m);
            Response<SaleResultDTO> venta = await VenderAsync((pan.Id, 1m));

            Response<SaleDetailDTO> respuesta = await AnularAsync(venta.Result!.Id, motivo);

            Assert.False(respuesta.IsSuccess);
            Assert.Contains("motivo", respuesta.Message);
            Assert.Equal(4m, await StockActualAsync(pan.Id));
        }

        [Fact]
        public async Task Motivo_de_mas_de_200_caracteres_se_rechaza()
        {
            Product pan = await _fixture.CrearProductoAsync("Pan", 300m, 5m);
            Response<SaleResultDTO> venta = await VenderAsync((pan.Id, 1m));

            Response<SaleDetailDTO> respuesta = await AnularAsync(venta.Result!.Id, new string('x', 201));

            Assert.False(respuesta.IsSuccess);
            Assert.Equal(4m, await StockActualAsync(pan.Id));
        }

        [Fact]
        public async Task Una_venta_no_se_anula_dos_veces()
        {
            Product leche = await _fixture.CrearProductoAsync("Leche", 1200m, 10m);
            Response<SaleResultDTO> venta = await VenderAsync((leche.Id, 4m));
            await AnularAsync(venta.Result!.Id);

            Response<SaleDetailDTO> segunda = await AnularAsync(venta.Result.Id);

            Assert.False(segunda.IsSuccess);
            Assert.Contains("ya está anulada", segunda.Message);
            Assert.Equal(10m, await StockActualAsync(leche.Id)); // el stock volvió UNA sola vez
        }

        [Fact]
        public async Task Dos_anulaciones_simultaneas_devuelven_el_stock_una_sola_vez()
        {
            Product huevos = await _fixture.CrearProductoAsync("Huevos", 500m, 30m);
            Response<SaleResultDTO> venta = await VenderAsync((huevos.Id, 12m));
            Guid ventaId = venta.Result!.Id;

            TaskCompletionSource salida = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            Task<Response<SaleDetailDTO>>[] tareas = Enumerable.Range(0, 5)
                .Select(_ => Task.Run(async () => { await salida.Task; return await AnularAsync(ventaId); }))
                .ToArray();
            salida.SetResult();
            Response<SaleDetailDTO>[] respuestas = await Task.WhenAll(tareas);

            Assert.Single(respuestas, r => r.IsSuccess);
            Assert.All(respuestas.Where(r => !r.IsSuccess), r => Assert.Contains("ya está anulada", r.Message));
            Assert.Equal(30m, await StockActualAsync(huevos.Id));
        }

        [Fact]
        public async Task Con_el_turno_cerrado_no_se_puede_anular()
        {
            string cajero = await _fixture.CrearCajeroAsync();
            Response<CashSessionDTO> turno = await ConCajaAsync(c => c.OpenAsync(cajero, new OpenCashSessionDTO()));
            Product cafe = await _fixture.CrearProductoAsync("Café", 15000m, 5m);
            Response<SaleResultDTO> venta = await VenderAsync(cajero, PaymentMethod.Cash, (cafe.Id, 1m));
            await ConCajaAsync(c => c.CloseAsync(turno.Result!.Id, new CloseCashSessionDTO { CountedAmount = 15000m }, cajero, false));

            Response<SaleDetailDTO> respuesta = await AnularAsync(venta.Result!.Id);

            Assert.False(respuesta.IsSuccess);
            Assert.Contains("devolución", respuesta.Message);
            Assert.Equal(4m, await StockActualAsync(cafe.Id));

            await using DataContext context = _fixture.CrearContexto();
            Assert.Equal(SaleStatus.Completed, (await context.Sales.SingleAsync(s => s.Id == venta.Result.Id)).Status);
        }

        [Fact]
        public async Task La_venta_anulada_no_cuenta_en_el_efectivo_esperado()
        {
            string cajero = await _fixture.CrearCajeroAsync();
            Response<CashSessionDTO> turno = await ConCajaAsync(c => c.OpenAsync(cajero, new OpenCashSessionDTO { OpeningAmount = 10000m }));
            Product aceite = await _fixture.CrearProductoAsync("Aceite", 9000m, 5m);
            await VenderAsync(cajero, PaymentMethod.Cash, (aceite.Id, 1m));                              // queda
            Response<SaleResultDTO> anulada = await VenderAsync(cajero, PaymentMethod.Cash, (aceite.Id, 2m)); // se anula
            await AnularAsync(anulada.Result!.Id);

            Response<CashSessionDTO> cierre = await ConCajaAsync(c => c.CloseAsync(turno.Result!.Id,
                new CloseCashSessionDTO { CountedAmount = 19000m }, cajero, false));

            Assert.Equal(19000m, cierre.Result!.ExpectedAmount); // 10.000 + 9.000 (la anulada no cuenta)
            Assert.Equal(0m, cierre.Result.Difference);

            Response<CashSessionSummaryDTO> reporte = await ConCajaAsync(c => c.GetSummaryAsync(turno.Result!.Id));
            Assert.Equal(1, reporte.Result!.SalesCount);
            Assert.Equal(1, reporte.Result.CancelledCount);
            Assert.Equal(18000m, reporte.Result.CancelledTotal);
        }

        [Fact]
        public async Task Anular_una_venta_inexistente_falla()
        {
            Response<SaleDetailDTO> respuesta = await AnularAsync(Guid.NewGuid());

            Assert.False(respuesta.IsSuccess);
            Assert.Contains("no existe", respuesta.Message);
        }

        [Fact]
        public async Task El_comprobante_indica_si_se_puede_anular()
        {
            Product sal = await _fixture.CrearProductoAsync("Sal", 1000m, 5m);
            Response<SaleResultDTO> venta = await VenderAsync((sal.Id, 1m));

            await using DataContext context = _fixture.CrearContexto();
            Response<SaleDetailDTO> detalle = await new SalesService(context).GetOneAsync(venta.Result!.Id);

            Assert.True(detalle.Result!.CanBeCancelled);
        }
    }
}
