using SistemadeVentasSupermercado.Tests.Infraestructura;
using SistemadeVentasSupermercado.Web.Core;
using SistemadeVentasSupermercado.Web.Data;
using SistemadeVentasSupermercado.Web.Data.Entities;
using SistemadeVentasSupermercado.Web.DTOs;
using SistemadeVentasSupermercado.Web.Services.Implementations;

namespace SistemadeVentasSupermercado.Tests
{
    /// <summary>Resumen del Inicio: solo calcula lo que se pide, ventas de hoy, caja propia y productos con poco stock.</summary>
    [Collection(ColeccionBaseDeDatos.Nombre)]
    public class HomeServiceTests
    {
        private readonly BaseDeDatosFixture _fixture;

        public HomeServiceTests(BaseDeDatosFixture fixture)
        {
            _fixture = fixture;
        }

        private async Task<HomeSummaryDTO> ResumenAsync(HomeRequestDTO pedido)
        {
            await using DataContext context = _fixture.CrearContexto();
            Response<HomeSummaryDTO> respuesta = await new HomeService(context).GetSummaryAsync(pedido);
            Assert.True(respuesta.IsSuccess, respuesta.Message);
            return respuesta.Result!;
        }

        private async Task<Response<SaleResultDTO>> VenderAsync(Product producto, decimal cantidad)
        {
            await using DataContext context = _fixture.CrearContexto();
            return await new SalesService(context).CreateAsync(new CreateSaleDTO
            {
                PaymentMethod = PaymentMethod.Cash,
                Items = { new SaleLineDTO { ProductId = producto.Id, Quantity = cantidad } },
                IdempotencyKey = Guid.NewGuid()
            }, _fixture.CajeroId);
        }

        [Fact]
        public async Task Sin_permisos_no_calcula_nada()
        {
            HomeSummaryDTO resumen = await ResumenAsync(new HomeRequestDTO { UserId = _fixture.CajeroId });

            Assert.Null(resumen.SalesCountToday);
            Assert.Null(resumen.HasOpenCashSession);
            Assert.Null(resumen.LowStock);
        }

        [Fact]
        public async Task Las_ventas_de_hoy_suman_las_completadas_y_no_las_anuladas()
        {
            Product producto = await _fixture.CrearProductoAsync("Para el inicio", 1000m, 20m);
            HomeRequestDTO pedido = new HomeRequestDTO { UserId = _fixture.CajeroId, WantsSalesToday = true };
            HomeSummaryDTO antes = await ResumenAsync(pedido);

            Response<SaleResultDTO> buena = await VenderAsync(producto, 2m);
            Response<SaleResultDTO> anulada = await VenderAsync(producto, 3m);
            Assert.True(buena.IsSuccess && anulada.IsSuccess);
            await using (DataContext context = _fixture.CrearContexto())
            {
                Assert.True((await new SalesService(context).CancelAsync(anulada.Result!.Id,
                    new CancelSaleDTO { Reason = "Prueba" }, _fixture.CajeroId)).IsSuccess);
            }

            HomeSummaryDTO despues = await ResumenAsync(pedido);

            Assert.Equal(antes.SalesCountToday + 1, despues.SalesCountToday);
            Assert.Equal(antes.SalesTotalToday + 2000m, despues.SalesTotalToday);
        }

        [Fact]
        public async Task La_caja_propia_se_reporta_abierta_o_no()
        {
            string sinCaja = await _fixture.CrearCajeroAsync("SinCaja");

            HomeSummaryDTO conCaja = await ResumenAsync(new HomeRequestDTO { UserId = _fixture.CajeroId, WantsOwnCashSession = true });
            HomeSummaryDTO sinCajaResumen = await ResumenAsync(new HomeRequestDTO { UserId = sinCaja, WantsOwnCashSession = true });

            Assert.True(conCaja.HasOpenCashSession);
            Assert.NotNull(conCaja.CashSessionOpenedAtUtc);
            Assert.False(sinCajaResumen.HasOpenCashSession);
        }

        [Fact]
        public async Task Poco_stock_lista_los_productos_con_5_o_menos_y_no_los_que_tienen_mas()
        {
            string sufijo = Guid.NewGuid().ToString("N")[..6];
            Product agotado = await _fixture.CrearProductoAsync($"Agotado {sufijo}", 1000m, 0m);
            Product limite = await _fixture.CrearProductoAsync($"Limite {sufijo}", 1000m, 5m);
            Product sobrado = await _fixture.CrearProductoAsync($"Sobrado {sufijo}", 1000m, 6m);

            HomeSummaryDTO resumen = await ResumenAsync(new HomeRequestDTO { UserId = _fixture.CajeroId, WantsLowStock = true });

            Assert.NotNull(resumen.LowStock);
            Assert.True(resumen.LowStock!.Count <= HomeService.LowStockMaxItems);
            Assert.DoesNotContain(resumen.LowStock, p => p.Id == sobrado.Id);
            // Los más escasos van primero: si hay muchos productos con poco stock en la base, el agotado debe estar.
            Assert.All(resumen.LowStock, p => Assert.True(p.Stock <= HomeService.LowStockThreshold));
            Assert.Equal(resumen.LowStock.OrderBy(p => p.Stock).Select(p => p.Stock), resumen.LowStock.Select(p => p.Stock));
            Assert.Contains(resumen.LowStock, p => p.Id == agotado.Id || p.Stock == 0m);
        }
    }
}
