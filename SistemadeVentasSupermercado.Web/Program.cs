
using SistemadeVentasSupermercado.Web;
using Microsoft.AspNetCore.Mvc;
using System.Globalization;

WebApplicationBuilder builder = WebApplication.CreateBuilder(args);
// Los números se envían y leen con punto decimal (1.5).
CultureInfo.DefaultThreadCurrentCulture = CultureInfo.InvariantCulture;
CultureInfo.DefaultThreadCurrentUICulture = CultureInfo.InvariantCulture;

builder.Services.AddControllersWithViews(options =>
{
    options.Filters.Add(new AutoValidateAntiforgeryTokenAttribute());
    // Con contraseña temporal, toda página lleva a "Cambiar contraseña".
    options.Filters.Add<SistemadeVentasSupermercado.Web.Core.Filters.MustChangePasswordFilter>();
});

builder.AddCustomConfiguration();

WebApplication app = builder.Build();

if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Home/Error");
    app.UseHsts();
}

app.UseHttpsRedirection();
app.UseStaticFiles();

app.UseAuthentication();
app.UseRouting();
app.UseAuthorization();

app.MapControllerRoute(
    name: "default",
    pattern: "{controller=Home}/{action=Index}/{id?}");

app.AddCustomWebApplicationConfiguration();

app.Run();

// Necesario para que las pruebas (WebApplicationFactory) puedan arrancar la aplicación.
public partial class Program { }
