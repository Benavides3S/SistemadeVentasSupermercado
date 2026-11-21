using AspNetCoreHero.ToastNotification;
using AspNetCoreHero.ToastNotification.Extensions;
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

            // AutoMapper
            builder.Services.AddAutoMapper(typeof(Program));

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

            return builder;
        }

        private static void AddIAM(WebApplicationBuilder builder)
        {
            builder.Services.AddIdentity<User, IdentityRole>(conf =>
            {
                conf.Password.RequireDigit = true;
                conf.Password.RequiredUniqueChars = 4;
                conf.Password.RequireLowercase = true;
                conf.Password.RequireUppercase = true;
                conf.Password.RequireNonAlphanumeric = false;
                conf.Password.RequiredLength = 8;
            }).AddEntityFrameworkStores<DataContext>()
              .AddDefaultTokenProviders();

            builder.Services.ConfigureApplicationCookie(conf =>
            {
                conf.Cookie.Name = "Auth";
                conf.ExpireTimeSpan = TimeSpan.FromHours(8);
                conf.SlidingExpiration = true;
                conf.LoginPath = "/Account/Login";
                conf.AccessDeniedPath = "/Error/403";
            });
        }

        private static void AddServices(WebApplicationBuilder builder)
        {
            builder.Services.AddScoped<IProductService, ProductsService>();
            builder.Services.AddScoped<IClientService, ClientsService>();
            builder.Services.AddScoped<IRolesService, RolesService>();
            builder.Services.AddScoped<IUsersService, UsersService>();
            builder.Services.AddScoped<ICategoryService, CategoriesService>();

            builder.Services.AddTransient<SeedDb>();
            builder.Services.AddScoped<ISalesService, SalesService>();
            builder.Services.AddScoped<ICashSessionService, CashSessionService>();
            builder.Services.AddScoped<ISaleReturnsService, SaleReturnsService>();
            builder.Services.AddScoped<IReportsService, ReportsService>();
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
            SeedDb service = scope.ServiceProvider.GetService<SeedDb>();
            string? passwordInicial = app.Configuration["Seed:PasswordInicial"];
            service.SeedAsync(passwordInicial, app.Environment.IsDevelopment()).Wait();
           
        }
    }
}