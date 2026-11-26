using System.Net;
using ClosedXML.Excel;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using SistemadeVentasSupermercado.Tests.Infraestructura;
using SistemadeVentasSupermercado.Web.Data;
using SistemadeVentasSupermercado.Web.Data.Entities;
using SistemadeVentasSupermercado.Web.Helpers;

namespace SistemadeVentasSupermercado.Tests
{
    /// <summary>Reporte por cajero con la aplicación real: permisos, pantallas y Excel.</summary>
    [Collection(ColeccionAppWeb.Nombre)]
    public class CashierReportHttpTests
    {
        private readonly AppWebFixture _app;

        public CashierReportHttpTests(AppWebFixture app) => _app = app;

        private async Task<T> ConBaseAsync<T>(Func<DataContext, Task<T>> consulta)
        {
            using IServiceScope scope = _app.Services.CreateScope();
            return await consulta(scope.ServiceProvider.GetRequiredService<DataContext>());
        }

        /// <summary>Un cajero con una anulación (con un motivo que parece fórmula) y un faltante, en 2018.</summary>
        private async Task<User> CajeroConHistoriaAsync()
        {
            (User cajero, _) = await _app.CrearCajeroConCajaAsync();
            await ConBaseAsync(async db =>
            {
                DateTime fecha = new(2018, 9, 10, 15, 0, 0, DateTimeKind.Utc);
                db.Sales.Add(new Sale
                {
                    Id = Guid.NewGuid(), UserId = cajero.Id, CreatedAtUtc = fecha, Total = 4321m,
                    PaymentMethod = PaymentMethod.Cash, Status = SaleStatus.Cancelled, CancelledAtUtc = fecha,
                    CancelledByUserId = cajero.Id, CancelReason = "=1+1 <script>alert(1)</script>"
                });
                db.CashSessions.Add(new CashSession
                {
                    Id = Guid.NewGuid(), UserId = cajero.Id, OpenedAtUtc = fecha.AddHours(-3), ClosedAtUtc = fecha,
                    Status = CashSessionStatus.Closed, ClosedByUserId = cajero.Id, ExpectedAmount = 10_000m,
                    CountedAmount = 7_500m, Difference = -2_500m
                });
                return await db.SaveChangesAsync();
            });
            return cajero;
        }

        private const string Fechas = "From=2018-09-01&To=2018-09-30";

        [Theory]
        [InlineData(AppWebFixture.Admin, true)]
        [InlineData(AppWebFixture.Cajero, false)]
        [InlineData(AppWebFixture.Supervisor, false)] // tiene showReports, pero este permiso es aparte
        [InlineData(AppWebFixture.SinPermisos, false)]
        public async Task AlPrincipioSoloEntraElAdmin(string correo, bool puede)
        {
            using HttpClient cliente = await _app.CrearClienteConSesionAsync(correo);

            foreach (string url in new[] { "/CashierReports", $"/CashierReports/Detail/{Guid.NewGuid()}", "/CashierReports/Excel" })
            {
                HttpResponseMessage respuesta = await cliente.GetAsync(url);
                bool negado = respuesta.Headers.Location?.ToString().Contains("/Error/403") == true;
                Assert.True(negado != puede, $"{url} para {correo}: {(int)respuesta.StatusCode}");
            }

            string inicio = await cliente.GetStringAsync("/");
            Assert.Equal(puede, inicio.Contains("href=\"/CashierReports\""));
        }

        [Fact]
        public async Task Pantallas_MuestranElResumenYElDetalle_SinEjecutarElTextoEscrito()
        {
            User cajero = await CajeroConHistoriaAsync();
            using HttpClient admin = await _app.CrearClienteConSesionAsync(AppWebFixture.Admin);

            string resumen = WebUtility.HtmlDecode(await admin.GetStringAsync($"/CashierReports?{Fechas}"));
            Assert.Contains($"/CashierReports/Detail/{cajero.Id}", resumen);
            Assert.Contains("From=2018-09-01", resumen);
            Assert.Contains("Anulaciones que ejecutó", resumen);

            string crudo = await admin.GetStringAsync($"/CashierReports/Detail/{cajero.Id}?{Fechas}");
            Assert.DoesNotContain("<script>alert(1)</script>", crudo);
            string detalle = WebUtility.HtmlDecode(crudo);
            Assert.Contains("=1+1", detalle);
            Assert.Contains("Faltan $", detalle);
            Assert.Contains("Abierto ahora", detalle);
            Assert.Contains($"cashierId={cajero.Id}", detalle);

            // Fechas al revés: la pantalla abre y avisa, sin error del servidor.
            Assert.Equal(HttpStatusCode.OK,
                (await admin.GetAsync("/CashierReports?From=2018-09-30&To=2018-09-01")).StatusCode);
        }

        [Fact]
        public async Task Excel_TraeCuatroHojas_ConNumeros_YElTextoNuncaComoFormula()
        {
            User cajero = await CajeroConHistoriaAsync();
            using HttpClient admin = await _app.CrearClienteConSesionAsync(AppWebFixture.Admin);

            foreach (string url in new[]
                     { $"/CashierReports/Excel?{Fechas}", $"/CashierReports/Excel?cashierId={cajero.Id}&{Fechas}" })
            {
                HttpResponseMessage respuesta = await admin.GetAsync(url);
                Assert.Equal(HttpStatusCode.OK, respuesta.StatusCode);
                Assert.True(respuesta.Headers.CacheControl?.NoStore);

                using XLWorkbook libro = new(new MemoryStream(await respuesta.Content.ReadAsByteArrayAsync()));
                Assert.Equal(["Resumen", "Anulaciones", "Devoluciones", "Turnos"], libro.Worksheets.Select(w => w.Name));

                // La otra prueba crea un cajero igual en las mismas fechas: se busca la fila del propio.
                IXLRow anulacion = libro.Worksheet("Anulaciones").RowsUsed().Single(r => r.Cell(5).GetString() == cajero.FullName);
                Assert.StartsWith("=1+1", anulacion.Cell(7).GetString());
                Assert.False(anulacion.Cell(7).HasFormula);
                Assert.Equal(XLDataType.Number, anulacion.Cell(4).DataType);
                Assert.Equal(XLDataType.DateTime, anulacion.Cell(3).DataType);
                Assert.Equal(new DateTime(2018, 9, 10, 10, 0, 0), anulacion.Cell(3).GetDateTime()); // hora de Colombia
            }
        }

        [Fact]
        public async Task ConSuCajaAbierta_QuienConsultaNoVeSusCifras_NiEnPantallaNiEnExcel()
        {
            string adminId = await ConBaseAsync(db => db.Users.Where(u => u.UserName == AppWebFixture.Admin)
                .Select(u => u.Id).SingleAsync());
            Guid turno = Guid.NewGuid();
            await ConBaseAsync(async db =>
            {
                db.Sales.Add(new Sale
                {
                    Id = Guid.NewGuid(), UserId = adminId, Total = 987_654m, PaymentMethod = PaymentMethod.Cash,
                    CreatedAtUtc = new DateTime(2018, 10, 5, 15, 0, 0, DateTimeKind.Utc)
                });
                db.CashSessions.Add(new CashSession { Id = turno, UserId = adminId, Status = CashSessionStatus.Open });
                return await db.SaveChangesAsync();
            });

            const string octubre = "From=2018-10-01&To=2018-10-31";
            try
            {
                using HttpClient admin = await _app.CrearClienteConSesionAsync(AppWebFixture.Admin);

                string resumen = WebUtility.HtmlDecode(await admin.GetStringAsync($"/CashierReports?{octubre}"));
                Assert.Contains("los totales son parciales", resumen);
                Assert.DoesNotContain("987.654", resumen);
                Assert.DoesNotContain($"/CashierReports/Detail/{adminId}", resumen);

                // Pidiendo su propio detalle por la dirección: lo devuelve al resumen.
                foreach (string url in new[]
                         { $"/CashierReports/Detail/{adminId}?{octubre}", $"/CashierReports/Excel?cashierId={adminId}&{octubre}" })
                {
                    HttpResponseMessage respuesta = await admin.GetAsync(url);
                    Assert.Equal(HttpStatusCode.Redirect, respuesta.StatusCode);
                    Assert.StartsWith("/CashierReports", respuesta.Headers.Location!.ToString());
                }

                byte[] archivo = await admin.GetByteArrayAsync($"/CashierReports/Excel?{octubre}");
                using XLWorkbook libro = new(new MemoryStream(archivo));
                IXLWorksheet hoja = libro.Worksheet("Resumen");
                Assert.Equal(CashierReportExcelBuilder.PartialNotice, hoja.Cell(3, 1).GetString());
                Assert.DoesNotContain(hoja.CellsUsed(), c => c.DataType == XLDataType.Number && c.GetValue<decimal>() == 987_654m);
            }
            finally
            {
                // Las demás pruebas esperan al Admin sin caja abierta.
                await ConBaseAsync(db => db.CashSessions.Where(c => c.Id == turno).ExecuteDeleteAsync());
            }
        }
    }
}
