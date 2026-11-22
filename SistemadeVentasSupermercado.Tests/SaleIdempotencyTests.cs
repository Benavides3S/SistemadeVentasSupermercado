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
    /// Reintentos de cobro: si el mismo cobro (mismo código) llega dos veces, por ejemplo porque se
    /// cortó la red antes de recibir la respuesta, la venta se registra una sola vez.
    /// </summary>
    [Collection(ColeccionBaseDeDatos.Nombre)]
    public class SaleIdempotencyTests
    {
        private readonly BaseDeDatosFixture _fixture;

        public SaleIdempotencyTests(BaseDeDatosFixture fixture)
        {
            _fixture = fixture;
        }

        // ---------- Ayudantes ----------

        private static CreateSaleDTO Venta(Guid codigo, Guid productoId, decimal cantidad) => new CreateSaleDTO
        {
            PaymentMethod = PaymentMethod.Cash,
            Items = { new SaleLineDTO { ProductId = productoId, Quantity = cantidad } },
            IdempotencyKey = codigo
        };

        /// <summary>Cada llamada usa su propio contexto, como una petición web distinta.</summary>
        private async Task<Response<SaleResultDTO>> VenderAsync(CreateSaleDTO venta, string? cajeroId = null)
        {
            await using DataContext context = _fixture.CrearContexto();
            return await new SalesService(context).CreateAsync(venta, cajeroId ?? _fixture.CajeroId);
        }

        private async Task<decimal> StockActualAsync(Guid productoId)
        {
            await using DataContext context = _fixture.CrearContexto();
            return await context.Products.Where(p => p.Id == productoId).Select(p => p.Stock).SingleAsync();
        }

        private async Task<int> VentasConCodigoAsync(Guid codigo)
        {
            await using DataContext context = _fixture.CrearContexto();
            return await context.Sales.CountAsync(s => s.IdempotencyKey == codigo);
        }

        /// <summary>Crea un cajero con su caja abierta y devuelve su id y el del turno.</summary>
        private async Task<(string cajeroId, Guid turnoId)> CajeroConCajaAbiertaAsync(string nombre = "Cajero")
        {
            string cajero = await _fixture.CrearCajeroAsync(nombre);
            await using DataContext context = _fixture.CrearContexto();
            Response<CashSessionDTO> turno = await new CashSessionService(context)
                .OpenAsync(cajero, new OpenCashSessionDTO { OpeningAmount = 0m });
            Assert.True(turno.IsSuccess, turno.Message);
            return (cajero, turno.Result!.Id);
        }

        // ---------- Pruebas ----------

        [Fact]
        public async Task Mismo_codigo_dos_veces_registra_una_sola_venta()
        {
            Product arroz = await _fixture.CrearProductoAsync("Arroz", 2500m, 10m);
            Guid codigo = Guid.NewGuid();

            Response<SaleResultDTO> primera = await VenderAsync(Venta(codigo, arroz.Id, 3m));
            Response<SaleResultDTO> reintento = await VenderAsync(Venta(codigo, arroz.Id, 3m));

            Assert.True(primera.IsSuccess, primera.Message);
            Assert.False(primera.Result!.AlreadyRegistered);

            Assert.True(reintento.IsSuccess, reintento.Message);
            Assert.True(reintento.Result!.AlreadyRegistered);
            Assert.Equal(primera.Result.Id, reintento.Result.Id);
            Assert.Equal(primera.Result.Number, reintento.Result.Number);
            Assert.Equal(7500m, reintento.Result.Total);

            Assert.Equal(7m, await StockActualAsync(arroz.Id)); // se descontó una sola vez
            Assert.Equal(1, await VentasConCodigoAsync(codigo));
        }

        [Fact]
        public async Task Reintentos_simultaneos_con_el_mismo_codigo_registran_una_sola_venta()
        {
            Product leche = await _fixture.CrearProductoAsync("Leche", 1200m, 10m);
            Guid codigo = Guid.NewGuid();

            TaskCompletionSource salida = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            Task<Response<SaleResultDTO>>[] tareas = Enumerable.Range(0, 5)
                .Select(_ => Task.Run(async () =>
                {
                    await salida.Task;
                    return await VenderAsync(Venta(codigo, leche.Id, 2m));
                }))
                .ToArray();
            salida.SetResult();
            Response<SaleResultDTO>[] respuestas = await Task.WhenAll(tareas);

            // Todas responden con éxito y con la MISMA venta; solo una la registró de verdad.
            Assert.All(respuestas, r => Assert.True(r.IsSuccess, r.Message));
            Assert.Single(respuestas.Select(r => r.Result!.Id).Distinct());
            Assert.Single(respuestas, r => !r.Result!.AlreadyRegistered);

            Assert.Equal(8m, await StockActualAsync(leche.Id));
            Assert.Equal(1, await VentasConCodigoAsync(codigo));
        }

        [Fact]
        public async Task Una_venta_que_fallo_no_gasta_el_codigo()
        {
            Product aceite = await _fixture.CrearProductoAsync("Aceite", 9000m, 1m);
            Guid codigo = Guid.NewGuid();

            Response<SaleResultDTO> fallida = await VenderAsync(Venta(codigo, aceite.Id, 2m));
            Assert.False(fallida.IsSuccess);
            Assert.Contains("Stock insuficiente", fallida.Message);

            // Llega mercancía y el cajero vuelve a cobrar con el mismo código.
            await using (DataContext context = _fixture.CrearContexto())
            {
                await context.Products.Where(p => p.Id == aceite.Id)
                    .ExecuteUpdateAsync(s => s.SetProperty(p => p.Stock, 5m));
            }

            Response<SaleResultDTO> reintento = await VenderAsync(Venta(codigo, aceite.Id, 2m));

            Assert.True(reintento.IsSuccess, reintento.Message);
            Assert.False(reintento.Result!.AlreadyRegistered);
            Assert.Equal(3m, await StockActualAsync(aceite.Id));
            Assert.Equal(1, await VentasConCodigoAsync(codigo));
        }

        [Fact]
        public async Task Reintento_despues_de_cerrar_la_caja_devuelve_la_venta_y_no_un_error()
        {
            (string cajero, Guid turno) = await CajeroConCajaAbiertaAsync();
            Product pan = await _fixture.CrearProductoAsync("Pan", 300m, 10m);
            Guid codigo = Guid.NewGuid();

            Response<SaleResultDTO> primera = await VenderAsync(Venta(codigo, pan.Id, 2m), cajero);
            Assert.True(primera.IsSuccess, primera.Message);

            await using (DataContext context = _fixture.CrearContexto())
            {
                Response<CashSessionDTO> cierre = await new CashSessionService(context)
                    .CloseAsync(turno, new CloseCashSessionDTO { CountedAmount = 600m }, cajero, false);
                Assert.True(cierre.IsSuccess, cierre.Message);
            }

            Response<SaleResultDTO> reintento = await VenderAsync(Venta(codigo, pan.Id, 2m), cajero);

            Assert.True(reintento.IsSuccess, reintento.Message);
            Assert.True(reintento.Result!.AlreadyRegistered);
            Assert.Equal(primera.Result!.Number, reintento.Result.Number);
            Assert.Equal(8m, await StockActualAsync(pan.Id));
        }

        [Fact]
        public async Task Venta_sin_codigo_se_rechaza()
        {
            Product sal = await _fixture.CrearProductoAsync("Sal", 1000m, 5m);

            Response<SaleResultDTO> respuesta = await VenderAsync(Venta(Guid.Empty, sal.Id, 1m));

            Assert.False(respuesta.IsSuccess);
            Assert.Contains("Recarga la página", respuesta.Message);
            Assert.Equal(5m, await StockActualAsync(sal.Id));
        }

        [Fact]
        public async Task El_mismo_codigo_en_dos_cajeros_son_ventas_distintas()
        {
            (string cajeroA, _) = await CajeroConCajaAbiertaAsync("Cajero A");
            (string cajeroB, _) = await CajeroConCajaAbiertaAsync("Cajero B");
            Product huevos = await _fixture.CrearProductoAsync("Huevos", 500m, 10m);
            Guid codigo = Guid.NewGuid();

            Response<SaleResultDTO> deA = await VenderAsync(Venta(codigo, huevos.Id, 1m), cajeroA);
            Response<SaleResultDTO> deB = await VenderAsync(Venta(codigo, huevos.Id, 1m), cajeroB);

            // Un cajero nunca recibe la venta de otro aunque conozca su código.
            Assert.True(deA.IsSuccess, deA.Message);
            Assert.True(deB.IsSuccess, deB.Message);
            Assert.False(deB.Result!.AlreadyRegistered);
            Assert.NotEqual(deA.Result!.Id, deB.Result.Id);
            Assert.Equal(8m, await StockActualAsync(huevos.Id));
        }
    }
}
