using AspNetCoreHero.ToastNotification;
using AspNetCoreHero.ToastNotification.Extensions;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using SistemadeVentasSupermercado.Web.Core;
using SistemadeVentasSupermercado.Web.Data;
using SistemadeVentasSupermercado.Web.Data.Entities;
using SistemadeVentasSupermercado.Web.Data.Seeders;
using SistemadeVentasSupermercado.Web.Helpers.Abstractions;
using SistemadeVentasSupermercado.Web.Helpers.Implementations;
using SistemadeVentasSupermercado.Web.Services.Abstractions;
using SistemadeVentasSupermercado.Web.Services.Implementations;

namespace SistemadeVentasSupermercado.Web
{
    public static class CustomConfiguration
    {
        public static WebApplicationBuilder AddCustomConfiguration(this WebApplicationBuilder builder)
        {
            string? cnn = builder.Configuration.GetConnectionString("MyConnection");
            builder.Logging.ClearProviders();
            builder.Logging.AddConsole();
            builder.Logging.AddDebug();
            builder.Logging.SetMinimumLevel(LogLevel.Debug);

            // Data Context
            builder.Services.AddDbContext<DataContext>(options =>
            {
                options.UseSqlServer(builder.Configuration.GetConnectionString("MyConnection"));
            });

            // Toast Notification Setup
            builder.Services.AddNotyf(config =>
            {
                config.DurationInSeconds = 10;
                config.IsDismissable = true;
                config.Position = NotyfPosition.BottomRight;
            });

            // Identity and Access Management
            AddIAM(builder);

            // Services
            AddServices(builder);

            builder.Services.AddHttpContextAccessor();

            // Llaves que firman la cookie de sesión y los tokens antiforgery. En Docker se guardan
            // en un volumen (DataProtection:KeysPath); si no, al recrear el contenedor se perderían
            // y todos los usuarios quedarían deslogueados. Sin la opción se usa la ubicación por defecto.
            string? keysPath = builder.Configuration["DataProtection:KeysPath"];
            if (!string.IsNullOrWhiteSpace(keysPath))
            {
                builder.Services.AddDataProtection()
                    .PersistKeysToFileSystem(new DirectoryInfo(keysPath));
            }

            return builder;
        }

        /// <summary>
        /// Política de contraseñas y bloqueo. Pública para que las pruebas usen exactamente la misma.
        /// </summary>
        public static void ConfigureIdentityOptions(IdentityOptions conf)
        {
            conf.Password.RequireDigit = true;
            conf.Password.RequiredUniqueChars = 4;
            conf.Password.RequireLowercase = true;
            conf.Password.RequireUppercase = true;
            conf.Password.RequireNonAlphanumeric = false;
            conf.Password.RequiredLength = 8;

            // 5 intentos fallidos seguidos bloquean la cuenta 15 minutos (contra quien prueba contraseñas).
            conf.Lockout.AllowedForNewUsers = true;
            conf.Lockout.MaxFailedAccessAttempts = 5;
            conf.Lockout.DefaultLockoutTimeSpan = TimeSpan.FromMinutes(15);
        }

        private static void AddIAM(WebApplicationBuilder builder)
        {
            builder.Services.AddIdentity<User, IdentityRole>(ConfigureIdentityOptions)
              .AddEntityFrameworkStores<DataContext>()
              .AddDefaultTokenProviders();

            // El "sello de seguridad" cambia al restablecer o cambiar la contraseña. Por defecto Identity lo
            // revisa cada 30 minutos y mientras tanto las sesiones viejas siguen sirviendo: se revisa en
            // cada petición para que se cierren de inmediato (cuesta unas consultas más por petición).
            builder.Services.Configure<SecurityStampValidatorOptions>(o => o.ValidationInterval = TimeSpan.Zero);

            builder.Services.ConfigureApplicationCookie(conf =>
            {
                conf.Cookie.Name = "Auth";
                conf.ExpireTimeSpan = TimeSpan.FromHours(8);
                conf.SlidingExpiration = true;
                conf.LoginPath = "/Account/Login";
                conf.AccessDeniedPath = "/Error/403";
                // Usuario desactivado: pierde el acceso en su siguiente petición.
                conf.Events.OnValidatePrincipal = ActiveUserCookieValidator.ValidateAsync;
            });
        }

        private static void AddServices(WebApplicationBuilder builder)
        {
            builder.Services.AddScoped<IProductService, ProductsService>();
            builder.Services.AddScoped<IClientService, ClientsService>();
            builder.Services.AddScoped<IRolesService, RolesService>();
            builder.Services.AddScoped<IUsersService, UsersService>();
            builder.Services.AddScoped<ICategoryService, CategoriesService>();
            builder.Services.AddScoped<ISupplierService, SuppliersService>();

            builder.Services.AddTransient<SeedDb>();
            builder.Services.AddScoped<ISalesService, SalesService>();
            builder.Services.AddScoped<ICashSessionService, CashSessionService>();
            builder.Services.AddScoped<ISaleReturnsService, SaleReturnsService>();
            builder.Services.AddScoped<IPurchasesService, PurchasesService>();
            builder.Services.AddScoped<IInventoryService, InventoryService>();
            builder.Services.AddScoped<IReportsService, ReportsService>();
            builder.Services.AddScoped<IHomeService, HomeService>();
            builder.Services.AddTransient<ICombosHelper, CombosHelper>();

            // Datos del negocio para el comprobante (sección "Negocio" de appsettings.json).
            builder.Services.Configure<BusinessOptions>(builder.Configuration.GetSection(BusinessOptions.SectionName));
        }

        public static WebApplication AddCustomWebApplicationConfiguration(this WebApplication app)
        {
            app.UseNotyf();

            SeedData(app);

            return app;
        }

        private static void SeedData(WebApplication app)
        {
            IServiceScopeFactory scopeFactory = app.Services.GetService<IServiceScopeFactory>();

            using IServiceScope scope = scopeFactory.CreateScope();

            // Las migraciones no se aplican solas: si faltan, se avisa claramente en vez de fallar más adelante.
            DataContext context = scope.ServiceProvider.GetRequiredService<DataContext>();
            List<string> pendientes = context.Database.GetPendingMigrations().ToList();
            if (pendientes.Count > 0)
            {
                throw new InvalidOperationException(
                    "La base de datos no está actualizada. Faltan estas migraciones: " + string.Join(", ", pendientes) +
                    ". En desarrollo: Update-Database. En Docker: docker/aplicar-migraciones.ps1 (ver docker/LEEME.md).");
            }

            SeedDb service = scope.ServiceProvider.GetService<SeedDb>();
            string? passwordInicial = app.Configuration["Seed:PasswordInicial"];
            service.SeedAsync(passwordInicial, app.Environment.IsDevelopment()).Wait();
           
        }
    }
}