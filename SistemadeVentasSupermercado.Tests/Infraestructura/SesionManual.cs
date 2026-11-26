using System.Net;
using System.Net.Http.Json;
using System.Reflection;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using SistemadeVentasSupermercado.Web.Data;
using SistemadeVentasSupermercado.Web.Data.Entities;
using SistemadeVentasSupermercado.Web.Data.Seeders;
using SistemadeVentasSupermercado.Web.DTOs;
using SistemadeVentasSupermercado.Web.Services.Implementations;

namespace SistemadeVentasSupermercado.Tests.Infraestructura
{
    /// <summary>Herramientas para probar qué puede hacer una sesión que ya estaba abierta.</summary>
    public static class AyudasDeSesion
    {
        public static void AssertMandaAlLogin(HttpResponseMessage respuesta, string accion)
        {
            string destino = respuesta.Headers.Location?.ToString() ?? string.Empty;
            Assert.True(respuesta.StatusCode == HttpStatusCode.Redirect && destino.Contains("/Account/Login"),
                $"{accion}: se esperaba ir al login y respondió {(int)respuesta.StatusCode} {destino}");
        }

        /// <summary>Todas las acciones que no son [AllowAnonymous], con su método HTTP.</summary>
        public static IEnumerable<(HttpMethod Metodo, string Ruta)> AccionesProtegidas() =>
            typeof(Program).Assembly.GetTypes()
                .Where(t => typeof(ControllerBase).IsAssignableFrom(t) && !t.IsAbstract)
                .SelectMany(t => t.GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly)
                    .Where(m => !m.IsSpecialName
                             && m.GetCustomAttribute<NonActionAttribute>() is null
                             && m.GetCustomAttribute<AllowAnonymousAttribute>() is null)
                    .Select(m => (
                        m.GetCustomAttribute<HttpPostAttribute>() is null ? HttpMethod.Get : HttpMethod.Post,
                        $"/{t.Name.Replace("Controller", "")}/{m.Name}")))
                .ToList();

        /// <summary>Un cajero propio de la prueba (con la clave del fixture) y su caja abierta.</summary>
        public static async Task<(User, CashSessionDTO)> CrearCajeroConCajaAsync(this AppWebFixture app)
        {
            using IServiceScope scope = app.Services.CreateScope();
            DataContext db = scope.ServiceProvider.GetRequiredService<DataContext>();
            string marca = Guid.NewGuid().ToString("N")[..8];

            User cajero = new()
            {
                UserName = $"propio-{marca}@pruebas.local",
                Email = $"propio-{marca}@pruebas.local",
                EmailConfirmed = true,
                Document = $"PROPIO-{marca}",
                FirstName = "Cajero",
                LastName = marca,
                SistemaVentasRoleId = await db.SistemaVentasRoles
                    .Where(r => r.Name == UserRolesSeeder.CASHIER_ROLE_NAME).Select(r => r.Id).SingleAsync()
            };

            IdentityResult resultado = await scope.ServiceProvider.GetRequiredService<UserManager<User>>()
                .CreateAsync(cajero, app.Clave);
            Assert.True(resultado.Succeeded);

            CashSessionDTO turno = (await new CashSessionService(db)
                .OpenAsync(cajero.Id, new OpenCashSessionDTO { OpeningAmount = 0m })).Result!;

            return (cajero, turno);
        }
    }

    /// <summary>
    /// Una sesión que reenvía SIEMPRE las mismas cookies, aunque el servidor pida borrarlas
    /// (como alguien que las guardó). Así cada petición prueba la sesión que ya estaba abierta.
    /// </summary>
    public sealed class SesionManual
    {
        private readonly HttpClient _cliente;
        private readonly Dictionary<string, string> _cookies = [];

        private SesionManual(HttpClient cliente) => _cliente = cliente;

        public static async Task<SesionManual> IniciarAsync(AppWebFixture app, string correo, string? clave = null)
        {
            SesionManual sesion = new(app.CreateClient(new WebApplicationFactoryClientOptions
            {
                AllowAutoRedirect = false,
                HandleCookies = false
            }));

            HttpResponseMessage formulario = await sesion.EnviarAsync(HttpMethod.Get, "/Account/Login");
            sesion.Guardar(formulario);
            string token = AppWebFixture.ExtraerToken(await formulario.Content.ReadAsStringAsync());

            HttpResponseMessage login = await sesion.EnviarAsync(HttpMethod.Post, "/Account/Login", new FormUrlEncodedContent(
            [
                new("Email", correo),
                new("Password", clave ?? app.Clave),
                new("__RequestVerificationToken", token)
            ]));
            Assert.Equal(HttpStatusCode.Redirect, login.StatusCode);
            sesion.Guardar(login);
            Assert.Contains("Auth", sesion._cookies.Keys);

            return sesion;
        }

        private void Guardar(HttpResponseMessage respuesta)
        {
            if (!respuesta.Headers.TryGetValues("Set-Cookie", out IEnumerable<string>? valores)) return;

            foreach (string valor in valores)
            {
                string[] partes = valor.Split(';')[0].Split('=', 2);
                _cookies[partes[0]] = partes[1];
            }
        }

        public Task<HttpResponseMessage> EnviarAsync(HttpMethod metodo, string url, HttpContent? cuerpo = null,
            string? token = null)
        {
            HttpRequestMessage peticion = new(metodo, url) { Content = cuerpo };
            if (_cookies.Count > 0)
                peticion.Headers.Add("Cookie", string.Join("; ", _cookies.Select(c => $"{c.Key}={c.Value}")));
            if (token is not null)
                peticion.Headers.Add("RequestVerificationToken", token);

            return _cliente.SendAsync(peticion);
        }

        /// <summary>El mismo cobro que hace pos.js: JSON y token antiforgery en el encabezado.</summary>
        public Task<HttpResponseMessage> CobrarAsync(Guid productoId, string token)
            => EnviarAsync(HttpMethod.Post, "/Sales/Create", JsonContent.Create(new
            {
                paymentMethod = (int)PaymentMethod.Cash,
                idempotencyKey = Guid.NewGuid(),
                items = new[] { new { productId = productoId, quantity = 1 } }
            }), token);
    }
}
