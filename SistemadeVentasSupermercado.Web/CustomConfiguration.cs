using Microsoft.EntityFrameworkCore;
using SistemadeVentasSupermercado.Web.Data;

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
            return builder;

        }
    }
}
