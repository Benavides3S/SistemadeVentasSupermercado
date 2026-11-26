using System.Net;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using SistemadeVentasSupermercado.Tests.Infraestructura;
using SistemadeVentasSupermercado.Web.Data;
using SistemadeVentasSupermercado.Web.Data.Entities;
using SistemadeVentasSupermercado.Web.Data.Seeders;
using SistemadeVentasSupermercado.Web.DTOs;
using SistemadeVentasSupermercado.Web.Services.Implementations;

namespace SistemadeVentasSupermercado.Tests
{
    /// <summary>
    /// Autorización de supervisor con la aplicación real: "Mi PIN", "Anular o devolver" y los formularios,
    /// incluidos los manipulados.
    /// </summary>
    [Collection(ColeccionAppWeb.Nombre)]
    public class SupervisorAuthorizationHttpTests
    {
        private const string Pin = "428317";
        private const string PinMalo = "905162";

        private readonly AppWebFixture _app;

        public SupervisorAuthorizationHttpTests(AppWebFixture app) => _app = app;

        // ---------- Ayudantes ----------

        private async Task<T> ConBaseAsync<T>(Func<DataContext, Task<T>> accion)
        {
            using IServiceScope scope = _app.Services.CreateScope();
            return await accion(scope.ServiceProvider.GetRequiredService<DataContext>());
        }

        private async Task<User> CrearSupervisorAsync()
        {
            using IServiceScope scope = _app.Services.CreateScope();
            DataContext db = scope.ServiceProvider.GetRequiredService<DataContext>();
            string marca = Guid.NewGuid().ToString("N")[..8];
            User supervisor = new()
            {
                UserName = $"sup-{marca}@pruebas.local",
                Email = $"sup-{marca}@pruebas.local",
                EmailConfirmed = true,
                Document = $"SUP-{marca}",
                FirstName = "Autoriza",
                LastName = marca,
                SistemaVentasRoleId = await db.SistemaVentasRoles
                    .Where(r => r.Name == UserRolesSeeder.SUPERVISOR_ROLE_NAME).Select(r => r.Id).SingleAsync()
            };
            IdentityResult resultado = await scope.ServiceProvider.GetRequiredService<UserManager<User>>()
                .CreateAsync(supervisor, _app.Clave);
            Assert.True(resultado.Succeeded);
            return supervisor;
        }

        private static async Task<HttpResponseMessage> PostAsync(HttpClient cliente, string ruta,
            params (string, string)[] campos)
        {
            string token = AppWebFixture.ExtraerToken(await cliente.GetStringAsync("/Account/UpdateUser"));
            List<KeyValuePair<string, string>> formulario = campos.Select(c => new KeyValuePair<string, string>(c.Item1, c.Item2)).ToList();
            formulario.Add(new("__RequestVerificationToken", token));
            return await cliente.PostAsync(ruta, new FormUrlEncodedContent(formulario));
        }

        /// <summary>El supervisor crea su PIN por la pantalla real.</summary>
        private async Task<User> SupervisorConPinAsync()
        {
            User supervisor = await CrearSupervisorAsync();
            using HttpClient cliente = await _app.CrearClienteConSesionAsync(supervisor.Email!);

            HttpResponseMessage r = await PostAsync(cliente, "/Account/AuthorizationPin",
                ("CurrentPassword", _app.Clave), ("Pin", Pin), ("ConfirmPin", Pin));

            Assert.Equal(HttpStatusCode.Redirect, r.StatusCode);
            return supervisor;
        }

        /// <summary>Un cajero propio, con caja abierta y una venta de 3 unidades.</summary>
        private async Task<(User Cajero, SaleResultDTO Venta)> CajeroConVentaAsync()
        {
            var (cajero, _) = await _app.CrearCajeroConCajaAsync();
            SaleResultDTO venta = await ConBaseAsync(async db =>
            {
                Guid categoria = await db.Categories.Where(c => c.IsActive).Select(c => c.Id).FirstAsync();
                Product producto = new()
                {
                    Id = Guid.NewGuid(),
                    Name = $"Autorización {Guid.NewGuid().ToString("N")[..8]}",
                    Price = 1000m,
                    Stock = 10m,
                    Unit = UnitOfMeasure.Unit,
                    CategoryId = categoria
                };
                db.Products.Add(producto);
                await db.SaveChangesAsync();

                return (await new SalesService(db).CreateAsync(new CreateSaleDTO
                {
                    PaymentMethod = PaymentMethod.Cash,
                    IdempotencyKey = Guid.NewGuid(),
                    Items = [new SaleLineDTO { ProductId = producto.Id, Quantity = 3 }]
                }, cajero.Id)).Result!;
            });
            return (cajero, venta);
        }

        private Task<Sale> VentaAsync(Guid id)
            => ConBaseAsync(db => db.Sales.AsNoTracking().Include(s => s.Returns).SingleAsync(s => s.Id == id));

        private static void AssertNiega(HttpResponseMessage r)
        {
            Assert.Equal(HttpStatusCode.Redirect, r.StatusCode);
            Assert.Contains("/Error/403", r.Headers.Location?.ToString());
        }

        private static async Task<string> TextoAsync(HttpResponseMessage r)
        {
            Assert.Equal(HttpStatusCode.OK, r.StatusCode);
            return WebUtility.HtmlDecode(await r.Content.ReadAsStringAsync());
        }

        // ---------- Mi PIN y menús ----------

        [Theory]
        [InlineData(AppWebFixture.Admin, true)]
        [InlineData(AppWebFixture.Supervisor, true)]
        [InlineData(AppWebFixture.Cajero, false)]
        [InlineData(AppWebFixture.SinPermisos, false)]
        public async Task MiPin_SoloParaQuienPuedeAutorizar(string correo, bool puede)
        {
            using HttpClient cliente = await _app.CrearClienteConSesionAsync(correo);

            HttpResponseMessage pagina = await cliente.GetAsync("/Account/AuthorizationPin");
            string inicio = await cliente.GetStringAsync("/");

            if (puede)
            {
                Assert.Equal(HttpStatusCode.OK, pagina.StatusCode);
                Assert.Contains("href=\"/Account/AuthorizationPin\"", inicio);
            }
            else
            {
                AssertNiega(pagina);
                Assert.DoesNotContain("href=\"/Account/AuthorizationPin\"", inicio);
                AssertNiega(await PostAsync(cliente, "/Account/AuthorizationPin",
                    ("CurrentPassword", _app.Clave), ("Pin", Pin), ("ConfirmPin", Pin)));
            }
        }

        [Fact]
        public async Task MiPin_ConError_NoDevuelveNiLaClaveNiElPin()
        {
            User supervisor = await CrearSupervisorAsync();
            using HttpClient cliente = await _app.CrearClienteConSesionAsync(supervisor.Email!);

            string html = await TextoAsync(await PostAsync(cliente, "/Account/AuthorizationPin",
                ("CurrentPassword", "Mala2026Clave"), ("Pin", Pin), ("ConfirmPin", Pin)));

            Assert.Contains("La contraseña actual no es correcta.", html);
            Assert.DoesNotContain("Mala2026Clave", html);
            Assert.DoesNotContain(Pin, html);
        }

        [Theory]
        [InlineData(AppWebFixture.Cajero, true)]
        [InlineData(AppWebFixture.Supervisor, false)] // tiene todo: usa "Ventas"
        [InlineData(AppWebFixture.SinPermisos, false)]
        public async Task Menu_AnularODevolver_SoloParaQuienLeFaltanPermisos(string correo, bool sale)
        {
            using HttpClient cliente = await _app.CrearClienteConSesionAsync(correo);

            string inicio = await cliente.GetStringAsync("/");

            Assert.Equal(sale, inicio.Contains("href=\"/Sales/Assist\""));
        }

        // ---------- Lo que NO se puede ----------

        [Fact]
        public async Task SinPuntoDeVenta_NoEntraANingunaRutaNueva()
        {
            using HttpClient cliente = await _app.CrearClienteConSesionAsync(AppWebFixture.SinPermisos);
            Guid id = Guid.NewGuid();

            AssertNiega(await cliente.GetAsync("/Sales/Assist?number=1"));
            AssertNiega(await cliente.GetAsync($"/SaleReturns/CreateWithAuthorization?saleId={id}"));
            AssertNiega(await PostAsync(cliente, $"/SaleReturns/CreateWithAuthorization?saleId={id}"));
            AssertNiega(await PostAsync(cliente, $"/Sales/CancelWithAuthorization/{id}"));
        }

        [Fact]
        public async Task ElCajero_NoPuedeUsarLasRutasSinAutorizacion_AunqueEnvieUnPinBueno()
        {
            User supervisor = await SupervisorConPinAsync();
            var (cajero, venta) = await CajeroConVentaAsync();
            using HttpClient cliente = await _app.CrearClienteConSesionAsync(cajero.Email!);

            AssertNiega(await PostAsync(cliente, $"/Sales/Cancel/{venta.Id}", ("Reason", "x"),
                ("authorizerUserId", supervisor.Id), ("authorizationPin", Pin)));
            AssertNiega(await PostAsync(cliente, $"/SaleReturns/Create?saleId={venta.Id}", ("Reason", "x")));

            Assert.Equal(SaleStatus.Completed, (await VentaAsync(venta.Id)).Status);
        }

        [Theory]
        [InlineData("sin datos")]
        [InlineData("pin malo")]
        [InlineData("el mismo cajero")]
        [InlineData("id inventado")]
        public async Task Anular_FormularioManipuladoOIncompleto_NoAnula(string caso)
        {
            User supervisor = await SupervisorConPinAsync();
            var (cajero, venta) = await CajeroConVentaAsync();
            using HttpClient cliente = await _app.CrearClienteConSesionAsync(cajero.Email!);

            (string quien, string pin) = caso switch
            {
                "pin malo" => (supervisor.Id, PinMalo),
                "el mismo cajero" => (cajero.Id, Pin),
                "id inventado" => (Guid.NewGuid().ToString(), Pin),
                _ => ("", "")
            };

            // Campos de más que alguien podría añadir: no se usan.
            HttpResponseMessage r = await PostAsync(cliente, $"/Sales/CancelWithAuthorization/{venta.Id}",
                ("Reason", "Cliente se arrepintió"), ("authorizerUserId", quien), ("authorizationPin", pin),
                ("authorizedByUserId", supervisor.Id), ("CancelAuthorizedByUserId", supervisor.Id));

            await TextoAsync(r);
            Sale guardada = await VentaAsync(venta.Id);
            Assert.Equal(SaleStatus.Completed, guardada.Status);
            Assert.Null(guardada.CancelAuthorizedByUserId);
        }

        [Fact]
        public async Task SinTokenAntiforgery_SeRechaza()
        {
            User supervisor = await SupervisorConPinAsync();
            var (cajero, venta) = await CajeroConVentaAsync();
            using HttpClient cliente = await _app.CrearClienteConSesionAsync(cajero.Email!);

            HttpResponseMessage r = await cliente.PostAsync($"/Sales/CancelWithAuthorization/{venta.Id}",
                new FormUrlEncodedContent([new("Reason", "x"), new("authorizerUserId", supervisor.Id),
                    new("authorizationPin", Pin)]));

            Assert.Equal(HttpStatusCode.BadRequest, r.StatusCode);
            Assert.Equal(SaleStatus.Completed, (await VentaAsync(venta.Id)).Status);
        }

        // ---------- El recorrido completo ----------

        [Fact]
        public async Task Anular_ElCajeroBuscaPorNumero_YElSupervisorAutoriza()
        {
            User supervisor = await SupervisorConPinAsync();
            var (cajero, venta) = await CajeroConVentaAsync();
            using HttpClient cliente = await _app.CrearClienteConSesionAsync(cajero.Email!);

            string vacia = await TextoAsync(await cliente.GetAsync("/Sales/Assist"));
            Assert.DoesNotContain("TOTAL", vacia); // sin número no hay ventas a la vista

            string pagina = await TextoAsync(await cliente.GetAsync($"/Sales/Assist?number={venta.Number}"));
            Assert.Contains($"/Sales/CancelWithAuthorization/{venta.Id}", pagina);
            Assert.Contains(supervisor.LastName, pagina);
            Assert.Contains($"/SaleReturns/CreateWithAuthorization?saleId={venta.Id}", pagina);

            // Con el PIN equivocado conserva el motivo y no repite el PIN.
            string error = await TextoAsync(await PostAsync(cliente, $"/Sales/CancelWithAuthorization/{venta.Id}",
                ("Reason", "Cobro repetido"), ("authorizerUserId", supervisor.Id), ("authorizationPin", PinMalo)));
            Assert.Contains("PIN incorrecto", error);
            Assert.Contains("value=\"Cobro repetido\"", error);
            Assert.DoesNotContain(PinMalo, error);

            HttpResponseMessage bien = await PostAsync(cliente, $"/Sales/CancelWithAuthorization/{venta.Id}",
                ("Reason", "Cobro repetido"), ("authorizerUserId", supervisor.Id), ("authorizationPin", Pin));

            Assert.Equal(HttpStatusCode.Redirect, bien.StatusCode);
            Assert.Contains($"/Sales/Assist?number={venta.Number}", bien.Headers.Location?.ToString());
            Sale guardada = await VentaAsync(venta.Id);
            Assert.Equal(SaleStatus.Cancelled, guardada.Status);
            Assert.Equal(cajero.Id, guardada.CancelledByUserId);
            Assert.Equal(supervisor.Id, guardada.CancelAuthorizedByUserId);

            // La autorización no deja ningún permiso en la sesión.
            AssertNiega(await cliente.GetAsync("/Sales"));
        }

        [Fact]
        public async Task Devolver_ConservaLoEscrito_YElCajeroVeSuComprobante()
        {
            User supervisor = await SupervisorConPinAsync();
            var (cajero, venta) = await CajeroConVentaAsync();
            using HttpClient cliente = await _app.CrearClienteConSesionAsync(cajero.Email!);
            Guid linea = await ConBaseAsync(db => db.SaleItems.Where(i => i.SaleId == venta.Id).Select(i => i.Id).SingleAsync());
            string ruta = $"/SaleReturns/CreateWithAuthorization?saleId={venta.Id}";

            string formulario = await TextoAsync(await cliente.GetAsync(ruta));
            Assert.Contains("authorizationPin", formulario);
            Assert.Contains(supervisor.LastName, formulario);

            (string, string)[] Campos(string pin) =>
            [
                ("Reason", "Producto dañado"),
                ("Lines[0].SaleItemId", linea.ToString()),
                ("Lines[0].Quantity", "2"),
                ("Lines[0].Restock", "false"),
                ("authorizerUserId", supervisor.Id),
                ("authorizationPin", pin)
            ];

            string error = await TextoAsync(await PostAsync(cliente, ruta, Campos(PinMalo)));
            Assert.Contains("PIN incorrecto", error);
            Assert.Contains("name=\"Lines[0].Quantity\" value=\"2\"", error);
            Assert.Contains("value=\"Producto dañado\"", error);
            Assert.DoesNotContain("checked", error.Split("name=\"Lines[0].Restock\"")[1].Split('>')[0]);
            Assert.DoesNotContain(PinMalo, error);
            Assert.Empty((await VentaAsync(venta.Id)).Returns);

            HttpResponseMessage bien = await PostAsync(cliente, ruta, Campos(Pin));

            Assert.Equal(HttpStatusCode.Redirect, bien.StatusCode);
            SaleReturn devolucion = Assert.Single((await VentaAsync(venta.Id)).Returns);
            Assert.Equal(cajero.Id, devolucion.UserId);
            Assert.Equal(supervisor.Id, devolucion.AuthorizedByUserId);

            string comprobante = await TextoAsync(await cliente.GetAsync(bien.Headers.Location));
            Assert.Contains("DEVOLUCIÓN", comprobante);
            Assert.Contains($"/Sales/Assist?number={venta.Number}", comprobante);
        }
    }
}
