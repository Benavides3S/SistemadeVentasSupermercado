using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using SistemadeVentasSupermercado.Web.Data;
using SistemadeVentasSupermercado.Web.Data.Entities;

namespace SistemadeVentasSupermercado.Tests.Infraestructura
{
    /// <summary>
    /// Se ejecuta UNA vez antes de todas las pruebas: borra la base de pruebas,
    /// la vuelve a crear con las migraciones y agrega los datos mínimos
    /// (un rol, un cajero y una categoría).
    /// </summary>
    public class BaseDeDatosFixture : IAsyncLifetime
    {
        // Seguro anti-accidentes: solo se permite borrar una base con este nombre exacto.
        public const string NombreBaseDePruebas = "SistemaVentasTests";

        private readonly DbContextOptions<DataContext> _opciones;

        public string CajeroId { get; private set; } = string.Empty;
        public Guid CategoriaId { get; private set; }

        public BaseDeDatosFixture()
        {
            // La variable de entorno ConnectionStrings__TestsConnection reemplaza al archivo si existe.
            IConfiguration configuracion = new ConfigurationBuilder()
                .SetBasePath(AppContext.BaseDirectory)
                .AddJsonFile("appsettings.Tests.json", optional: false)
                .AddEnvironmentVariables()
                .Build();

            string cadena = configuracion.GetConnectionString("TestsConnection")
                ?? throw new InvalidOperationException(
                    "Falta la cadena de conexión 'TestsConnection' en appsettings.Tests.json");

            string nombreBase = new SqlConnectionStringBuilder(cadena).InitialCatalog;
            if (!string.Equals(nombreBase, NombreBaseDePruebas, StringComparison.Ordinal))
            {
                throw new InvalidOperationException(
                    $"Las pruebas solo pueden usar la base '{NombreBaseDePruebas}', " +
                    $"pero la cadena de conexión apunta a '{nombreBase}'. No se borró nada.");
            }

            _opciones = new DbContextOptionsBuilder<DataContext>()
                .UseSqlServer(cadena)
                .Options;
        }

        /// <summary>
        /// Cada llamada crea un contexto nuevo, como cada petición web en la aplicación real.
        /// </summary>
        public DataContext CrearContexto() => new DataContext(_opciones);

        public async Task InitializeAsync()
        {
            await using DataContext context = CrearContexto();

            await context.Database.EnsureDeletedAsync();
            await context.Database.MigrateAsync();

            SistemaVentasRole rol = new SistemaVentasRole { Id = Guid.NewGuid(), Name = "Cajero pruebas" };

            User cajero = new User
            {
                UserName = "cajero@pruebas.local",
                Email = "cajero@pruebas.local",
                Document = "PRUEBAS-001",
                FirstName = "Cajero",
                LastName = "Pruebas",
                SistemaVentasRoleId = rol.Id
            };

            Category categoria = new Category { Id = Guid.NewGuid(), Name = "Categoría pruebas" };

            context.SistemaVentasRoles.Add(rol);
            context.Users.Add(cajero);
            context.Categories.Add(categoria);
            await context.SaveChangesAsync();

            CajeroId = cajero.Id;
            CategoriaId = categoria.Id;
        }

        // La base se deja como quedó para poder revisarla; la próxima ejecución la recrea.
        public Task DisposeAsync() => Task.CompletedTask;
    }
}
