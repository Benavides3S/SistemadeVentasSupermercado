using System.Net;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using SistemadeVentasSupermercado.Tests.Infraestructura;
using static SistemadeVentasSupermercado.Tests.Infraestructura.AyudasDeSesion;
using SistemadeVentasSupermercado.Web.Core;
using SistemadeVentasSupermercado.Web.Data;
using SistemadeVentasSupermercado.Web.Data.Entities;
using SistemadeVentasSupermercado.Web.Data.Seeders;
using SistemadeVentasSupermercado.Web.DTOs;

namespace SistemadeVentasSupermercado.Tests
{
    /// <summary>Desactivar usuarios con la aplicación real.</summary>
    [Collection(ColeccionAppWeb.Nombre)]
    public class UserDeactivationHttpTests
    {
        private readonly AppWebFixture _app;

        public UserDeactivationHttpTests(AppWebFixture app) => _app = app;

        [Fact]
        public async Task SesionAbierta_TrasDesactivar_NoPuedeCobrarNiHacerNingunaOtraAccion()
        {
            // Un cajero propio, con la caja abierta y la sesión iniciada.
            (User cajero, CashSessionDTO turno) = await _app.CrearCajeroConCajaAsync();
            Guid productoId = await ProductoVendibleAsync();

            SesionManual sesion = await SesionManual.IniciarAsync(_app, cajero.UserName!);

            HttpResponseMessage puntoDeVenta = await sesion.EnviarAsync(HttpMethod.Get, "/Sales/Create");
            Assert.Equal(HttpStatusCode.OK, puntoDeVenta.StatusCode);
            string token = AppWebFixture.ExtraerToken(await puntoDeVenta.Content.ReadAsStringAsync());

            // Antes de desactivarlo, esta misma sesión SÍ puede cobrar (si no, la prueba no demostraría nada).
            HttpResponseMessage antes = await sesion.CobrarAsync(productoId, token);
            Assert.Equal(HttpStatusCode.OK, antes.StatusCode);
            using (JsonDocument json = JsonDocument.Parse(await antes.Content.ReadAsStringAsync()))
                Assert.True(json.RootElement.GetProperty("isSuccess").GetBoolean());

            // El Admin lo desactiva desde la pantalla y ve el aviso de la caja abierta.
            using HttpClient admin = await _app.CrearClienteConSesionAsync(AppWebFixture.Admin);
            string avisos = await DesactivarDesdeLaPantallaAsync(admin, cajero.Id);

            Assert.Contains($"tiene abierta la caja n.º {turno.Number} desde el {DisplayFormat.DateTimeColombia(turno.OpenedAtUtc)}", avisos);
            Assert.Contains("Alguien con permiso para ver los turnos de caja debe cerrarla", avisos);

            // En la lista de turnos, el turno aparece marcado.
            string turnos = WebUtility.HtmlDecode(await admin.GetStringAsync($"/CashSessions?Filter={turno.Number}"));
            Assert.Contains("Usuario desactivado: falta cerrarla", turnos);

            (int ventas, decimal stock) = await VentasYStockAsync(cajero.Id, productoId);

            // La misma sesión ya no puede cobrar.
            HttpResponseMessage despues = await sesion.CobrarAsync(productoId, token);
            AssertMandaAlLogin(despues, "POST /Sales/Create (cobro)");

            // Ni ninguna otra acción: todas las de todos los controladores que exigen sesión, con la MISMA cookie.
            List<string> rechazadas = [];
            foreach ((HttpMethod metodo, string ruta) in AccionesProtegidas())
            {
                string url = $"{ruta}/{turno.Id}?saleId={Guid.NewGuid()}&productId={productoId}";
                HttpContent? cuerpo = metodo == HttpMethod.Post
                    ? new FormUrlEncodedContent([new("__RequestVerificationToken", token), new("CountedAmount", "0")])
                    : null;

                AssertMandaAlLogin(await sesion.EnviarAsync(metodo, url, cuerpo), $"{metodo} {ruta}");
                rechazadas.Add(ruta);
            }

            // Si la lista saliera vacía o recortada, el recorrido no valdría.
            Assert.True(rechazadas.Count > 40, $"Solo se probaron {rechazadas.Count} acciones.");
            Assert.Contains("/Account/UpdateUser", rechazadas);
            Assert.Contains("/CashSessions/Close", rechazadas);

            // Nada cambió: ni ventas, ni stock, ni el turno.
            Assert.Equal((ventas, stock), await VentasYStockAsync(cajero.Id, productoId));
            Assert.Equal(CashSessionStatus.Open, await ConBaseAsync(db =>
                db.CashSessions.Where(c => c.Id == turno.Id).Select(c => c.Status).SingleAsync()));

            // Tampoco puede volver a entrar, aunque la contraseña sea correcta.
            await Assert.ThrowsAsync<InvalidOperationException>(() => _app.CrearClienteConSesionAsync(cajero.UserName!));
        }

        [Theory]
        [InlineData(AppWebFixture.Cajero)]
        [InlineData(AppWebFixture.Supervisor)]
        [InlineData(AppWebFixture.SinPermisos)]
        public async Task Desactivar_Reactivar_Desbloquear_SinPermiso_SeRechaza(string correo)
        {
            using HttpClient cliente = await _app.CrearClienteConSesionAsync(correo);
            string token = AppWebFixture.ExtraerToken(await cliente.GetStringAsync("/Account/UpdateUser"));

            foreach (string accion in new[] { "Deactivate", "Reactivate", "Unlock" })
            {
                HttpResponseMessage respuesta = await cliente.PostAsync($"/Users/{accion}/{Guid.NewGuid()}",
                    new FormUrlEncodedContent([new("__RequestVerificationToken", token)]));

                Assert.Equal(HttpStatusCode.Redirect, respuesta.StatusCode);
                Assert.Contains("/Error/403", respuesta.Headers.Location?.ToString());
            }
        }

        [Fact]
        public async Task ListaDeUsuarios_MuestraRolYEstado_YFiltra()
        {
            (User cajero, _) = await _app.CrearCajeroConCajaAsync();
            using HttpClient admin = await _app.CrearClienteConSesionAsync(AppWebFixture.Admin);

            string activos = WebUtility.HtmlDecode(await admin.GetStringAsync($"/Users?Filter={cajero.Email}&Status=Active"));
            Assert.Contains(cajero.Email!, activos);
            Assert.Contains(UserRolesSeeder.CASHIER_ROLE_NAME + "</td>", activos);
            Assert.Contains($"/Users/Deactivate/{cajero.Id}", activos);

            await DesactivarDesdeLaPantallaAsync(admin, cajero.Id);

            string desactivados = WebUtility.HtmlDecode(await admin.GetStringAsync($"/Users?Filter={cajero.Email}&Status=Deactivated"));
            Assert.Contains($"/Users/Reactivate/{cajero.Id}", desactivados);
            Assert.DoesNotContain($"/Users/Deactivate/{cajero.Id}", desactivados);
            Assert.DoesNotContain($"/Users/ResetPassword/{cajero.Id}", desactivados);
            // La paginación conserva el filtro.
            Assert.Contains("Status=Deactivated", desactivados);

            string yaNoEstaEntreLosActivos = await admin.GetStringAsync($"/Users?Filter={cajero.Email}&Status=Active");
            Assert.DoesNotContain($"/Users/Reactivate/{cajero.Id}", yaNoEstaEntreLosActivos);
            Assert.DoesNotContain($"/Users/Edit/{cajero.Id}", yaNoEstaEntreLosActivos);
        }

        [Fact]
        public async Task ReactivarYDesbloquear_DesdeLaPantalla_PermitenVolverAEntrar()
        {
            (User cajero, _) = await _app.CrearCajeroConCajaAsync();
            SesionManual sesionVieja = await SesionManual.IniciarAsync(_app, cajero.UserName!);
            using HttpClient admin = await _app.CrearClienteConSesionAsync(AppWebFixture.Admin);
            await DesactivarDesdeLaPantallaAsync(admin, cajero.Id);

            await PulsarAsync(admin, "Reactivate", cajero.Id);
            // La sesión de antes de desactivarlo no revive: tiene que volver a iniciar sesión.
            AssertMandaAlLogin(await sesionVieja.EnviarAsync(HttpMethod.Get, "/Account/UpdateUser"),
                "sesión anterior a la desactivación");
            using (await _app.CrearClienteConSesionAsync(cajero.UserName!)) { }

            await ConBaseAsync(db => db.Users.Where(u => u.Id == cajero.Id).ExecuteUpdateAsync(s => s
                .SetProperty(u => u.LockoutEnd, DateTimeOffset.UtcNow.AddMinutes(10))));
            string bloqueados = await admin.GetStringAsync($"/Users?Filter={cajero.Email}&Status=Locked");
            Assert.Contains($"/Users/Unlock/{cajero.Id}", bloqueados);
            await Assert.ThrowsAsync<InvalidOperationException>(() => _app.CrearClienteConSesionAsync(cajero.UserName!));

            await PulsarAsync(admin, "Unlock", cajero.Id);
            using (await _app.CrearClienteConSesionAsync(cajero.UserName!)) { }

            // Sin filtro de estado, los enlaces de las páginas no lo agregan.
            Assert.DoesNotContain("Status=", await admin.GetStringAsync("/Users"));
        }

        /// <summary>
        /// La revisión de "usuario activo" funciona por sí sola: aquí se marca en la base SIN cambiar el
        /// sello de seguridad, así que solo ella puede cerrar la sesión.
        /// </summary>
        [Fact]
        public async Task UsuarioMarcadoDesactivado_SinCambiarElSello_PierdeLaSesion()
        {
            (User cajero, _) = await _app.CrearCajeroConCajaAsync();
            SesionManual sesion = await SesionManual.IniciarAsync(_app, cajero.UserName!);

            await ConBaseAsync(db => db.Users.Where(u => u.Id == cajero.Id)
                .ExecuteUpdateAsync(s => s.SetProperty(u => u.DeactivatedAt, DateTime.UtcNow)));

            AssertMandaAlLogin(await sesion.EnviarAsync(HttpMethod.Get, "/Account/UpdateUser"), "usuario desactivado");
        }

        private static async Task PulsarAsync(HttpClient admin, string accion, string userId)
        {
            string token = AppWebFixture.ExtraerToken(await admin.GetStringAsync("/Account/UpdateUser"));
            HttpResponseMessage respuesta = await admin.PostAsync($"/Users/{accion}/{userId}",
                new FormUrlEncodedContent([new("__RequestVerificationToken", token)]));
            Assert.Equal(HttpStatusCode.Redirect, respuesta.StatusCode);
        }

        // ---------- Ayudantes ----------

        private static async Task<string> DesactivarDesdeLaPantallaAsync(HttpClient admin, string userId)
        {
            string token = AppWebFixture.ExtraerToken(await admin.GetStringAsync("/Account/UpdateUser"));
            HttpResponseMessage respuesta = await admin.PostAsync($"/Users/Deactivate/{userId}",
                new FormUrlEncodedContent([new("__RequestVerificationToken", token)]));

            Assert.Equal(HttpStatusCode.Redirect, respuesta.StatusCode);
            return WebUtility.HtmlDecode(await admin.GetStringAsync(respuesta.Headers.Location));
        }

        private async Task<T> ConBaseAsync<T>(Func<DataContext, Task<T>> consulta)
        {
            using IServiceScope scope = _app.Services.CreateScope();
            return await consulta(scope.ServiceProvider.GetRequiredService<DataContext>());
        }

        private Task<Guid> ProductoVendibleAsync() => ConBaseAsync(db => db.Products
            .Where(p => p.Unit == UnitOfMeasure.Unit && p.Stock >= 5 && p.Category!.IsActive)
            .OrderByDescending(p => p.Stock)
            .Select(p => p.Id).FirstAsync());

        private Task<(int, decimal)> VentasYStockAsync(string userId, Guid productoId) => ConBaseAsync(async db => (
            await db.Sales.CountAsync(s => s.UserId == userId),
            await db.Products.Where(p => p.Id == productoId).Select(p => p.Stock).SingleAsync()));
    }
}
