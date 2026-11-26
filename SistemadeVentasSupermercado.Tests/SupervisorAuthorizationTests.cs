using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using SistemadeVentasSupermercado.Tests.Infraestructura;
using SistemadeVentasSupermercado.Web.Core;
using SistemadeVentasSupermercado.Web.Data;
using SistemadeVentasSupermercado.Web.Data.Entities;
using SistemadeVentasSupermercado.Web.DTOs;
using SistemadeVentasSupermercado.Web.Services.Abstractions;
using SistemadeVentasSupermercado.Web.Services.Implementations;

namespace SistemadeVentasSupermercado.Tests
{
    /// <summary>Anulaciones y devoluciones autorizadas: qué se guarda, y que sea todo o nada.</summary>
    [Collection(ColeccionBaseDeDatos.Nombre)]
    public class SupervisorAuthorizationTests
    {
        private readonly BaseDeDatosFixture _fixture;

        public SupervisorAuthorizationTests(BaseDeDatosFixture fixture) => _fixture = fixture;

        /// <summary>Deja pendiente una fila que la base rechaza (0 intentos): falla al guardar.</summary>
        private sealed class RegistroQueFalla(DataContext context) : IUserActivityRecorder
        {
            public Task StageAsync(UserActivityType type, string? actorUserName, User? target = null,
                string? targetName = null, string? details = null)
            {
                context.UserActivities.Add(new UserActivity { Id = Guid.NewGuid(), Type = type, AttemptCount = 0 });
                return Task.CompletedTask;
            }

            public Task RecordAccessAsync(UserActivityType type, string userName) => Task.CompletedTask;
            public Task RecordFailedLoginAsync(UserActivityType type, User? target, string? typedEmail) => Task.CompletedTask;
            public Task RecordFailedAuthorizationAsync(User? authorizer, string requesterUserName) => Task.CompletedTask;
        }

        private static IUserActivityRecorder Registro(DataContext context)
            => new UserActivityRecorder(context, new HttpContextAccessor(), NullLogger<UserActivityRecorder>.Instance);

        // ---------- Ayudantes ----------

        private async Task<(Product producto, SaleResultDTO venta)> VenderAsync()
        {
            Product producto = await _fixture.CrearProductoAsync("Autorizado", 1000m, 10m);
            await using DataContext context = _fixture.CrearContexto();
            Response<SaleResultDTO> venta = await new SalesService(context).CreateAsync(new CreateSaleDTO
            {
                PaymentMethod = PaymentMethod.Cash,
                Items = [new SaleLineDTO { ProductId = producto.Id, Quantity = 3m }],
                IdempotencyKey = Guid.NewGuid()
            }, _fixture.CajeroId);
            Assert.True(venta.IsSuccess, venta.Message);
            return (producto, venta.Result!);
        }

        private async Task<Response<SaleDetailDTO>> AnularAsync(Guid ventaId, string? autoriza,
            Func<DataContext, IUserActivityRecorder?>? registro = null)
        {
            await using DataContext context = _fixture.CrearContexto();
            return await new SalesService(context, (registro ?? Registro)(context))
                .CancelAsync(ventaId, new CancelSaleDTO { Reason = "Error" }, _fixture.CajeroId, autoriza);
        }

        private async Task<Response<SaleReturnDetailDTO>> DevolverAsync(Guid ventaId, decimal cantidad,
            string? autoriza, Func<DataContext, IUserActivityRecorder?>? registro = null)
        {
            await using DataContext context = _fixture.CrearContexto();
            Guid linea = await context.SaleItems.Where(i => i.SaleId == ventaId).Select(i => i.Id).SingleAsync();
            return await new SaleReturnsService(context, (registro ?? Registro)(context)).CreateAsync(ventaId,
                new CreateSaleReturnDTO
                {
                    Reason = "Dañado",
                    Lines = [new SaleReturnLineDTO { SaleItemId = linea, Quantity = cantidad, Restock = true }]
                }, _fixture.CajeroId, autoriza);
        }

        private async Task<Sale> LeerVentaAsync(Guid ventaId)
        {
            await using DataContext context = _fixture.CrearContexto();
            return await context.Sales.AsNoTracking().Include(s => s.Returns).SingleAsync(s => s.Id == ventaId);
        }

        private async Task<List<UserActivity>> AutorizacionesDeAsync(string quienAutorizo)
        {
            await using DataContext context = _fixture.CrearContexto();
            return await context.UserActivities.AsNoTracking()
                .Where(a => a.Type == UserActivityType.SupervisorAuthorizationGranted && a.ActorUserId == quienAutorizo)
                .ToListAsync();
        }

        private async Task<decimal> StockAsync(Guid productoId)
        {
            await using DataContext context = _fixture.CrearContexto();
            return await context.Products.Where(p => p.Id == productoId).Select(p => p.Stock).SingleAsync();
        }

        // ---------- Anulación ----------

        [Fact]
        public async Task AnulacionAutorizada_GuardaQuienEjecuto_QuienAutorizo_YElRegistro()
        {
            string supervisor = await _fixture.CrearCajeroAsync("Supervisora");
            var (_, venta) = await VenderAsync();

            Response<SaleDetailDTO> respuesta = await AnularAsync(venta.Id, supervisor);

            Assert.True(respuesta.IsSuccess, respuesta.Message);
            Sale guardada = await LeerVentaAsync(venta.Id);
            Assert.Equal(_fixture.CajeroId, guardada.CancelledByUserId);
            Assert.Equal(supervisor, guardada.CancelAuthorizedByUserId);

            UserActivity registro = Assert.Single(await AutorizacionesDeAsync(supervisor));
            Assert.Equal(_fixture.CajeroId, registro.TargetUserId);
            Assert.Equal($"Anulación de la venta N.º {venta.Number}", registro.Details);
        }

        [Fact]
        public async Task AnulacionConPermisoPropio_NoGuardaAutorizador_NiNecesitaRegistro()
        {
            var (_, venta) = await VenderAsync();

            Response<SaleDetailDTO> respuesta = await AnularAsync(venta.Id, null, _ => null);

            Assert.True(respuesta.IsSuccess, respuesta.Message);
            Assert.Null((await LeerVentaAsync(venta.Id)).CancelAuthorizedByUserId);
        }

        [Fact]
        public async Task AnulacionRechazada_NoDejaAutorizacionEnElHistorial()
        {
            string supervisor = await _fixture.CrearCajeroAsync("Supervisora");
            var (_, venta) = await VenderAsync();
            await AnularAsync(venta.Id, null);

            Response<SaleDetailDTO> respuesta = await AnularAsync(venta.Id, supervisor);

            Assert.False(respuesta.IsSuccess);
            Assert.Empty(await AutorizacionesDeAsync(supervisor));
        }

        [Theory]
        [InlineData(true)]   // el registro falla al guardarse
        [InlineData(false)]  // no hay quien registre
        public async Task SiNoSePuedeRegistrar_LaAnulacionSeDeshace(bool conRegistroQueFalla)
        {
            string supervisor = await _fixture.CrearCajeroAsync("Supervisora");
            var (producto, venta) = await VenderAsync();

            Response<SaleDetailDTO> respuesta = await AnularAsync(venta.Id, supervisor,
                c => conRegistroQueFalla ? new RegistroQueFalla(c) : null);

            Assert.False(respuesta.IsSuccess);
            Sale guardada = await LeerVentaAsync(venta.Id);
            Assert.Equal(SaleStatus.Completed, guardada.Status);
            Assert.Null(guardada.CancelAuthorizedByUserId);
            Assert.Equal(7m, await StockAsync(producto.Id));
        }

        [Fact]
        public async Task NadieSeAutorizaSuPropiaAnulacion()
        {
            var (_, venta) = await VenderAsync();

            Response<SaleDetailDTO> respuesta = await AnularAsync(venta.Id, _fixture.CajeroId);

            Assert.False(respuesta.IsSuccess);
            Assert.Equal(SaleStatus.Completed, (await LeerVentaAsync(venta.Id)).Status);
        }

        // ---------- Dónde se ve ----------

        [Fact]
        public async Task QuienAutorizo_SaleEnComprobantesYReporte_SinSumarleOperaciones()
        {
            string supervisor = await _fixture.CrearCajeroAsync("Autorizadora");
            var (_, anulada) = await VenderAsync();
            var (_, devuelta) = await VenderAsync();
            await AnularAsync(anulada.Id, supervisor);
            Response<SaleReturnDetailDTO> devolucion = await DevolverAsync(devuelta.Id, 1m, supervisor);

            await using DataContext context = _fixture.CrearContexto();
            SaleDetailDTO comprobante = (await new SalesService(context).GetOneAsync(anulada.Id)).Result!;
            Assert.StartsWith("Autorizadora", comprobante.CancelAuthorizedByName);
            Assert.StartsWith("Autorizadora", devolucion.Result!.AuthorizedByName);

            DateOnly hoy = DateOnly.FromDateTime(DateTime.UtcNow.AddHours(-5));
            // Consulta una persona sin caja abierta: no se le oculta nada.
            CashierReportService reporte = new(context);
            CashierDetailsDTO detalle = (await reporte.GetDetailsAsync(hoy, hoy, supervisor)).Result!;
            Assert.Equal(comprobante.CancelAuthorizedByName,
                detalle.Cancellations.Single(c => c.SaleId == anulada.Id).AuthorizedByName);
            Assert.Equal(devolucion.Result.AuthorizedByName,
                detalle.Returns.Single(r => r.SaleId == devuelta.Id).AuthorizedByName);

            CashierReportDTO resumen = (await reporte.GetSummaryAsync(hoy, hoy, supervisor)).Result!;
            Assert.DoesNotContain(resumen.Rows, r => r.UserId == supervisor);
        }

        // ---------- Devolución ----------

        [Fact]
        public async Task DevolucionAutorizada_GuardaQuienEjecuto_QuienAutorizo_YElRegistro()
        {
            string supervisor = await _fixture.CrearCajeroAsync("Supervisora");
            var (_, venta) = await VenderAsync();

            Response<SaleReturnDetailDTO> respuesta = await DevolverAsync(venta.Id, 1m, supervisor);

            Assert.True(respuesta.IsSuccess, respuesta.Message);
            SaleReturn guardada = Assert.Single((await LeerVentaAsync(venta.Id)).Returns);
            Assert.Equal(_fixture.CajeroId, guardada.UserId);
            Assert.Equal(supervisor, guardada.AuthorizedByUserId);

            UserActivity registro = Assert.Single(await AutorizacionesDeAsync(supervisor));
            Assert.Equal($"Devolución N.º {guardada.Number} de la venta N.º {venta.Number}", registro.Details);
        }

        [Fact]
        public async Task DevolucionRechazada_NoDejaAutorizacionEnElHistorial()
        {
            string supervisor = await _fixture.CrearCajeroAsync("Supervisora");
            var (_, venta) = await VenderAsync();

            Response<SaleReturnDetailDTO> respuesta = await DevolverAsync(venta.Id, 4m, supervisor);

            Assert.False(respuesta.IsSuccess);
            Assert.Empty(await AutorizacionesDeAsync(supervisor));
        }

        [Fact]
        public async Task SiNoSePuedeRegistrar_LaDevolucionSeDeshace()
        {
            string supervisor = await _fixture.CrearCajeroAsync("Supervisora");
            var (producto, venta) = await VenderAsync();

            Response<SaleReturnDetailDTO> respuesta =
                await DevolverAsync(venta.Id, 1m, supervisor, c => new RegistroQueFalla(c));

            Assert.False(respuesta.IsSuccess);
            Sale guardada = await LeerVentaAsync(venta.Id);
            Assert.Empty(guardada.Returns);
            Assert.Equal(0m, guardada.ReturnedTotal);
            Assert.Equal(7m, await StockAsync(producto.Id));
        }

        [Fact]
        public async Task NadieSeAutorizaSuPropiaDevolucion()
        {
            var (_, venta) = await VenderAsync();

            Response<SaleReturnDetailDTO> respuesta = await DevolverAsync(venta.Id, 1m, _fixture.CajeroId);

            Assert.False(respuesta.IsSuccess);
            Assert.Empty((await LeerVentaAsync(venta.Id)).Returns);
        }
    }
}
