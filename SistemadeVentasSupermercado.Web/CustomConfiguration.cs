using Microsoft.EntityFrameworkCore;
using SistemadeVentasSupermercado.Web.Data;
using SistemadeVentasSupermercado.Web.Services.Abstractions;
using SistemadeVentasSupermercado.Web.Services.Implementations;
using AutoMapper;
using AspNetCoreHero.ToastNotification;
using Microsoft.Identity.Client;
using AspNetCoreHero.ToastNotification.Extensions;
using SistemadeVentasSupermercado.Web.Data.Seeders;
namespace SistemadeVentasSupermercado.Web
{
    public static class CustomConfiguration
    {
        public static WebApplicationBuilder AddCustomConfiguration(this WebApplicationBuilder builder)
        {
            
            builder.Services.AddDbContext<DataContext>(options => {

                options.UseSqlServer(builder.Configuration.GetConnectionString("Myconnection"));
            
            });
           
             builder.Services.AddAutoMapper(typeof(Program));

            
            builder.Services.AddNotyf(config =>
            {

                config.DurationInSeconds = 10;
                config.IsDismissable = true;
                config.Position = NotyfPosition.BottomRight;

            });
            
            
            AddService(builder);
             
            return builder;

        }
        private static void AddService(WebApplicationBuilder builder)
        {
            builder.Services.AddScoped<IProductService, ProductsService>();
            builder.Services.AddTransient<SeedDb>();


        }
        public static  WebApplication AddCustomWebApplicationConfiguration(this WebApplication app)
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
            service.SeedAsync().Wait();
        }


    }
}
