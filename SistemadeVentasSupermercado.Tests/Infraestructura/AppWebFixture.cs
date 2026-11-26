using System.Net;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using SistemadeVentasSupermercado.Web.Data;
using SistemadeVentasSupermercado.Web.Core;
using SistemadeVentasSupermercado.Web.Data.Entities;
using SistemadeVentasSupermercado.Web.DTOs;
using SistemadeVentasSupermercado.Web.Services.Implementations;

namespace SistemadeVentasSupermercado.Tests.Infraestructura
{
    /// <summary>
    /// Arranca la aplicación web completa (Program.cs, filtros, cookie, seeders) contra una base propia
    /// <c>SistemaVentasTestsWeb</c>, distinta de la de <see cref="BaseDeDatosFixture"/> para que los datos
    /// de ejemplo de los seeders no se mezclen con los de las otras pruebas.
    /// Deja listos cuatro usuarios: Admin, Cajero, Supervisor (los del seeder) y uno sin permisos.
    /// </summary>
    public class AppWebFixture : WebApplicationFactory<Program>, IAsyncLifetime
    {
        public const string NombreBaseWeb = "SistemaVentasTestsWeb";

        public const string Admin = "manuel@yopmail.com";
        public const string Cajero = "anad@yopmail.com";
        public const string Supervisor = "jhond@yopmail.com";
        public const string SinPermisos = "sinpermisos@pruebas.local";

        // Clave distinta en cada ejecución: no queda escrita en ningún archivo.
        public string Clave { get; } = $"Pw-{Guid.NewGuid():N}aA1";

        /// <summary>Un producto de ejemplo (sembrado en Development), para las pantallas que lo piden.</summary>
        public Guid ProductoId { get; private set; }

        // Datos para probar los comprobantes que revisan el permiso dentro de la acción.
        /// <summary>Devolución hecha por el Cajero.</summary>
        public Guid DevolucionId { get; private set; }
        /// <summary>Turno del Cajero ya cerrado.</summary>
        public Guid TurnoCerradoCajeroId { get; private set; }
        /// <summary>Turno del Cajero todavía abierto (arqueo ciego: no puede ver su reporte).</summary>
        public Guid TurnoAbiertoCajeroId { get; private set; }
        /// <summary>Turno abierto del Supervisor (ajeno al Cajero).</summary>
        public Guid TurnoAbiertoSupervisorId { get; private set; }

        private readonly string _cadena;

        public AppWebFixture()
        {
            IConfiguration configuracion = new ConfigurationBuilder()
                .SetBasePath(AppContext.BaseDirectory)
                .AddJsonFile("appsettings.Tests.json", optional: false)
                .AddEnvironmentVariables()
                .Build();

            string cadenaTests = configuracion.GetConnectionString("TestsConnection")
                ?? throw new InvalidOperationException("Falta la cadena 'TestsConnection' en appsettings.Tests.json");

            // Mismo servidor que las otras pruebas, pero con la base web.
            SqlConnectionStringBuilder partes = new(cadenaTests) { InitialCatalog = NombreBaseWeb };
            _cadena = partes.ConnectionString;
        }

        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            // Development: crea anad (Cajero) y jhond (Supervisor) además del Admin.
            builder.UseEnvironment("Development");
            builder.UseSetting("ConnectionStrings:MyConnection", _cadena);
            builder.UseSetting("Seed:PasswordInicial", Clave);
        }

        public async Task InitializeAsync()
        {
            // Las migraciones no se aplican solas: la app se niega a arrancar si faltan.
            DbContextOptions<DataContext> opciones = new DbContextOptionsBuilder<DataContext>()
                .UseSqlServer(_cadena).Options;

            await using (DataContext context = new(opciones))
            {
                await context.Database.EnsureDeletedAsync();
                await context.Database.MigrateAsync();
            }

            // Arranca la aplicación (corren los seeders).
            using IServiceScope scope = Services.CreateScope();
            DataContext db = scope.ServiceProvider.GetRequiredService<DataContext>();

            // El Admin sembrado nace con contraseña temporal y todas las páginas lo mandarían a cambiarla.
            await db.Users.Where(u => u.UserName == Admin)
                .ExecuteUpdateAsync(s => s.SetProperty(u => u.MustChangePassword, false));

            ProductoId = await db.Products.Select(p => p.Id).FirstAsync();

            await CrearDatosDeCajaAsync(db);

            // Usuario con un rol sin ningún permiso.
            SistemaVentasRole rol = new() { Id = Guid.NewGuid(), Name = "Sin permisos pruebas" };
            db.SistemaVentasRoles.Add(rol);
            await db.SaveChangesAsync();

            UserManager<User> usuarios = scope.ServiceProvider.GetRequiredService<UserManager<User>>();
            IdentityResult resultado = await usuarios.CreateAsync(new User
            {
                UserName = SinPermisos,
                Email = SinPermisos,
                EmailConfirmed = true,
                Document = "PRUEBAS-SINPERM",
                FirstName = "Sin",
                LastName = "Permisos",
                SistemaVentasRoleId = rol.Id
            }, Clave);

            if (!resultado.Succeeded)
                throw new InvalidOperationException("No se pudo crear el usuario sin permisos: "
                    + string.Join("; ", resultado.Errors.Select(e => e.Description)));
        }

        private async Task CrearDatosDeCajaAsync(DataContext db)
        {
            string cajeroId = await db.Users.Where(u => u.UserName == Cajero).Select(u => u.Id).SingleAsync();
            string supervisorId = await db.Users.Where(u => u.UserName == Supervisor).Select(u => u.Id).SingleAsync();
            Guid productoVentaId = await db.Products
                .Where(p => p.Unit == UnitOfMeasure.Unit && p.Stock >= 5 && p.Category.IsActive)
                .Select(p => p.Id).FirstAsync();

            CashSessionService caja = new(db);

            // Turno 1 del Cajero: abre, vende, devuelve y cierra.
            Guid turnoCerrado = (await caja.OpenAsync(cajeroId, new OpenCashSessionDTO { OpeningAmount = 0m })).Result!.Id;

            SaleResultDTO venta = (await new SalesService(db).CreateAsync(new CreateSaleDTO
            {
                PaymentMethod = PaymentMethod.Cash,
                IdempotencyKey = Guid.NewGuid(),
                Items = [new SaleLineDTO { ProductId = productoVentaId, Quantity = 2 }]
            }, cajeroId)).Result!;

            Guid lineaId = await db.SaleItems.Where(i => i.SaleId == venta.Id).Select(i => i.Id).SingleAsync();
            Response<SaleReturnDetailDTO> devolucion = await new SaleReturnsService(db).CreateAsync(venta.Id,
                new CreateSaleReturnDTO
                {
                    Reason = "Prueba de permisos",
                    Lines = [new SaleReturnLineDTO { SaleItemId = lineaId, Quantity = 1, Restock = true }]
                }, cajeroId);
            DevolucionId = devolucion.Result!.Id;

            Response<CashSessionDTO> cierre = await caja.CloseAsync(turnoCerrado,
                new CloseCashSessionDTO { CountedAmount = 0m }, cajeroId, canCloseOthers: false);
            if (!cierre.IsSuccess) throw new InvalidOperationException("No se pudo cerrar el turno de prueba: " + cierre.Message);
            TurnoCerradoCajeroId = turnoCerrado;

            // Turno 2 del Cajero (queda abierto) y turno abierto del Supervisor.
            TurnoAbiertoCajeroId = (await caja.OpenAsync(cajeroId, new OpenCashSessionDTO { OpeningAmount = 0m })).Result!.Id;
            TurnoAbiertoSupervisorId = (await caja.OpenAsync(supervisorId, new OpenCashSessionDTO { OpeningAmount = 0m })).Result!.Id;
        }

        Task IAsyncLifetime.DisposeAsync() => Task.CompletedTask;

        /// <summary>Cliente que no sigue redirecciones (para ver a dónde manda la aplicación) y guarda cookies.</summary>
        public HttpClient CrearCliente() => CreateClient(new WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = false,
            HandleCookies = true
        });

        /// <summary>Cliente con la sesión iniciada por el formulario real de login.</summary>
        public async Task<HttpClient> CrearClienteConSesionAsync(string correo)
        {
            HttpClient cliente = CrearCliente();
            string token = ExtraerToken(await cliente.GetStringAsync("/Account/Login"));

            HttpResponseMessage respuesta = await cliente.PostAsync("/Account/Login", new FormUrlEncodedContent(
            [
                new("Email", correo),
                new("Password", Clave),
                new("__RequestVerificationToken", token)
            ]));

            if (respuesta.StatusCode != HttpStatusCode.Redirect)
                throw new InvalidOperationException($"El login de {correo} falló: {(int)respuesta.StatusCode}.");

            return cliente;
        }

        /// <summary>Lee el token antiforgery de un formulario.</summary>
        public static string ExtraerToken(string html)
        {
            Match m = Regex.Match(html, "name=\"__RequestVerificationToken\"[^>]*value=\"([^\"]+)\"");
            if (!m.Success)
                throw new InvalidOperationException("La página no tiene token antiforgery.");
            return m.Groups[1].Value;
        }
    }

    [CollectionDefinition(Nombre)]
    public class ColeccionAppWeb : ICollectionFixture<AppWebFixture>
    {
        public const string Nombre = "Aplicación web de pruebas";
    }
}
