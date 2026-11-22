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
    /// Pruebas de la caja: apertura, cierre con arqueo y su relación con las ventas.
    /// Cada prueba usa un cajero propio para no afectar la caja del cajero principal.
    /// </summary>
    [Collection(ColeccionBaseDeDatos.Nombre)]
    public class CashSessionServiceTests
    {
        private readonly BaseDeDatosFixture _fixture;

        public CashSessionServiceTests(BaseDeDatosFixture fixture)
        {
            _fixture = fixture;
        }

        // ---------- Ayudantes ----------

        private async Task<T> ConCajaAsync<T>(Func<CashSessionService, Task<T>> accion)
        {
            await using DataContext context = _fixture.CrearContexto();
            return await accion(new CashSessionService(context));
        }

        private Task<Response<CashSessionDTO>> AbrirAsync(string cajeroId, decimal monto)
            => ConCajaAsync(c => c.OpenAsync(cajeroId, new OpenCashSessionDTO { OpeningAmount = monto }));

        private Task<Response<CashSessionDTO>> CerrarAsync(Guid turnoId, decimal contado, string quienCierra,
            bool puedeCerrarOtras = false, string? notas = null)
            => ConCajaAsync(c => c.CloseAsync(turnoId,
                new CloseCashSessionDTO { CountedAmount = contado, Notes = notas }, quienCierra, puedeCerrarOtras));

        private async Task<Response<SaleResultDTO>> VenderAsync(string cajeroId, Guid productoId, decimal cantidad,
            PaymentMethod metodo = PaymentMethod.Cash)
        {
            await using DataContext context = _fixture.CrearContexto();
            return await new SalesService(context).CreateAsync(new CreateSaleDTO
            {
                PaymentMethod = metodo,
                Items = { new SaleLineDTO { ProductId = productoId, Quantity = cantidad } },
                IdempotencyKey = Guid.NewGuid()
            }, cajeroId);
        }

        private async Task<decimal> StockActualAsync(Guid productoId)
        {
            await using DataContext context = _fixture.CrearContexto();
            return await context.Products.Where(p => p.Id == productoId).Select(p => p.Stock).SingleAsync();
        }

        // ---------- Apertura ----------

        [Fact]
        public async Task Abrir_caja_crea_un_turno_abierto()
        {
            string cajero = await _fixture.CrearCajeroAsync();

            Response<CashSessionDTO> respuesta = await AbrirAsync(cajero, 50000m);

            Assert.True(respuesta.IsSuccess, respuesta.Message);
            Assert.Equal(CashSessionStatus.Open, respuesta.Result!.Status);
            Assert.Equal(50000m, respuesta.Result.OpeningAmount);
            Assert.True(respuesta.Result.Number > 0);

            Response<CashSessionDTO?> actual = await ConCajaAsync(c => c.GetCurrentAsync(cajero));
            Assert.Equal(respuesta.Result.Id, actual.Result!.Id);
        }

        [Fact]
        public async Task No_se_puede_abrir_una_segunda_caja()
        {
            string cajero = await _fixture.CrearCajeroAsync();
            await AbrirAsync(cajero, 0m);

            Response<CashSessionDTO> segunda = await AbrirAsync(cajero, 0m);

            Assert.False(segunda.IsSuccess);
            Assert.Contains("Ya tienes una caja abierta", segunda.Message);
        }

        [Fact]
        public async Task Dos_aperturas_simultaneas_solo_abren_una_caja()
        {
            string cajero = await _fixture.CrearCajeroAsync();
            TaskCompletionSource salida = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

            Task<Response<CashSessionDTO>>[] tareas = Enumerable.Range(0, 5)
                .Select(_ => Task.Run(async () => { await salida.Task; return await AbrirAsync(cajero, 10000m); }))
                .ToArray();
            salida.SetResult();
            Response<CashSessionDTO>[] respuestas = await Task.WhenAll(tareas);

            Assert.Single(respuestas, r => r.IsSuccess);
            Assert.All(respuestas.Where(r => !r.IsSuccess), r => Assert.Contains("Ya tienes una caja abierta", r.Message));

            await using DataContext context = _fixture.CrearContexto();
            Assert.Equal(1, await context.CashSessions.CountAsync(c => c.UserId == cajero && c.Status == CashSessionStatus.Open));
        }

        [Fact]
        public async Task Monto_inicial_negativo_se_rechaza()
        {
            string cajero = await _fixture.CrearCajeroAsync();

            Response<CashSessionDTO> respuesta = await AbrirAsync(cajero, -1m);

            Assert.False(respuesta.IsSuccess);
            Assert.Contains("negativo", respuesta.Message);
        }

        // ---------- Ventas y caja ----------

        [Fact]
        public async Task Sin_caja_abierta_no_se_puede_vender()
        {
            string cajero = await _fixture.CrearCajeroAsync();
            Product arroz = await _fixture.CrearProductoAsync("Arroz", 2500m, 5m);

            Response<SaleResultDTO> respuesta = await VenderAsync(cajero, arroz.Id, 1m);

            Assert.False(respuesta.IsSuccess);
            Assert.Contains("Abre la caja", respuesta.Message);
            Assert.Equal(5m, await StockActualAsync(arroz.Id));
        }

        [Fact]
        public async Task La_venta_queda_en_el_turno_del_cajero()
        {
            string cajero = await _fixture.CrearCajeroAsync();
            Response<CashSessionDTO> turno = await AbrirAsync(cajero, 0m);
            Product pan = await _fixture.CrearProductoAsync("Pan", 300m, 5m);

            Response<SaleResultDTO> venta = await VenderAsync(cajero, pan.Id, 2m);

            Assert.True(venta.IsSuccess, venta.Message);
            await using DataContext context = _fixture.CrearContexto();
            Sale guardada = await context.Sales.SingleAsync(s => s.Id == venta.Result!.Id);
            Assert.Equal(turno.Result!.Id, guardada.CashSessionId);
        }

        // ---------- Cierre y arqueo ----------

        [Fact]
        public async Task Cerrar_caja_calcula_esperado_y_diferencia()
        {
            string cajero = await _fixture.CrearCajeroAsync();
            Response<CashSessionDTO> turno = await AbrirAsync(cajero, 50000m);
            Product cafe = await _fixture.CrearProductoAsync("Café", 5000m, 20m);

            await VenderAsync(cajero, cafe.Id, 2m);                         // 10.000 efectivo
            await VenderAsync(cajero, cafe.Id, 1m);                         //  5.000 efectivo
            await VenderAsync(cajero, cafe.Id, 3m, PaymentMethod.Card);     // 15.000 tarjeta (no va al cajón)

            // Esperado: 50.000 + 10.000 + 5.000 = 65.000. Se cuentan 64.000 → faltan 1.000.
            Response<CashSessionDTO> cierre = await CerrarAsync(turno.Result!.Id, 64000m, cajero, notas: "  Faltó un billete  ");

            Assert.True(cierre.IsSuccess, cierre.Message);
            CashSessionDTO cerrado = cierre.Result!;
            Assert.Equal(CashSessionStatus.Closed, cerrado.Status);
            Assert.Equal(65000m, cerrado.ExpectedAmount);
            Assert.Equal(64000m, cerrado.CountedAmount);
            Assert.Equal(-1000m, cerrado.Difference);
            Assert.Equal("Faltó un billete", cerrado.Notes);
            Assert.NotNull(cerrado.ClosedAtUtc);
            Assert.NotNull(cerrado.ClosedByName);

            Response<CashSessionSummaryDTO> reporte = await ConCajaAsync(c => c.GetSummaryAsync(turno.Result.Id));
            CashSessionSummaryDTO resumen = reporte.Result!;
            Assert.Equal(3, resumen.SalesCount);
            Assert.Equal(30000m, resumen.SalesTotal);
            Assert.Equal(65000m, resumen.ExpectedCash);

            PaymentTotalDTO efectivo = resumen.Payments.Single(p => p.PaymentMethod == PaymentMethod.Cash);
            PaymentTotalDTO tarjeta = resumen.Payments.Single(p => p.PaymentMethod == PaymentMethod.Card);
            PaymentTotalDTO transferencia = resumen.Payments.Single(p => p.PaymentMethod == PaymentMethod.Transfer);
            Assert.Equal((2, 15000m), (efectivo.Count, efectivo.Total));
            Assert.Equal((1, 15000m), (tarjeta.Count, tarjeta.Total));
            Assert.Equal((0, 0m), (transferencia.Count, transferencia.Total));
        }

        [Fact]
        public async Task Una_caja_cerrada_no_se_puede_cerrar_otra_vez()
        {
            string cajero = await _fixture.CrearCajeroAsync();
            Response<CashSessionDTO> turno = await AbrirAsync(cajero, 1000m);
            await CerrarAsync(turno.Result!.Id, 1000m, cajero);

            Response<CashSessionDTO> segundo = await CerrarAsync(turno.Result.Id, 999999m, cajero);

            Assert.False(segundo.IsSuccess);
            Assert.Contains("ya estaba cerrada", segundo.Message);

            // El primer arqueo no se alteró.
            Response<CashSessionSummaryDTO> reporte = await ConCajaAsync(c => c.GetSummaryAsync(turno.Result.Id));
            Assert.Equal(1000m, reporte.Result!.Session.CountedAmount);
        }

        [Fact]
        public async Task Despues_de_cerrar_no_se_puede_vender_y_se_puede_abrir_otra()
        {
            string cajero = await _fixture.CrearCajeroAsync();
            Response<CashSessionDTO> turno = await AbrirAsync(cajero, 0m);
            await CerrarAsync(turno.Result!.Id, 0m, cajero);
            Product sal = await _fixture.CrearProductoAsync("Sal", 1000m, 5m);

            Response<SaleResultDTO> venta = await VenderAsync(cajero, sal.Id, 1m);
            Assert.False(venta.IsSuccess);
            Assert.Contains("Abre la caja", venta.Message);

            Response<CashSessionDTO> nueva = await AbrirAsync(cajero, 0m);
            Assert.True(nueva.IsSuccess, nueva.Message);
            Assert.NotEqual(turno.Result.Id, nueva.Result!.Id);
        }

        [Fact]
        public async Task Solo_un_supervisor_puede_cerrar_la_caja_de_otro()
        {
            string cajero = await _fixture.CrearCajeroAsync();
            string otro = await _fixture.CrearCajeroAsync("Supervisor");
            Response<CashSessionDTO> turno = await AbrirAsync(cajero, 0m);

            Response<CashSessionDTO> sinPermiso = await CerrarAsync(turno.Result!.Id, 0m, otro, puedeCerrarOtras: false);
            Assert.False(sinPermiso.IsSuccess);
            Assert.Contains("propia caja", sinPermiso.Message);

            Response<CashSessionDTO> conPermiso = await CerrarAsync(turno.Result.Id, 0m, otro, puedeCerrarOtras: true);
            Assert.True(conPermiso.IsSuccess, conPermiso.Message);
            Assert.Equal("Supervisor", conPermiso.Result!.ClosedByName!.Split(' ')[0]);
        }

        [Fact]
        public async Task Efectivo_contado_negativo_se_rechaza()
        {
            string cajero = await _fixture.CrearCajeroAsync();
            Response<CashSessionDTO> turno = await AbrirAsync(cajero, 0m);

            Response<CashSessionDTO> respuesta = await CerrarAsync(turno.Result!.Id, -5m, cajero);

            Assert.False(respuesta.IsSuccess);
            Response<CashSessionDTO?> actual = await ConCajaAsync(c => c.GetCurrentAsync(cajero));
            Assert.NotNull(actual.Result); // sigue abierta
        }

        [Fact]
        public async Task Cierre_simultaneo_con_ventas_deja_el_arqueo_cuadrado()
        {
            string cajero = await _fixture.CrearCajeroAsync();
            Response<CashSessionDTO> turno = await AbrirAsync(cajero, 20000m);
            Product leche = await _fixture.CrearProductoAsync("Leche", 1000m, 100m);
            Guid turnoId = turno.Result!.Id;

            TaskCompletionSource salida = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

            Task[] ventas = Enumerable.Range(0, 15)
                .Select(_ => (Task)Task.Run(async () => { await salida.Task; await VenderAsync(cajero, leche.Id, 1m); }))
                .ToArray();
            Task<Response<CashSessionDTO>> cierre = Task.Run(async () =>
            {
                await salida.Task;
                await Task.Delay(5); // deja que algunas ventas arranquen primero
                return await CerrarAsync(turnoId, 0m, cajero);
            });

            salida.SetResult();
            await Task.WhenAll(ventas);
            Response<CashSessionDTO> resultadoCierre = await cierre;
            Assert.True(resultadoCierre.IsSuccess, resultadoCierre.Message);

            // Lo que importa: el esperado guardado coincide EXACTAMENTE con las ventas que quedaron en el turno.
            await using DataContext context = _fixture.CrearContexto();
            decimal ventasEnTurno = await context.Sales
                .Where(s => s.CashSessionId == turnoId && s.Status == SaleStatus.Completed && s.PaymentMethod == PaymentMethod.Cash)
                .SumAsync(s => (decimal?)s.Total) ?? 0m;
            int unidadesVendidas = await context.Sales.CountAsync(s => s.CashSessionId == turnoId);

            Assert.Equal(20000m + ventasEnTurno, resultadoCierre.Result!.ExpectedAmount);
            // Y el stock coincide con lo vendido: ninguna venta rechazada descontó stock.
            Assert.Equal(100m - unidadesVendidas, await StockActualAsync(leche.Id));
        }
    }
}
