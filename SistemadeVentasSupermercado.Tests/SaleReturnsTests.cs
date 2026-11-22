using Microsoft.EntityFrameworkCore;
using SistemadeVentasSupermercado.Tests.Infraestructura;
using SistemadeVentasSupermercado.Web.Core;
using SistemadeVentasSupermercado.Web.Data;
using SistemadeVentasSupermercado.Web.Data.Entities;
using SistemadeVentasSupermercado.Web.DTOs;
using SistemadeVentasSupermercado.Web.Services.Implementations;

namespace SistemadeVentasSupermercado.Tests
{
    /// <summary>Pruebas de SaleReturnsService: cantidades, dinero, stock, caja y concurrencia.</summary>
    [Collection(ColeccionBaseDeDatos.Nombre)]
    public class SaleReturnsTests
    {
        private readonly BaseDeDatosFixture _fixture;

        public SaleReturnsTests(BaseDeDatosFixture fixture)
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

        /// <summary>Id de la línea de la venta que corresponde a un producto.</summary>
        private async Task<Guid> LineaAsync(Guid ventaId, Guid productoId)
        {
            await using DataContext context = _fixture.CrearContexto();
            return await context.SaleItems.Where(i => i.SaleId == ventaId && i.ProductId == productoId)
                .Select(i => i.Id).SingleAsync();
        }

        private async Task<Response<SaleReturnDetailDTO>> DevolverAsync(Guid ventaId, string? quien = null,
            string motivo = "Producto en mal estado", params (Guid lineaId, decimal cantidad, bool reintegrar)[] lineas)
        {
            await using DataContext context = _fixture.CrearContexto();
            return await new SaleReturnsService(context).CreateAsync(ventaId, new CreateSaleReturnDTO
            {
                Reason = motivo,
                Lines = lineas.Select(l => new SaleReturnLineDTO { SaleItemId = l.lineaId, Quantity = l.cantidad, Restock = l.reintegrar }).ToList()
            }, quien ?? _fixture.CajeroId);
        }

        private Task<Response<SaleReturnDetailDTO>> DevolverAsync(Guid ventaId, params (Guid lineaId, decimal cantidad, bool reintegrar)[] lineas)
            => DevolverAsync(ventaId, null, "Producto en mal estado", lineas);

        private async Task<decimal> StockActualAsync(Guid productoId)
        {
            await using DataContext context = _fixture.CrearContexto();
            return await context.Products.Where(p => p.Id == productoId).Select(p => p.Stock).SingleAsync();
        }

        private async Task<decimal> DevueltoDeVentaAsync(Guid ventaId)
        {
            await using DataContext context = _fixture.CrearContexto();
            return await context.Sales.Where(s => s.Id == ventaId).Select(s => s.ReturnedTotal).SingleAsync();
        }

        private async Task<T> ConCajaAsync<T>(Func<CashSessionService, Task<T>> accion)
        {
            await using DataContext context = _fixture.CrearContexto();
            return await accion(new CashSessionService(context));
        }

        // ---------- Devolución parcial ----------

        [Fact]
        public async Task Devolucion_parcial_reembolsa_al_precio_original_y_devuelve_stock()
        {
            Product jugo = await _fixture.CrearProductoAsync("Jugo", 3000m, 10m);
            Response<SaleResultDTO> venta = await VenderAsync((jugo.Id, 5m));
            Guid linea = await LineaAsync(venta.Result!.Id, jugo.Id);

            // El precio del producto cambia después de la venta: se reembolsa al precio ORIGINAL.
            await using (DataContext context = _fixture.CrearContexto())
                await context.Products.Where(p => p.Id == jugo.Id).ExecuteUpdateAsync(s => s.SetProperty(p => p.Price, 5000m));

            Response<SaleReturnDetailDTO> respuesta = await DevolverAsync(venta.Result.Id, (linea, 2m, true));

            Assert.True(respuesta.IsSuccess, respuesta.Message);
            SaleReturnDetailDTO devolucion = respuesta.Result!;
            Assert.True(devolucion.Number > 0);
            Assert.Equal(venta.Result.Number, devolucion.SaleNumber);
            Assert.Equal(6000m, devolucion.Total);                 // 2 × 3.000, no 2 × 5.000
            Assert.Equal(PaymentMethod.Cash, devolucion.RefundMethod);
            SaleReturnDetailLineDTO item = Assert.Single(devolucion.Items);
            Assert.Equal(2m, item.Quantity);
            Assert.True(item.Restocked);

            Assert.Equal(7m, await StockActualAsync(jugo.Id));     // 10 − 5 + 2
            Assert.Equal(6000m, await DevueltoDeVentaAsync(venta.Result.Id));
        }

        [Fact]
        public async Task Si_no_vuelve_al_inventario_el_stock_no_cambia()
        {
            Product yogur = await _fixture.CrearProductoAsync("Yogur", 2500m, 10m);
            Response<SaleResultDTO> venta = await VenderAsync((yogur.Id, 3m));
            Guid linea = await LineaAsync(venta.Result!.Id, yogur.Id);

            Response<SaleReturnDetailDTO> respuesta = await DevolverAsync(venta.Result.Id, (linea, 1m, false));

            Assert.True(respuesta.IsSuccess, respuesta.Message);
            Assert.False(Assert.Single(respuesta.Result!.Items).Restocked);
            Assert.Equal(7m, await StockActualAsync(yogur.Id));
            Assert.Equal(2500m, await DevueltoDeVentaAsync(venta.Result.Id)); // el dinero sí se devuelve
        }

        // ---------- Límites de cantidad ----------

        [Fact]
        public async Task No_se_puede_devolver_mas_de_lo_vendido()
        {
            Product pan = await _fixture.CrearProductoAsync("Pan", 300m, 10m);
            Response<SaleResultDTO> venta = await VenderAsync((pan.Id, 5m));
            Guid linea = await LineaAsync(venta.Result!.Id, pan.Id);

            Response<SaleReturnDetailDTO> demasiado = await DevolverAsync(venta.Result.Id, (linea, 6m, true));
            Assert.False(demasiado.IsSuccess);
            Assert.Contains("solo se pueden devolver 5", demasiado.Message);

            Response<SaleReturnDetailDTO> primera = await DevolverAsync(venta.Result.Id, (linea, 3m, true));
            Assert.True(primera.IsSuccess, primera.Message);

            // Cuentan las devoluciones anteriores: ya solo quedan 2.
            Response<SaleReturnDetailDTO> segunda = await DevolverAsync(venta.Result.Id, (linea, 3m, true));
            Assert.False(segunda.IsSuccess);
            Assert.Contains("solo se pueden devolver 2", segunda.Message);

            Assert.Equal(8m, await StockActualAsync(pan.Id)); // 10 − 5 + 3
        }

        [Fact]
        public async Task Producto_por_unidad_no_acepta_decimales_y_por_peso_si()
        {
            Product jabon = await _fixture.CrearProductoAsync("Jabón", 3000m, 10m);
            Product carne = await _fixture.CrearProductoAsync("Carne", 30000m, 10m, UnitOfMeasure.Kilogram);
            Response<SaleResultDTO> venta = await VenderAsync((jabon.Id, 2m), (carne.Id, 1.5m));
            Guid lineaJabon = await LineaAsync(venta.Result!.Id, jabon.Id);
            Guid lineaCarne = await LineaAsync(venta.Result.Id, carne.Id);

            Response<SaleReturnDetailDTO> unidad = await DevolverAsync(venta.Result.Id, (lineaJabon, 0.5m, true));
            Assert.False(unidad.IsSuccess);
            Assert.Contains("número entero", unidad.Message);

            Response<SaleReturnDetailDTO> peso = await DevolverAsync(venta.Result.Id, (lineaCarne, 0.25m, true));
            Assert.True(peso.IsSuccess, peso.Message);
            Assert.Equal(7500m, peso.Result!.Total);
            Assert.Equal(8.75m, await StockActualAsync(carne.Id)); // 10 − 1,5 + 0,25
        }

        [Fact]
        public async Task Los_redondeos_de_varias_devoluciones_nunca_superan_lo_cobrado()
        {
            // 1 kg a $999,99. Devolver 0,333 + 0,333 + 0,334 kg redondeando cada parte daría $1.000,00.
            Product queso = await _fixture.CrearProductoAsync("Queso", 999.99m, 5m, UnitOfMeasure.Kilogram);
            Response<SaleResultDTO> venta = await VenderAsync((queso.Id, 1m));
            Guid linea = await LineaAsync(venta.Result!.Id, queso.Id);

            Response<SaleReturnDetailDTO> d1 = await DevolverAsync(venta.Result.Id, (linea, 0.333m, true));
            Response<SaleReturnDetailDTO> d2 = await DevolverAsync(venta.Result.Id, (linea, 0.333m, true));
            Response<SaleReturnDetailDTO> d3 = await DevolverAsync(venta.Result.Id, (linea, 0.334m, true));

            Assert.True(d3.IsSuccess, d3.Message);
            Assert.Equal(333.00m, d1.Result!.Total);
            Assert.Equal(333.00m, d2.Result!.Total);
            Assert.Equal(333.99m, d3.Result!.Total);   // lo que faltaba de la línea, no 334,00
            Assert.Equal(999.99m, await DevueltoDeVentaAsync(venta.Result.Id));
            Assert.Equal(5m, await StockActualAsync(queso.Id));
        }

        // ---------- Validaciones ----------

        [Fact]
        public async Task Validaciones_de_motivo_lineas_vacias_repetidas_y_ajenas()
        {
            Product sal = await _fixture.CrearProductoAsync("Sal", 1000m, 10m);
            Response<SaleResultDTO> venta = await VenderAsync((sal.Id, 3m));
            Response<SaleResultDTO> otraVenta = await VenderAsync((sal.Id, 1m));
            Guid linea = await LineaAsync(venta.Result!.Id, sal.Id);
            Guid lineaAjena = await LineaAsync(otraVenta.Result!.Id, sal.Id);

            Response<SaleReturnDetailDTO> sinMotivo = await DevolverAsync(venta.Result.Id, null, "  ", (linea, 1m, true));
            Response<SaleReturnDetailDTO> todoEnCero = await DevolverAsync(venta.Result.Id, (linea, 0m, true));
            Response<SaleReturnDetailDTO> negativa = await DevolverAsync(venta.Result.Id, (linea, -1m, true));
            Response<SaleReturnDetailDTO> repetida = await DevolverAsync(venta.Result.Id, (linea, 1m, true), (linea, 1m, true));
            Response<SaleReturnDetailDTO> ajena = await DevolverAsync(venta.Result.Id, (lineaAjena, 1m, true));
            Response<SaleReturnDetailDTO> sinVenta = await DevolverAsync(Guid.NewGuid(), (linea, 1m, true));

            Assert.Contains("motivo", sinMotivo.Message);
            Assert.Contains("al menos un producto", todoEnCero.Message);
            Assert.Contains("negativas", negativa.Message);
            Assert.Contains("una sola vez", repetida.Message);
            Assert.Contains("no pertenece", ajena.Message);
            Assert.Contains("no existe", sinVenta.Message);
            Assert.All(new[] { sinMotivo, todoEnCero, negativa, repetida, ajena, sinVenta }, r => Assert.False(r.IsSuccess));

            Assert.Equal(6m, await StockActualAsync(sal.Id)); // nada se devolvió
            Assert.Equal(0m, await DevueltoDeVentaAsync(venta.Result.Id));
        }

        // ---------- Relación con la anulación ----------

        [Fact]
        public async Task Una_venta_anulada_no_admite_devoluciones()
        {
            Product te = await _fixture.CrearProductoAsync("Té", 4000m, 10m);
            Response<SaleResultDTO> venta = await VenderAsync((te.Id, 2m));
            Guid linea = await LineaAsync(venta.Result!.Id, te.Id);
            await using (DataContext context = _fixture.CrearContexto())
                await new SalesService(context).CancelAsync(venta.Result.Id, new CancelSaleDTO { Reason = "Error" }, _fixture.CajeroId);

            Response<SaleReturnDetailDTO> respuesta = await DevolverAsync(venta.Result.Id, (linea, 1m, true));

            Assert.False(respuesta.IsSuccess);
            Assert.Contains("anulada", respuesta.Message);
            Assert.Equal(10m, await StockActualAsync(te.Id)); // solo volvió por la anulación
        }

        [Fact]
        public async Task Una_venta_con_devoluciones_no_se_puede_anular()
        {
            Product cafe = await _fixture.CrearProductoAsync("Café", 15000m, 10m);
            Response<SaleResultDTO> venta = await VenderAsync((cafe.Id, 3m));
            Guid linea = await LineaAsync(venta.Result!.Id, cafe.Id);
            await DevolverAsync(venta.Result.Id, (linea, 1m, true));

            await using DataContext context = _fixture.CrearContexto();
            SalesService ventas = new SalesService(context);
            Response<SaleDetailDTO> anulacion = await ventas.CancelAsync(venta.Result.Id, new CancelSaleDTO { Reason = "Error" }, _fixture.CajeroId);

            Assert.False(anulacion.IsSuccess);
            Assert.Contains("tiene devoluciones", anulacion.Message);
            Assert.Equal(8m, await StockActualAsync(cafe.Id)); // 10 − 3 + 1, sin duplicar

            Response<SaleDetailDTO> detalle = await ventas.GetOneAsync(venta.Result.Id);
            Assert.False(detalle.Result!.CanBeCancelled);
            Assert.True(detalle.Result.CanBeReturned);
            Assert.Single(detalle.Result.Returns);
        }

        [Fact]
        public async Task Devuelto_todo_ya_no_se_puede_devolver_mas()
        {
            Product mani = await _fixture.CrearProductoAsync("Maní", 2000m, 10m);
            Response<SaleResultDTO> venta = await VenderAsync((mani.Id, 2m));
            Guid linea = await LineaAsync(venta.Result!.Id, mani.Id);
            await DevolverAsync(venta.Result.Id, (linea, 2m, true));

            await using DataContext context = _fixture.CrearContexto();
            Response<SaleDetailDTO> detalle = await new SalesService(context).GetOneAsync(venta.Result.Id);
            Assert.False(detalle.Result!.CanBeReturned);

            Response<ReturnableSaleDTO> devolvible = await new SaleReturnsService(context).GetReturnableAsync(venta.Result.Id);
            Assert.Equal(0m, Assert.Single(devolvible.Result!.Lines).RemainingQuantity);
        }

        // ---------- Caja ----------

        [Fact]
        public async Task Reembolso_en_efectivo_exige_caja_abierta_de_quien_devuelve()
        {
            string sinCaja = await _fixture.CrearCajeroAsync();
            Product arroz = await _fixture.CrearProductoAsync("Arroz", 2500m, 10m);
            Response<SaleResultDTO> enEfectivo = await VenderAsync(_fixture.CajeroId, PaymentMethod.Cash, (arroz.Id, 2m));
            Response<SaleResultDTO> conTarjeta = await VenderAsync(_fixture.CajeroId, PaymentMethod.Card, (arroz.Id, 2m));

            Response<SaleReturnDetailDTO> efectivo = await DevolverAsync(enEfectivo.Result!.Id, sinCaja, "Mal estado",
                (await LineaAsync(enEfectivo.Result.Id, arroz.Id), 1m, true));
            Assert.False(efectivo.IsSuccess);
            Assert.Contains("abre tu caja", efectivo.Message);

            // Con tarjeta no sale dinero del cajón: no necesita caja abierta.
            Response<SaleReturnDetailDTO> tarjeta = await DevolverAsync(conTarjeta.Result!.Id, sinCaja, "Mal estado",
                (await LineaAsync(conTarjeta.Result.Id, arroz.Id), 1m, true));
            Assert.True(tarjeta.IsSuccess, tarjeta.Message);
            Assert.Equal(PaymentMethod.Card, tarjeta.Result!.RefundMethod);

            await using DataContext context = _fixture.CrearContexto();
            Assert.Null((await context.SaleReturns.SingleAsync(r => r.Id == tarjeta.Result.Id)).CashSessionId);
        }

        [Fact]
        public async Task Reembolso_en_efectivo_resta_del_esperado_del_turno()
        {
            string cajero = await _fixture.CrearCajeroAsync();
            Response<CashSessionDTO> turno = await ConCajaAsync(c => c.OpenAsync(cajero, new OpenCashSessionDTO { OpeningAmount = 10000m }));
            Product leche = await _fixture.CrearProductoAsync("Leche", 2500m, 10m);
            Response<SaleResultDTO> venta = await VenderAsync(cajero, PaymentMethod.Cash, (leche.Id, 2m)); // +5.000
            Guid linea = await LineaAsync(venta.Result!.Id, leche.Id);
            await DevolverAsync(venta.Result.Id, cajero, "Vencida", (linea, 1m, false));                   // −2.500

            Response<CashSessionDTO> cierre = await ConCajaAsync(c => c.CloseAsync(turno.Result!.Id,
                new CloseCashSessionDTO { CountedAmount = 12500m }, cajero, false));

            Assert.Equal(12500m, cierre.Result!.ExpectedAmount); // 10.000 + 5.000 − 2.500
            Assert.Equal(0m, cierre.Result.Difference);

            Response<CashSessionSummaryDTO> reporte = await ConCajaAsync(c => c.GetSummaryAsync(turno.Result!.Id));
            Assert.Equal(1, reporte.Result!.CashRefundsCount);
            Assert.Equal(2500m, reporte.Result.CashRefundsTotal);
        }

        // ---------- Concurrencia ----------

        [Fact]
        public async Task Devoluciones_simultaneas_no_superan_lo_vendido()
        {
            Product agua = await _fixture.CrearProductoAsync("Agua", 1000m, 20m);
            // Con tarjeta: así las devoluciones solo compiten por la fila de la venta, no por una caja.
            Response<SaleResultDTO> venta = await VenderAsync(_fixture.CajeroId, PaymentMethod.Card, (agua.Id, 10m));
            Guid ventaId = venta.Result!.Id;
            Guid linea = await LineaAsync(ventaId, agua.Id);

            TaskCompletionSource salida = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            Task<Response<SaleReturnDetailDTO>>[] tareas = Enumerable.Range(0, 5)
                .Select(_ => Task.Run(async () => { await salida.Task; return await DevolverAsync(ventaId, (linea, 3m, true)); }))
                .ToArray();
            salida.SetResult();
            Response<SaleReturnDetailDTO>[] respuestas = await Task.WhenAll(tareas);

            // 10 vendidas: caben 3 devoluciones de 3 (= 9); las otras 2 se rechazan por cantidad.
            Assert.Equal(3, respuestas.Count(r => r.IsSuccess));
            Assert.All(respuestas.Where(r => !r.IsSuccess), r => Assert.Contains("solo se pueden devolver 1", r.Message));
            Assert.Equal(9000m, await DevueltoDeVentaAsync(ventaId));
            Assert.Equal(19m, await StockActualAsync(agua.Id)); // 20 − 10 + 9
        }
    }
}
