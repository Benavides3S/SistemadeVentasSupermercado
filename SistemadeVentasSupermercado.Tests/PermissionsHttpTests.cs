using System.Net;
using SistemadeVentasSupermercado.Tests.Infraestructura;

namespace SistemadeVentasSupermercado.Tests
{
    /// <summary>
    /// Pruebas de permisos de punta a punta: arrancan la aplicación real, inician sesión con el formulario
    /// y piden páginas de Proveedores, Compras e Inventario con cada usuario.
    /// Las expectativas están escritas a mano (no se leen del seeder): si alguien cambia los permisos de
    /// ejemplo sin querer, estas pruebas fallan.
    ///   Admin      → todo.
    ///   Cajero     → ninguna de estas pantallas.
    ///   Supervisor → ver/registrar/anular compras y ver inventario (NO proveedores, NO ajustar inventario).
    ///   SinPermisos→ nada.
    /// </summary>
    [Collection(ColeccionAppWeb.Nombre)]
    public class PermissionsHttpTests
    {
        private readonly AppWebFixture _app;
        private static readonly Guid UnId = Guid.NewGuid();

        public PermissionsHttpTests(AppWebFixture app) => _app = app;

        // (correo, ruta GET, ¿puede entrar?)
        public static IEnumerable<object[]> PaginasGet()
        {
            (string Ruta, string[] Permitidos)[] paginas =
            [
                // Proveedores
                ("/Suppliers", [AppWebFixture.Admin]),
                ("/Suppliers/Create", [AppWebFixture.Admin]),
                // Compras
                ("/Purchases", [AppWebFixture.Admin, AppWebFixture.Supervisor]),
                ("/Purchases/Create", [AppWebFixture.Admin, AppWebFixture.Supervisor]),
                ("/Purchases/SearchProducts?term=a", [AppWebFixture.Admin, AppWebFixture.Supervisor]),
                // Inventario
                ("/Inventory", [AppWebFixture.Admin, AppWebFixture.Supervisor]),
                ("/Inventory/Adjust?productId={producto}", [AppWebFixture.Admin]),
            ];

            string[] usuarios = [AppWebFixture.Admin, AppWebFixture.Cajero, AppWebFixture.Supervisor, AppWebFixture.SinPermisos];

            foreach ((string ruta, string[] permitidos) in paginas)
                foreach (string usuario in usuarios)
                    yield return [usuario, ruta, permitidos.Contains(usuario)];
        }

        [Theory]
        [MemberData(nameof(PaginasGet))]
        public async Task Get_RespetaLosPermisosDelRol(string correo, string ruta, bool debePoderEntrar)
        {
            using HttpClient cliente = await _app.CrearClienteConSesionAsync(correo);

            HttpResponseMessage respuesta = await cliente.GetAsync(ruta.Replace("{producto}", _app.ProductoId.ToString()));

            if (debePoderEntrar)
            {
                Assert.Equal(HttpStatusCode.OK, respuesta.StatusCode);
            }
            else
            {
                Assert.Equal(HttpStatusCode.Redirect, respuesta.StatusCode);
                Assert.Contains("/Error/403", respuesta.Headers.Location?.ToString());
            }
        }

        [Theory]
        [InlineData("/Suppliers")]
        [InlineData("/Suppliers/Create")]
        [InlineData("/Purchases")]
        [InlineData("/Purchases/Create")]
        [InlineData("/Inventory")]
        [InlineData("/Inventory/Adjust?productId=00000000-0000-0000-0000-000000000001")]
        public async Task Get_SinSesion_LlevaAlLogin(string ruta)
        {
            using HttpClient cliente = _app.CrearCliente();

            HttpResponseMessage respuesta = await cliente.GetAsync(ruta);

            Assert.Equal(HttpStatusCode.Redirect, respuesta.StatusCode);
            Assert.Contains("/Account/Login", respuesta.Headers.Location?.ToString());
        }

        // Acciones que modifican datos: solo se prueba que se NIEGUEN (permitidas cambiarían datos).
        // (correo, ruta POST). Cada usuario de la lista NO tiene el permiso.
        public static IEnumerable<object[]> PostsDenegados()
        {
            string cajero = AppWebFixture.Cajero, sup = AppWebFixture.Supervisor, nada = AppWebFixture.SinPermisos;

            // deleteSuppliers / createSuppliers / updateSuppliers: nadie salvo Admin.
            foreach (string u in new[] { cajero, sup, nada })
            {
                yield return [u, $"/Suppliers/Delete/{UnId}"];
                yield return [u, "/Suppliers/Create"];
                yield return [u, "/Suppliers/Edit"];
                // adjustInventory: nadie salvo Admin (el Supervisor solo ve el inventario).
                yield return [u, "/Inventory/Adjust"];
            }

            // cancelPurchases / createPurchases: el Supervisor SÍ los tiene.
            foreach (string u in new[] { cajero, nada })
            {
                yield return [u, $"/Purchases/Cancel/{UnId}"];
                yield return [u, "/Purchases/Create"];
            }
        }

        [Theory]
        [MemberData(nameof(PostsDenegados))]
        public async Task Post_SinPermiso_SeNiega(string correo, string ruta)
        {
            using HttpClient cliente = await _app.CrearClienteConSesionAsync(correo);

            // Token válido de la sesión: así la respuesta viene del permiso y no del antiforgery.
            string token = AppWebFixture.ExtraerToken(await cliente.GetStringAsync("/Account/UpdateUser"));

            using HttpRequestMessage peticion = new(HttpMethod.Post, ruta)
            {
                Content = new FormUrlEncodedContent([new("__RequestVerificationToken", token)])
            };
            peticion.Headers.Add("RequestVerificationToken", token);

            HttpResponseMessage respuesta = await cliente.SendAsync(peticion);

            Assert.Equal(HttpStatusCode.Redirect, respuesta.StatusCode);
            Assert.Contains("/Error/403", respuesta.Headers.Location?.ToString());
        }

        // ---------- Comprobantes que revisan el permiso DENTRO de la acción ----------
        // No llevan [CustomAuthorize]: un solo atributo no expresa "uno u otro" ni "dueño con turno cerrado".
        // Usan datos reales: con un id inexistente la acción redirige al Inicio sin revisar el permiso.

        private static void AssertPuede(HttpResponseMessage r) => Assert.Equal(HttpStatusCode.OK, r.StatusCode);

        private static void AssertNiega(HttpResponseMessage r)
        {
            Assert.Equal(HttpStatusCode.Redirect, r.StatusCode);
            Assert.Contains("/Error/403", r.Headers.Location?.ToString());
        }

        // Devolución: createReturns o showSales.
        // Supervisor tiene ambos; el Cajero solo createSales y manageCashSessions.
        [Theory]
        [InlineData(AppWebFixture.Admin, true)]
        [InlineData(AppWebFixture.Supervisor, true)]
        [InlineData(AppWebFixture.Cajero, false)]
        [InlineData(AppWebFixture.SinPermisos, false)]
        public async Task ComprobanteDeDevolucion_RespetaLosPermisos(string correo, bool debePoderVer)
        {
            using HttpClient cliente = await _app.CrearClienteConSesionAsync(correo);

            HttpResponseMessage r = await cliente.GetAsync($"/SaleReturns/Details/{_app.DevolucionId}");

            if (debePoderVer) AssertPuede(r); else AssertNiega(r);
        }

        [Fact]
        public async Task ComprobanteDeDevolucion_SinSesion_LlevaAlLogin()
        {
            using HttpClient cliente = _app.CrearCliente();

            HttpResponseMessage r = await cliente.GetAsync($"/SaleReturns/Details/{_app.DevolucionId}");

            Assert.Equal(HttpStatusCode.Redirect, r.StatusCode);
            Assert.Contains("/Account/Login", r.Headers.Location?.ToString());
        }

        // Reporte de cierre: showCashSessions (cualquier turno) o el dueño con el turno ya cerrado.
        [Fact]
        public async Task ReporteDeCaja_Supervisor_VeCualquierTurno()
        {
            using HttpClient cliente = await _app.CrearClienteConSesionAsync(AppWebFixture.Supervisor);

            AssertPuede(await cliente.GetAsync($"/CashSessions/Report/{_app.TurnoCerradoCajeroId}"));
            AssertPuede(await cliente.GetAsync($"/CashSessions/Report/{_app.TurnoAbiertoCajeroId}"));
        }

        [Fact]
        public async Task ReporteDeCaja_Admin_VeCualquierTurno()
        {
            using HttpClient cliente = await _app.CrearClienteConSesionAsync(AppWebFixture.Admin);

            AssertPuede(await cliente.GetAsync($"/CashSessions/Report/{_app.TurnoCerradoCajeroId}"));
            AssertPuede(await cliente.GetAsync($"/CashSessions/Report/{_app.TurnoAbiertoSupervisorId}"));
        }

        [Fact]
        public async Task ReporteDeCaja_Cajero_SoloVeSuTurnoYaCerrado()
        {
            using HttpClient cliente = await _app.CrearClienteConSesionAsync(AppWebFixture.Cajero);

            // Su turno cerrado: sí.
            AssertPuede(await cliente.GetAsync($"/CashSessions/Report/{_app.TurnoCerradoCajeroId}"));
            // Su turno todavía abierto: no (arqueo ciego).
            AssertNiega(await cliente.GetAsync($"/CashSessions/Report/{_app.TurnoAbiertoCajeroId}"));
            // Turno de otra persona: no.
            AssertNiega(await cliente.GetAsync($"/CashSessions/Report/{_app.TurnoAbiertoSupervisorId}"));
        }

        [Fact]
        public async Task ReporteDeCaja_SinPermisos_NoVeNada()
        {
            using HttpClient cliente = await _app.CrearClienteConSesionAsync(AppWebFixture.SinPermisos);

            AssertNiega(await cliente.GetAsync($"/CashSessions/Report/{_app.TurnoCerradoCajeroId}"));
            AssertNiega(await cliente.GetAsync($"/CashSessions/Report/{_app.TurnoAbiertoSupervisorId}"));
        }

        [Fact]
        public async Task ReporteDeCaja_SinSesion_LlevaAlLogin()
        {
            using HttpClient cliente = _app.CrearCliente();

            HttpResponseMessage r = await cliente.GetAsync($"/CashSessions/Report/{_app.TurnoCerradoCajeroId}");

            Assert.Equal(HttpStatusCode.Redirect, r.StatusCode);
            Assert.Contains("/Account/Login", r.Headers.Location?.ToString());
        }
    }
}
