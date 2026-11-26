using System.Net;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using SistemadeVentasSupermercado.Tests.Infraestructura;
using SistemadeVentasSupermercado.Web.Data;
using SistemadeVentasSupermercado.Web.Data.Entities;

namespace SistemadeVentasSupermercado.Tests
{
    /// <summary>
    /// "Mi perfil" con la aplicación real: a quién se edita lo decide la sesión, nunca el formulario.
    /// </summary>
    [Collection(ColeccionAppWeb.Nombre)]
    public class ProfileHttpTests
    {
        private readonly AppWebFixture _app;

        public ProfileHttpTests(AppWebFixture app) => _app = app;

        [Fact]
        public async Task UpdateUser_ConElIdDeOtroUsuario_SoloCambiaElPerfilPropio()
        {
            User adminAntes = await LeerUsuarioAsync(AppWebFixture.Admin);
            User propioAntes = await LeerUsuarioAsync(AppWebFixture.SinPermisos);

            using HttpClient cliente = await _app.CrearClienteConSesionAsync(AppWebFixture.SinPermisos);
            string token = AppWebFixture.ExtraerToken(await cliente.GetStringAsync("/Account/UpdateUser"));

            // Formulario manipulado: trae el Id y el correo del Admin.
            HttpResponseMessage respuesta = await cliente.PostAsync("/Account/UpdateUser", new FormUrlEncodedContent(
            [
                new("Id", adminAntes.Id),
                new("Email", AppWebFixture.Admin),
                new("Document", "PERFIL-123"),
                new("FirstName", "Nombre propio"),
                new("LastName", "Apellido propio"),
                new("PhoneNumber", "3001234567"),
                new("__RequestVerificationToken", token)
            ]));

            Assert.Equal(HttpStatusCode.Redirect, respuesta.StatusCode);

            User adminDespues = await LeerUsuarioAsync(AppWebFixture.Admin);
            Assert.Equal(adminAntes.Document, adminDespues.Document);
            Assert.Equal(adminAntes.FirstName, adminDespues.FirstName);
            Assert.Equal(adminAntes.LastName, adminDespues.LastName);
            Assert.Equal(adminAntes.PhoneNumber, adminDespues.PhoneNumber);

            User propioDespues = await LeerUsuarioAsync(AppWebFixture.SinPermisos);
            Assert.Equal("PERFIL-123", propioDespues.Document);
            Assert.Equal("Nombre propio", propioDespues.FirstName);
            Assert.Equal("Apellido propio", propioDespues.LastName);
            Assert.Equal("3001234567", propioDespues.PhoneNumber);
            // El formulario no cambia el correo ni el rol.
            Assert.Equal(propioAntes.Email, propioDespues.Email);
            Assert.Equal(propioAntes.SistemaVentasRoleId, propioDespues.SistemaVentasRoleId);
        }

        private async Task<User> LeerUsuarioAsync(string correo)
        {
            using IServiceScope scope = _app.Services.CreateScope();
            DataContext db = scope.ServiceProvider.GetRequiredService<DataContext>();
            return await db.Users.AsNoTracking().SingleAsync(u => u.UserName == correo);
        }
    }
}
