using Microsoft.EntityFrameworkCore;
using SistemadeVentasSupermercado.Web.Data;
using SistemadeVentasSupermercado.Web.Services.Abstractions;
using SistemadeVentasSupermercado.Web.Services.Implementations;
using AutoMapper;
using AspNetCoreHero.ToastNotification;
using Microsoft.Identity.Client;
using AspNetCoreHero.ToastNotification.Extensions;
namespace SistemadeVentasSupermercado.Web
{
    public static class CustomConfiguration
    {
        public static WebApplicationBuilder AddCustomConfiguration(this WebApplicationBuilder builder)
        {
            // Data context 
            builder.Services.AddDbContext<DataContext>(options => {

                options.UseSqlServer(builder.Configuration.GetConnectionString("Myconnection"));
            
            });
            // AutoMapper 
             builder.Services.AddAutoMapper(typeof(Program));

            // tost notification setup
            builder.Services.AddNotyf(config =>
            {

                config.DurationInSeconds = 10;
                config.IsDismissable = true;
                config.Position = NotyfPosition.BottomRight;

            });
            
            // Servicios 
            AddService(builder);
             
            return builder;

        }
        private static void AddService(WebApplicationBuilder builder)
        {
            builder.Services.AddScoped<IProductService, ProductsService>();


        }
        public static  WebApplication AddCustomWebApplicationConfiguration(this WebApplication app)
        {
            app.UseNotyf();
            return app;
        }

    }
}
