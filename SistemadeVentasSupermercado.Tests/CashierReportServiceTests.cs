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
    /// Reporte por cajero. Fechas fijas de 2018 (cada prueba en un mes distinto) y cajeros propios.
    /// Los turnos abiertos de otras pruebas salen en cualquier rango, así que se miran solo las filas propias.
    /// </summary>
    [Collection(ColeccionBaseDeDatos.Nombre)]
    public class CashierReportServiceTests
    {
        private readonly BaseDeDatosFixture _fixture;

        public CashierReportServiceTests(BaseDeDatosFixture fixture) => _fixture = fixture;

        // ---------- Ayudantes ----------

        private static DateTime Utc(int month, int day, int hour = 15, int minute = 0)
            => new(2018, month, day, hour, minute, 0, DateTimeKind.Utc);

        private static DateOnly Dia(int month, int day) => new(2018, month, day);

        private async Task<Sale> VentaAsync(string cajero, DateTime fecha, decimal total,
            PaymentMethod metodo = PaymentMethod.Cash, string? anuladaPor = null, DateTime? anuladaEn = null)
        {
            await using DataContext context = _fixture.CrearContexto();
            Sale venta = new()
            {
                Id = Guid.NewGuid(), UserId = cajero, CreatedAtUtc = fecha, Total = total, PaymentMethod = metodo,
                Status = anuladaEn is null ? SaleStatus.Completed : SaleStatus.Cancelled,
                CancelledAtUtc = anuladaEn, CancelledByUserId = anuladaPor,
                CancelReason = anuladaEn is null ? null : "Motivo de la anulación"
            };
            context.Sales.Add(venta);
            await context.SaveChangesAsync();
            return venta;
        }

        private async Task DevolucionAsync(Sale venta, string registradaPor, DateTime fecha, decimal total)
        {
            await using DataContext context = _fixture.CrearContexto();
            context.SaleReturns.Add(new SaleReturn
            {
                Id = Guid.NewGuid(), SaleId = venta.Id, UserId = registradaPor, CreatedAtUtc = fecha, Total = total,
                RefundMethod = venta.PaymentMethod, Reason = "Motivo de la devolución"
            });
            await context.SaveChangesAsync();
        }

        private async Task TurnoAsync(string cajero, DateTime cierre, decimal esperado, decimal contado, string? cerradoPor = null)
        {
            await using DataContext context = _fixture.CrearContexto();
            context.CashSessions.Add(new CashSession
            {
                Id = Guid.NewGuid(), UserId = cajero, OpenedAtUtc = cierre.AddHours(-8), ClosedAtUtc = cierre,
                Status = CashSessionStatus.Closed, ClosedByUserId = cerradoPor ?? cajero,
                ExpectedAmount = esperado, CountedAmount = contado, Difference = contado - esperado
            });
            await context.SaveChangesAsync();
        }

        private async Task AbrirCajaAsync(string cajero)
        {
            await using DataContext context = _fixture.CrearContexto();
            context.CashSessions.Add(new CashSession { Id = Guid.NewGuid(), UserId = cajero, Status = CashSessionStatus.Open });
            await context.SaveChangesAsync();
        }

        private async Task<CashierReportDTO> ResumenAsync(DateOnly desde, DateOnly hasta, string? quienMira = null)
        {
            await using DataContext context = _fixture.CrearContexto();
            Response<CashierReportDTO> respuesta =
                await new CashierReportService(context).GetSummaryAsync(desde, hasta, quienMira ?? "nadie");
            Assert.True(respuesta.IsSuccess, respuesta.Message);
            return respuesta.Result!;
        }

        private async Task<Response<CashierDetailsDTO>> DetalleAsync(DateOnly desde, DateOnly hasta,
            string? cajero = null, string? quienMira = null)
        {
            await using DataContext context = _fixture.CrearContexto();
            return await new CashierReportService(context).GetDetailsAsync(desde, hasta, quienMira ?? "nadie", cajero);
        }

        // ---------- Pruebas ----------

        [Fact]
        public async Task Distingue_QuienCobro_DeQuienAnuloODevolvio()
        {
            string ana = await _fixture.CrearCajeroAsync("Ana");
            string beto = await _fixture.CrearCajeroAsync("Beto");

            await VentaAsync(ana, Utc(1, 10), 1000m);
            await VentaAsync(ana, Utc(1, 10), 2000m, PaymentMethod.Card);
            Sale conDevolucion = await VentaAsync(ana, Utc(1, 11), 5000m, PaymentMethod.Transfer);
            await VentaAsync(ana, Utc(1, 11), 700m, anuladaPor: beto, anuladaEn: Utc(1, 11, 16));
            await VentaAsync(beto, Utc(1, 11), 300m, anuladaPor: beto, anuladaEn: Utc(1, 11, 17));
            await DevolucionAsync(conDevolucion, beto, Utc(1, 12), 1500m);

            CashierReportDTO reporte = await ResumenAsync(Dia(1, 1), Dia(1, 31));
            CashierRowDTO a = reporte.Rows.Single(r => r.UserId == ana);
            CashierRowDTO b = reporte.Rows.Single(r => r.UserId == beto);

            Assert.Equal((3, 8000m), (a.SalesCount, a.SalesTotal));
            Assert.Equal((1000m, 2000m, 5000m), (a.CashTotal, a.CardTotal, a.TransferTotal));
            Assert.Equal((1, 700m), (a.OwnSalesCancelledCount, a.OwnSalesCancelledTotal));
            Assert.Equal((0, 0m), (a.CancellationsDoneCount, a.CancellationsDoneTotal));
            Assert.Equal((0, 1), (a.ReturnsRegisteredCount, a.ReturnsOnOwnSalesCount));
            Assert.Equal(1500m, a.ReturnsOnOwnSalesTotal);

            Assert.Equal(0, b.SalesCount); // su única venta está anulada
            Assert.Equal((1, 300m), (b.OwnSalesCancelledCount, b.OwnSalesCancelledTotal));
            Assert.Equal((2, 1000m), (b.CancellationsDoneCount, b.CancellationsDoneTotal));
            Assert.Equal((1, 1500m), (b.ReturnsRegisteredCount, b.ReturnsRegisteredTotal));
            Assert.Equal(0, b.ReturnsOnOwnSalesCount);

            // El detalle de cada uno trae las operaciones donde participó, con los dos nombres.
            CashierDetailsDTO deAna = (await DetalleAsync(Dia(1, 1), Dia(1, 31), ana)).Result!;
            CashierCancellationDTO anulacion = Assert.Single(deAna.Cancellations);
            Assert.StartsWith("Ana ", anulacion.ChargedByName);
            Assert.StartsWith("Beto ", anulacion.CancelledByName);
            CashierReturnDTO devolucion = Assert.Single(deAna.Returns);
            Assert.StartsWith("Ana ", devolucion.ChargedByName);
            Assert.StartsWith("Beto ", devolucion.RegisteredByName);
            Assert.Equal(a.SalesTotal, deAna.Cashier!.SalesTotal);

            CashierDetailsDTO deBeto = (await DetalleAsync(Dia(1, 1), Dia(1, 31), beto)).Result!;
            Assert.Equal(2, deBeto.Cancellations.Count);
            Assert.Single(deBeto.Returns);
        }

        [Fact]
        public async Task CadaDato_CuentaPorSuPropiaFecha_EnDiasDeColombia()
        {
            string cajero = await _fixture.CrearCajeroAsync();

            // 28/02 a las 11:59 p. m. y 1/03 a las 12:00 a. m. en Colombia.
            Sale deFebrero = await VentaAsync(cajero, Utc(3, 1, 4, 59), 100m);
            await VentaAsync(cajero, Utc(3, 1, 5, 0), 200m);
            // Cobrada en febrero, anulada y devuelta en marzo.
            await VentaAsync(cajero, Utc(2, 10), 900m, anuladaPor: cajero, anuladaEn: Utc(3, 5));
            await DevolucionAsync(deFebrero, cajero, Utc(3, 6), 50m);
            await TurnoAsync(cajero, Utc(3, 1, 4, 59), 100m, 90m);
            await TurnoAsync(cajero, Utc(3, 1, 5, 0), 100m, 130m);

            CashierRowDTO febrero = (await ResumenAsync(Dia(2, 1), Dia(2, 28))).Rows.Single(r => r.UserId == cajero);
            CashierRowDTO marzo = (await ResumenAsync(Dia(3, 1), Dia(3, 31))).Rows.Single(r => r.UserId == cajero);

            Assert.Equal((100m, 0, 0), (febrero.SalesTotal, febrero.OwnSalesCancelledCount, febrero.ReturnsRegisteredCount));
            Assert.Equal((10m, 0m), (febrero.ShortageTotal, febrero.SurplusTotal));
            Assert.Equal((200m, 1, 1), (marzo.SalesTotal, marzo.OwnSalesCancelledCount, marzo.ReturnsRegisteredCount));
            Assert.Equal((0m, 30m), (marzo.ShortageTotal, marzo.SurplusTotal));
        }

        [Fact]
        public async Task FaltantesYSobrantes_NoSeCompensan_YSeVeQuienCerro()
        {
            string cajero = await _fixture.CrearCajeroAsync("Carla");
            string supervisor = await _fixture.CrearCajeroAsync("Diego");

            await TurnoAsync(cajero, Utc(4, 2), 100_000m, 80_000m);
            await TurnoAsync(cajero, Utc(4, 3), 100_000m, 120_000m, cerradoPor: supervisor);
            await TurnoAsync(cajero, Utc(4, 4), 50_000m, 50_000m);
            await AbrirCajaAsync(cajero); // abierto hoy: sale aunque el rango sea de 2018

            CashierReportDTO reporte = await ResumenAsync(Dia(4, 1), Dia(4, 30));
            CashierRowDTO fila = reporte.Rows.Single(r => r.UserId == cajero);

            Assert.Equal((3, 2, 1), (fila.ClosedSessionsCount, fila.UnbalancedSessionsCount, fila.OpenSessionsCount));
            Assert.Equal((20_000m, 20_000m), (fila.ShortageTotal, fila.SurplusTotal));
            // Quien solo cerró el turno de otro no tiene cifras propias.
            Assert.DoesNotContain(reporte.Rows, r => r.UserId == supervisor);

            CashierDetailsDTO detalle = (await DetalleAsync(Dia(4, 1), Dia(4, 30), cajero)).Result!;
            Assert.Equal(4, detalle.Sessions.Count);
            CashierSessionDTO cerradoPorOtro = Assert.Single(detalle.Sessions, s => s.ClosedByOther);
            Assert.StartsWith("Diego ", cerradoPorOtro.ClosedByName);
            Assert.Equal(20_000m, cerradoPorOtro.Difference);
            Assert.Single(detalle.Sessions, s => s.IsOpen);

            // Y del supervisor: ni siquiera esos turnos, porque no son suyos.
            Assert.Empty((await DetalleAsync(Dia(4, 1), Dia(4, 30), supervisor)).Result!.Sessions);
        }

        [Fact]
        public async Task ElTotal_DeVentasYDevoluciones_CoincideConElReporteDeVentas()
        {
            string uno = await _fixture.CrearCajeroAsync();
            string dos = await _fixture.CrearCajeroAsync();
            Sale venta = await VentaAsync(uno, Utc(5, 3), 12_345.67m);
            await VentaAsync(dos, Utc(5, 20), 8_000m, PaymentMethod.Card);
            await VentaAsync(dos, Utc(5, 20), 999m, anuladaPor: uno, anuladaEn: Utc(5, 20, 18));
            await DevolucionAsync(venta, dos, Utc(5, 21), 345.67m);

            CashierReportDTO porCajero = await ResumenAsync(Dia(5, 1), Dia(5, 31));
            await using DataContext context = _fixture.CrearContexto();
            DailySalesRowDTO porDia = (await new ReportsService(context)
                .GetDailySalesAsync(Dia(5, 1), Dia(5, 31))).Result!.Totals;

            Assert.Equal(20_345.67m, porCajero.Totals.SalesTotal);
            Assert.Equal((porDia.SalesCount, porDia.SalesTotal), (porCajero.Totals.SalesCount, porCajero.Totals.SalesTotal));
            Assert.Equal((porDia.CashTotal, porDia.CardTotal, porDia.TransferTotal),
                (porCajero.Totals.CashTotal, porCajero.Totals.CardTotal, porCajero.Totals.TransferTotal));
            Assert.Equal((porDia.ReturnsCount, porDia.ReturnsTotal),
                (porCajero.Totals.ReturnsRegisteredCount, porCajero.Totals.ReturnsRegisteredTotal));
        }

        [Fact]
        public async Task ConLaCajaAbierta_NoSeVenLasCifrasPropias()
        {
            string yo = await _fixture.CrearCajeroAsync("Elena");
            string otro = await _fixture.CrearCajeroAsync("Fabio");
            Sale mia = await VentaAsync(yo, Utc(6, 5), 40_000m);
            await VentaAsync(otro, Utc(6, 5), 1_000m);
            await VentaAsync(yo, Utc(6, 5), 500m, anuladaPor: yo, anuladaEn: Utc(6, 5, 16));    // solo mía
            await VentaAsync(otro, Utc(6, 5), 600m, anuladaPor: yo, anuladaEn: Utc(6, 5, 17));  // de Fabio
            await DevolucionAsync(mia, yo, Utc(6, 6), 100m);                                    // solo mía
            await TurnoAsync(yo, Utc(6, 7), 1_000m, 900m);

            // Con la caja cerrada se ve todo.
            CashierReportDTO antes = await ResumenAsync(Dia(6, 1), Dia(6, 30), yo);
            Assert.False(antes.OwnFiguresHidden);
            Assert.Contains(antes.Rows, r => r.UserId == yo);
            Assert.True((await DetalleAsync(Dia(6, 1), Dia(6, 30), yo, yo)).IsSuccess);

            await AbrirCajaAsync(yo);

            CashierReportDTO reporte = await ResumenAsync(Dia(6, 1), Dia(6, 30), yo);
            Assert.True(reporte.OwnFiguresHidden);
            Assert.DoesNotContain(reporte.Rows, r => r.UserId == yo);
            Assert.Equal(1_000m, reporte.Totals.SalesTotal); // el total no permite deducir lo mío

            Response<CashierDetailsDTO> propio = await DetalleAsync(Dia(6, 1), Dia(6, 30), yo, yo);
            Assert.False(propio.IsSuccess);
            Assert.Equal(CashierReportService.OwnFiguresMessage, propio.Message);

            // El detalle de todos (el del Excel) no trae lo que es solo mío.
            CashierDetailsDTO todos = (await DetalleAsync(Dia(6, 1), Dia(6, 30), quienMira: yo)).Result!;
            Assert.True(todos.OwnFiguresHidden);
            Assert.Equal([600m], todos.Cancellations.Select(c => c.Total));
            Assert.Empty(todos.Returns);
            Assert.DoesNotContain(todos.Sessions, s => s.CashierName.StartsWith("Elena "));

            // Otra persona sí ve mis cifras.
            CashierReportDTO deOtro = await ResumenAsync(Dia(6, 1), Dia(6, 30), otro);
            Assert.False(deOtro.OwnFiguresHidden);
            Assert.Equal(40_000m, deOtro.Rows.Single(r => r.UserId == yo).SalesTotal);
        }

        [Fact]
        public async Task SalenLosDesactivados_YNoSalenLosQueNoTienenActividad()
        {
            string antiguo = await _fixture.CrearCajeroAsync();
            string sinActividad = await _fixture.CrearCajeroAsync();
            await VentaAsync(antiguo, Utc(7, 1), 10m);
            await using (DataContext context = _fixture.CrearContexto())
                await context.Users.Where(u => u.Id == antiguo)
                    .ExecuteUpdateAsync(s => s.SetProperty(u => u.DeactivatedAt, DateTime.UtcNow));

            CashierReportDTO reporte = await ResumenAsync(Dia(7, 1), Dia(7, 31));

            Assert.True(reporte.Rows.Single(r => r.UserId == antiguo).IsDeactivated);
            Assert.DoesNotContain(reporte.Rows, r => r.UserId == sinActividad);

            CashierDetailsDTO vacio = (await DetalleAsync(Dia(7, 1), Dia(7, 31), sinActividad)).Result!;
            Assert.Equal(0, vacio.Cashier!.SalesCount);
            Assert.False((await DetalleAsync(Dia(7, 1), Dia(7, 31), Guid.NewGuid().ToString())).IsSuccess);
        }

        [Fact]
        public async Task RangosInvalidos_SeRechazan()
        {
            await using DataContext context = _fixture.CrearContexto();
            CashierReportService servicio = new(context);

            Assert.False((await servicio.GetSummaryAsync(Dia(8, 2), Dia(8, 1), "x")).IsSuccess);
            Assert.False((await servicio.GetSummaryAsync(new(2018, 1, 1), new(2019, 1, 2), "x")).IsSuccess);
            Assert.False((await servicio.GetDetailsAsync(DateOnly.MaxValue, DateOnly.MaxValue, "x")).IsSuccess);
            Assert.False((await new ReportsService(context).GetDailySalesAsync(DateOnly.MaxValue, DateOnly.MaxValue)).IsSuccess);
        }
    }
}
