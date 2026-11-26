using System.Net;
using System.Reflection;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using SistemadeVentasSupermercado.Tests.Infraestructura;
using SistemadeVentasSupermercado.Web.Core.ScreenLock;
using SistemadeVentasSupermercado.Web.Data;
using SistemadeVentasSupermercado.Web.Data.Entities;
using SistemadeVentasSupermercado.Web.DTOs;
using static SistemadeVentasSupermercado.Tests.Infraestructura.AyudasDeSesion;

namespace SistemadeVentasSupermercado.Tests
{
    /// <summary>El bloqueo de pantalla con la aplicación real: lo que importa es que lo aplica el servidor.</summary>
    [Collection(ColeccionAppWeb.Nombre)]
    public class ScreenLockHttpTests
    {
        private readonly AppWebFixture _app;

        public ScreenLockHttpTests(AppWebFixture app) => _app = app;

        [Fact]
        public async Task Bloqueada_NoSePuedeHacerNada_PorNingunaRuta()
        {
            (User cajero, CashSessionDTO turno) = await _app.CrearCajeroConCajaAsync();
            (SesionManual sesion, string token, Guid productoId) = await EntrarAlPuntoDeVentaAsync(cajero);

            await BloquearAsync(sesion, token);

            Assert.Equal(HttpStatusCode.Locked, (await sesion.CobrarAsync(productoId, token)).StatusCode);

            foreach ((HttpMethod metodo, string ruta) in AccionesProtegidas().Where(a => !PermitidaBloqueada(a.Ruta)))
            {
                HttpContent? cuerpo = metodo == HttpMethod.Post
                    ? new FormUrlEncodedContent([new("__RequestVerificationToken", token), new("CountedAmount", "0")])
                    : null;

                HttpResponseMessage respuesta = await sesion.EnviarAsync(metodo, $"{ruta}/{turno.Id}", cuerpo);

                Assert.True(respuesta.StatusCode == HttpStatusCode.Locked,
                    $"{metodo} {ruta}: se esperaba 423 y respondió {(int)respuesta.StatusCode}");
            }

            Assert.False(await ConBaseAsync(db => db.Sales.AnyAsync(s => s.UserId == cajero.Id)));
            Assert.True(await ConBaseAsync(db => db.CashSessions
                .AnyAsync(c => c.Id == turno.Id && c.Status == CashSessionStatus.Open)));
        }

        [Fact]
        public async Task UnaPagina_MandaALaPantallaDeDesbloqueo_YAlDesbloquearVuelve()
        {
            using HttpClient cliente = await _app.CrearClienteConSesionAsync(AppWebFixture.Cajero);
            string token = AppWebFixture.ExtraerToken(await cliente.GetStringAsync("/Account/UpdateUser"));
            Assert.Equal(HttpStatusCode.NoContent, (await EnviarFormularioAsync(cliente, "/Account/Lock", token)).StatusCode);

            // Como la pide un navegador.
            HttpRequestMessage pagina = new(HttpMethod.Get, "/Account/UpdateUser?desde=prueba");
            pagina.Headers.Add("Accept", "text/html,application/xhtml+xml,*/*;q=0.8");
            HttpResponseMessage bloqueada = await cliente.SendAsync(pagina);

            Assert.Equal(HttpStatusCode.Redirect, bloqueada.StatusCode);
            string destino = bloqueada.Headers.Location!.ToString();
            Assert.StartsWith("/Account/Locked?returnUrl=", destino);

            string html = await cliente.GetStringAsync(destino);
            Assert.Contains("Pantalla bloqueada", html);

            HttpResponseMessage mala = await EnviarFormularioAsync(cliente, "/Account/Locked",
                AppWebFixture.ExtraerToken(html), ("password", "NoEsEsta-123"), ("returnUrl", "/Account/UpdateUser?desde=prueba"));
            Assert.Equal(HttpStatusCode.OK, mala.StatusCode);
            Assert.Contains("La contraseña no es correcta.", WebUtility.HtmlDecode(await mala.Content.ReadAsStringAsync()));

            HttpResponseMessage buena = await EnviarFormularioAsync(cliente, "/Account/Locked",
                AppWebFixture.ExtraerToken(html), ("password", _app.Clave), ("returnUrl", "/Account/UpdateUser?desde=prueba"));

            Assert.Equal("/Account/UpdateUser?desde=prueba", buena.Headers.Location!.ToString());
            Assert.Equal(HttpStatusCode.OK, (await cliente.GetAsync("/Account/UpdateUser")).StatusCode);
        }

        [Theory]
        [InlineData("https://sitio-malo.example/robar")]
        [InlineData("//sitio-malo.example")]
        public async Task Desbloquear_NuncaMandaAOtroSitio(string destinoMalo)
        {
            using HttpClient cliente = await _app.CrearClienteConSesionAsync(AppWebFixture.Cajero);
            string token = AppWebFixture.ExtraerToken(await cliente.GetStringAsync("/Account/UpdateUser"));
            await EnviarFormularioAsync(cliente, "/Account/Lock", token);

            HttpResponseMessage respuesta = await EnviarFormularioAsync(cliente, "/Account/Locked", token,
                ("password", _app.Clave), ("returnUrl", destinoMalo));

            Assert.Equal(HttpStatusCode.Redirect, respuesta.StatusCode);
            Assert.Equal("/", respuesta.Headers.Location!.ToString());
        }

        [Fact]
        public async Task Desbloquear_ConLaContrasena_ElMismoTokenPermiteCobrar()
        {
            (User cajero, _) = await _app.CrearCajeroConCajaAsync();
            (SesionManual sesion, string token, Guid productoId) = await EntrarAlPuntoDeVentaAsync(cajero);
            await BloquearAsync(sesion, token);

            (bool mal, _) = await DesbloquearAsync(sesion, token, "NoEsEsta-123");
            Assert.False(mal);
            Assert.Equal(HttpStatusCode.Locked, (await sesion.CobrarAsync(productoId, token)).StatusCode);

            (bool bien, _) = await DesbloquearAsync(sesion, token, _app.Clave);

            Assert.True(bien);
            // El carrito que quedó en pantalla se puede cobrar sin recargar la página.
            Assert.Equal(HttpStatusCode.OK, (await sesion.CobrarAsync(productoId, token)).StatusCode);
            Assert.Equal(1, await ConBaseAsync(db => db.Sales.CountAsync(s => s.UserId == cajero.Id)));
            Assert.True(await ConBaseAsync(db => db.UserActivities.AnyAsync(a =>
                a.Type == UserActivityType.UnlockFailed)));
        }

        [Fact]
        public async Task CincoContrasenasMalas_BloqueanLaCuenta_YNiLaBuenaDesbloquea()
        {
            (User cajero, _) = await _app.CrearCajeroConCajaAsync();
            (SesionManual sesion, string token, Guid productoId) = await EntrarAlPuntoDeVentaAsync(cajero);
            await BloquearAsync(sesion, token);

            for (int i = 0; i < 5; i++)
                await DesbloquearAsync(sesion, token, "NoEsEsta-123");

            (bool exito, string mensaje) = await DesbloquearAsync(sesion, token, _app.Clave);

            Assert.False(exito);
            Assert.Contains("bloqueada", mensaje);
            Assert.Equal(HttpStatusCode.Locked, (await sesion.CobrarAsync(productoId, token)).StatusCode);
            Assert.True(await ConBaseAsync(db => db.Users.AnyAsync(u => u.Id == cajero.Id && u.LockoutEnd != null)));
        }

        [Fact]
        public async Task BloquearUnaSesion_NoBloqueaOtraDelMismoUsuario()
        {
            (User cajero, _) = await _app.CrearCajeroConCajaAsync();
            (SesionManual caja, string token, _) = await EntrarAlPuntoDeVentaAsync(cajero);
            SesionManual celular = await SesionManual.IniciarAsync(_app, cajero.UserName!);

            await BloquearAsync(caja, token);

            Assert.Equal(HttpStatusCode.OK, (await celular.EnviarAsync(HttpMethod.Get, "/Account/UpdateUser")).StatusCode);
            // Y entrar de nuevo en otro lado tampoco desbloquea la que estaba bloqueada.
            Assert.Equal(HttpStatusCode.Locked, (await caja.EnviarAsync(HttpMethod.Get, "/Account/UpdateUser")).StatusCode);
        }

        [Fact]
        public async Task LasPantallasConSesion_TraenElBloqueo_YElLoginNo()
        {
            using HttpClient cliente = await _app.CrearClienteConSesionAsync(AppWebFixture.Cajero);
            using HttpClient sinSesion = _app.CrearCliente();

            string pagina = await cliente.GetStringAsync("/Account/UpdateUser");

            Assert.Contains("id=\"sessionLock\"", pagina);
            Assert.Contains("data-idle-minutes=\"5\"", pagina);
            Assert.Contains("data-lock-now", pagina);
            Assert.DoesNotContain("sessionLock", await sinSesion.GetStringAsync("/Account/Login"));
        }

        [Fact]
        public async Task BloqueadaSePuedeCerrarSesion()
        {
            using HttpClient cliente = await _app.CrearClienteConSesionAsync(AppWebFixture.Cajero);
            string token = AppWebFixture.ExtraerToken(await cliente.GetStringAsync("/Account/UpdateUser"));
            await EnviarFormularioAsync(cliente, "/Account/Lock", token);

            HttpResponseMessage salir = await cliente.GetAsync("/Account/Logout");

            Assert.Equal(HttpStatusCode.Redirect, salir.StatusCode);
            AssertMandaAlLogin(await cliente.GetAsync("/Account/UpdateUser"), "después de salir");
        }

        [Theory]
        [InlineData("/Account/Lock")]
        [InlineData("/Account/Unlock")]
        [InlineData("/Account/Activity")]
        [InlineData("/Account/Locked")]
        public async Task LasRutasDelBloqueo_ExigenTokenAntiforgery_YSesion(string ruta)
        {
            using HttpClient conSesion = await _app.CrearClienteConSesionAsync(AppWebFixture.Cajero);
            using HttpClient sinSesion = _app.CrearCliente();

            HttpResponseMessage sinToken = await conSesion.PostAsync(ruta, new FormUrlEncodedContent([]));
            Assert.Equal(HttpStatusCode.BadRequest, sinToken.StatusCode);

            AssertMandaAlLogin(await sinSesion.PostAsync(ruta, new FormUrlEncodedContent([])), ruta);
        }

        private static bool PermitidaBloqueada(string ruta)
        {
            string[] partes = ruta.Trim('/').Split('/');
            Type controlador = typeof(Program).Assembly.GetTypes().Single(t => t.Name == partes[0] + "Controller");
            return controlador.GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly)
                .Where(m => m.Name == partes[1])
                .Any(m => m.GetCustomAttribute<AllowWhenLockedAttribute>() is not null);
        }

        private async Task<(SesionManual, string Token, Guid ProductoId)> EntrarAlPuntoDeVentaAsync(User cajero)
        {
            SesionManual sesion = await SesionManual.IniciarAsync(_app, cajero.UserName!);
            HttpResponseMessage pagina = await sesion.EnviarAsync(HttpMethod.Get, "/Sales/Create");
            Assert.Equal(HttpStatusCode.OK, pagina.StatusCode);
            string token = AppWebFixture.ExtraerToken(await pagina.Content.ReadAsStringAsync());

            Guid productoId = await ConBaseAsync(db => db.Products
                .Where(p => p.Unit == UnitOfMeasure.Unit && p.Stock >= 5 && p.Category!.IsActive)
                .OrderByDescending(p => p.Stock).Select(p => p.Id).FirstAsync());

            return (sesion, token, productoId);
        }

        private static async Task BloquearAsync(SesionManual sesion, string token)
        {
            HttpResponseMessage respuesta = await sesion.EnviarAsync(HttpMethod.Post, "/Account/Lock", null, token);
            Assert.Equal(HttpStatusCode.NoContent, respuesta.StatusCode);
        }

        private static async Task<(bool Exito, string Mensaje)> DesbloquearAsync(SesionManual sesion, string token, string clave)
        {
            HttpResponseMessage respuesta = await sesion.EnviarAsync(HttpMethod.Post, "/Account/Unlock",
                new FormUrlEncodedContent([new("password", clave)]), token);
            Assert.Equal(HttpStatusCode.OK, respuesta.StatusCode);

            using JsonDocument json = JsonDocument.Parse(await respuesta.Content.ReadAsStringAsync());
            return (json.RootElement.GetProperty("isSuccess").GetBoolean(),
                    json.RootElement.GetProperty("message").GetString()!);
        }

        private static Task<HttpResponseMessage> EnviarFormularioAsync(HttpClient cliente, string ruta, string token,
            params (string Nombre, string Valor)[] campos)
            => cliente.PostAsync(ruta, new FormUrlEncodedContent(campos
                .Select(c => new KeyValuePair<string, string>(c.Nombre, c.Valor))
                .Append(new("__RequestVerificationToken", token))));

        private async Task<T> ConBaseAsync<T>(Func<DataContext, Task<T>> consulta)
        {
            using IServiceScope scope = _app.Services.CreateScope();
            return await consulta(scope.ServiceProvider.GetRequiredService<DataContext>());
        }
    }
}
