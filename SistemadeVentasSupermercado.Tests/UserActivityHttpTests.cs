using System.Net;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Newtonsoft.Json;
using SistemadeVentasSupermercado.Tests.Infraestructura;
using SistemadeVentasSupermercado.Web.Data;
using SistemadeVentasSupermercado.Web.Data.Entities;
using SistemadeVentasSupermercado.Web.DTOs;
using SistemadeVentasSupermercado.Web.Services.Implementations;

namespace SistemadeVentasSupermercado.Tests
{
    /// <summary>Historial de actividad con la aplicación real: accesos desde el formulario y la pantalla.</summary>
    [Collection(ColeccionAppWeb.Nombre)]
    public class UserActivityHttpTests
    {
        private readonly AppWebFixture _app;

        public UserActivityHttpTests(AppWebFixture app) => _app = app;

        /// <summary>Envía el formulario de ingreso y devuelve el código de respuesta (302 = entró).</summary>
        private static async Task<HttpStatusCode> IntentarEntrarAsync(HttpClient cliente, string correo, string clave)
        {
            string token = AppWebFixture.ExtraerToken(await cliente.GetStringAsync("/Account/Login"));
            HttpResponseMessage respuesta = await cliente.PostAsync("/Account/Login", new FormUrlEncodedContent(
                [new("Email", correo), new("Password", clave), new("__RequestVerificationToken", token)]));
            return respuesta.StatusCode;
        }

        private async Task<T> ConBaseAsync<T>(Func<DataContext, Task<T>> consulta)
        {
            using IServiceScope scope = _app.Services.CreateScope();
            return await consulta(scope.ServiceProvider.GetRequiredService<DataContext>());
        }

        private Task<List<UserActivity>> FilasAsync(int despuesDe) => ConBaseAsync(db => db.UserActivities.AsNoTracking()
            .Where(a => a.Number > despuesDe).OrderBy(a => a.Number).ToListAsync());

        private Task<int> UltimoNumeroAsync() => ConBaseAsync(async db => await db.UserActivities.MaxAsync(a => (int?)a.Number) ?? 0);

        // Los fallos se suman en vez de contar filas: si la prueba cruza de minuto, quedan en dos.
        private static int Intentos(IEnumerable<UserActivity> filas, UserActivityType tipo)
            => filas.Where(a => a.Type == tipo).Sum(a => a.AttemptCount);

        [Fact]
        public async Task Accesos_SeRegistranDesdeElFormulario_SinGuardarLoQuePuedeSerUnaContrasena()
        {
            (User cajero, _) = await _app.CrearCajeroConCajaAsync();
            int inicio = await UltimoNumeroAsync();
            using HttpClient cliente = _app.CrearCliente();

            const string claveMala = "ClaveEquivocada99";
            const string claveEnElCorreo = "SuperSecreta123";
            const string claveEnLosDos = "Secreta@clave.abc";
            string inexistente = $"intruso-{Guid.NewGuid():N}@correo.co";

            await IntentarEntrarAsync(cliente, inexistente, claveMala);
            await IntentarEntrarAsync(cliente, claveEnElCorreo, claveMala);   // el formulario lo rechaza: no parece correo
            await IntentarEntrarAsync(cliente, claveEnLosDos, claveEnLosDos); // con forma de correo, pero es la contraseña

            // Cuatro fallos y el quinto bloquea; después ni la contraseña correcta entra.
            for (int i = 0; i < 5; i++)
                Assert.Equal(HttpStatusCode.OK, await IntentarEntrarAsync(cliente, cajero.Email!, claveMala));
            Assert.Equal(HttpStatusCode.OK, await IntentarEntrarAsync(cliente, cajero.Email!, _app.Clave));

            List<UserActivity> filas = await FilasAsync(inicio);
            List<UserActivity> delCajero = filas.Where(a => a.TargetUserId == cajero.Id).ToList();
            Assert.Equal(4, Intentos(delCajero, UserActivityType.LoginFailed));
            Assert.Equal(2, Intentos(delCajero, UserActivityType.AccountLocked));
            Assert.All(delCajero, a => Assert.Null(a.ActorUserId));
            Assert.Equal(1, Intentos(filas.Where(a => a.TargetName == inexistente), UserActivityType.LoginFailed));
            Assert.Equal(2, Intentos(filas.Where(a => a.TargetName == UserActivityRecorder.InvalidUserText),
                UserActivityType.LoginFailed));

            // Entrar y salir: uno por cada vez, sin agrupar.
            await ConBaseAsync(db => db.Users.Where(u => u.Id == cajero.Id)
                .ExecuteUpdateAsync(s => s.SetProperty(u => u.LockoutEnd, (DateTimeOffset?)null)));
            int antesDeEntrar = await UltimoNumeroAsync();
            for (int i = 0; i < 2; i++)
            {
                using HttpClient sesion = await _app.CrearClienteConSesionAsync(cajero.UserName!);
                await sesion.GetAsync("/Account/Logout");
            }

            List<UserActivity> accesos = await FilasAsync(antesDeEntrar);
            Assert.Equal(
                [UserActivityType.LoginSucceeded, UserActivityType.Logout, UserActivityType.LoginSucceeded, UserActivityType.Logout],
                accesos.Select(a => a.Type));
            Assert.All(accesos, a =>
            {
                Assert.Equal(cajero.Id, a.ActorUserId);
                Assert.Equal(1, a.AttemptCount);
                Assert.Null(a.GroupKey);
            });

            // Un usuario desactivado que intenta entrar.
            await ConBaseAsync(db => db.Users.Where(u => u.Id == cajero.Id)
                .ExecuteUpdateAsync(s => s.SetProperty(u => u.DeactivatedAt, DateTime.UtcNow)));
            await IntentarEntrarAsync(cliente, cajero.Email!, _app.Clave);

            List<UserActivity> todas = await FilasAsync(inicio);
            Assert.Equal(1, Intentos(todas.Where(a => a.TargetUserId == cajero.Id), UserActivityType.DeactivatedLoginRejected));

            // Ninguna contraseña quedó en ninguna columna.
            string texto = JsonConvert.SerializeObject(todas.Select(a =>
                new { a.ActorName, a.TargetName, a.Details, a.GroupKey, a.IpAddress }));
            foreach (string clave in new[] { claveMala, claveEnElCorreo, claveEnLosDos, _app.Clave })
                Assert.DoesNotContain(clave, texto, StringComparison.OrdinalIgnoreCase);
        }

        [Theory]
        [InlineData(AppWebFixture.Admin, true)]
        [InlineData(AppWebFixture.Cajero, false)]
        [InlineData(AppWebFixture.Supervisor, false)]
        [InlineData(AppWebFixture.SinPermisos, false)]
        public async Task Pantalla_AlPrincipioSoloLaVeElAdmin(string correo, bool puede)
        {
            using HttpClient cliente = await _app.CrearClienteConSesionAsync(correo);

            HttpResponseMessage respuesta = await cliente.GetAsync("/UserActivity");

            if (puede)
            {
                Assert.Equal(HttpStatusCode.OK, respuesta.StatusCode);
            }
            else
            {
                Assert.Equal(HttpStatusCode.Redirect, respuesta.StatusCode);
                Assert.Contains("/Error/403", respuesta.Headers.Location?.ToString());
            }
        }

        [Fact]
        public async Task Pantalla_MuestraEventosYContador_YLaPaginacionConservaLosFiltros()
        {
            string inexistente = $"repetido-{Guid.NewGuid():N}@correo.co";
            await ConBaseAsync(async db =>
            {
                for (int i = 0; i < 12; i++)
                    db.UserActivities.Add(new UserActivity
                    {
                        Id = Guid.NewGuid(), Type = UserActivityType.LoginFailed, TargetName = inexistente,
                        AttemptCount = 7, LastAttemptAtUtc = DateTime.UtcNow
                    });
                return await db.SaveChangesAsync();
            });
            using HttpClient admin = await _app.CrearClienteConSesionAsync(AppWebFixture.Admin);

            string html = WebUtility.HtmlDecode(await admin.GetStringAsync(
                $"/UserActivity?Filter={inexistente}&Type={(int)UserActivityType.LoginFailed}&RecordsPerPage=10&From=2020-01-01"));

            Assert.Contains("Intento fallido de ingreso", html);
            Assert.Contains("7 intentos; el último:", html);
            Assert.Contains("12 registro(s)", html);
            Assert.Contains("Page=2", html);
            Assert.Contains($"Type={(int)UserActivityType.LoginFailed}", html);
            Assert.Contains("From=2020-01-01", html);
            Assert.Contains("href=\"/UserActivity\"", html); // entrada del menú
        }
    }
}
