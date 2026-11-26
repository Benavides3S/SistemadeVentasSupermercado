using System.Net;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using SistemadeVentasSupermercado.Tests.Infraestructura;
using SistemadeVentasSupermercado.Web.Data;
using SistemadeVentasSupermercado.Web.Data.Entities;
using SistemadeVentasSupermercado.Web.DTOs;
using static SistemadeVentasSupermercado.Tests.Infraestructura.AyudasDeSesion;

namespace SistemadeVentasSupermercado.Tests
{
    /// <summary>Qué pasa con las sesiones abiertas cuando cambia la contraseña, con la aplicación real.</summary>
    [Collection(ColeccionAppWeb.Nombre)]
    public class PasswordSessionHttpTests
    {
        private readonly AppWebFixture _app;

        public PasswordSessionHttpTests(AppWebFixture app) => _app = app;

        [Fact]
        public async Task Restablecer_CierraDeInmediatoLaSesionAbierta()
        {
            (User cajero, CashSessionDTO turno) = await _app.CrearCajeroConCajaAsync();
            SesionManual sesion = await SesionManual.IniciarAsync(_app, cajero.UserName!);

            HttpResponseMessage puntoDeVenta = await sesion.EnviarAsync(HttpMethod.Get, "/Sales/Create");
            Assert.Equal(HttpStatusCode.OK, puntoDeVenta.StatusCode);
            string token = AppWebFixture.ExtraerToken(await puntoDeVenta.Content.ReadAsStringAsync());
            Guid productoId = await ConBaseAsync(db => db.Products
                .Where(p => p.Unit == UnitOfMeasure.Unit && p.Stock >= 5 && p.Category!.IsActive)
                .OrderByDescending(p => p.Stock).Select(p => p.Id).FirstAsync());

            // El Admin restablece la contraseña desde la pantalla.
            using HttpClient admin = await _app.CrearClienteConSesionAsync(AppWebFixture.Admin);
            string tokenAdmin = AppWebFixture.ExtraerToken(await admin.GetStringAsync("/Account/UpdateUser"));
            HttpResponseMessage restablecer = await admin.PostAsync($"/Users/ResetPassword/{cajero.Id}",
                new FormUrlEncodedContent([new("__RequestVerificationToken", tokenAdmin)]));
            Assert.Equal(HttpStatusCode.OK, restablecer.StatusCode);

            // La sesión abierta no puede cobrar ni hacer ninguna otra acción, ni siquiera cambiar la contraseña.
            AssertMandaAlLogin(await sesion.CobrarAsync(productoId, token), "POST /Sales/Create (cobro)");

            foreach ((HttpMethod metodo, string ruta) in AccionesProtegidas())
            {
                HttpContent? cuerpo = metodo == HttpMethod.Post
                    ? new FormUrlEncodedContent([new("__RequestVerificationToken", token), new("CountedAmount", "0")])
                    : null;

                AssertMandaAlLogin(await sesion.EnviarAsync(metodo, $"{ruta}/{turno.Id}", cuerpo), $"{metodo} {ruta}");
            }

            Assert.False(await ConBaseAsync(db => db.Sales.AnyAsync(s => s.UserId == cajero.Id)));
            // El Admin conserva su propia sesión.
            Assert.Equal(HttpStatusCode.OK, (await admin.GetAsync("/Users")).StatusCode);
        }

        [Fact]
        public async Task CambiarLaPropiaContrasena_ConservaEsaSesion_YCierraLasDemas()
        {
            (User cajero, _) = await _app.CrearCajeroConCajaAsync();
            using HttpClient esta = await _app.CrearClienteConSesionAsync(cajero.UserName!);
            SesionManual otra = await SesionManual.IniciarAsync(_app, cajero.UserName!);
            Assert.Equal(HttpStatusCode.OK, (await otra.EnviarAsync(HttpMethod.Get, "/Account/UpdateUser")).StatusCode);

            string nueva = $"Nueva-{Guid.NewGuid():N}aA1";
            string token = AppWebFixture.ExtraerToken(await esta.GetStringAsync("/Account/ChangePassword"));
            HttpResponseMessage cambio = await esta.PostAsync("/Account/ChangePassword", new FormUrlEncodedContent(
            [
                new("CurrentPassword", _app.Clave),
                new("NewPassword", nueva),
                new("ConfirmPassword", nueva),
                new("__RequestVerificationToken", token)
            ]));
            Assert.Equal(HttpStatusCode.Redirect, cambio.StatusCode);

            // Quien la cambió sigue dentro (y sus formularios siguen funcionando); la otra sesión, no.
            Assert.Equal(HttpStatusCode.OK, (await esta.GetAsync("/Account/UpdateUser")).StatusCode);
            Assert.Equal(HttpStatusCode.OK, (await esta.GetAsync("/Sales/Create")).StatusCode);
            AssertMandaAlLogin(await otra.EnviarAsync(HttpMethod.Get, "/Account/UpdateUser"), "la otra sesión");
        }

        private async Task<T> ConBaseAsync<T>(Func<DataContext, Task<T>> consulta)
        {
            using IServiceScope scope = _app.Services.CreateScope();
            return await consulta(scope.ServiceProvider.GetRequiredService<DataContext>());
        }
    }
}
