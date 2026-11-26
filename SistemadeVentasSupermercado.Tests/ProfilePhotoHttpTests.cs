using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using SistemadeVentasSupermercado.Tests.Infraestructura;
using SistemadeVentasSupermercado.Web.Core;
using SistemadeVentasSupermercado.Web.Data;
using SistemadeVentasSupermercado.Web.Data.Entities;

namespace SistemadeVentasSupermercado.Tests
{
    /// <summary>
    /// Foto de perfil con la aplicación real. Cada prueba crea sus propios usuarios. El límite del cuerpo
    /// completo de la petición lo aplica Kestrel, que aquí no existe: se prueba aparte, con la app arrancada.
    /// </summary>
    [Collection(ColeccionAppWeb.Nombre)]
    public class ProfilePhotoHttpTests
    {
        private readonly AppWebFixture _app;

        public ProfilePhotoHttpTests(AppWebFixture app) => _app = app;

        [Fact]
        public async Task SubirYVerLaPropia_SinCache_YQuedaEnElHistorial()
        {
            (User usuario, HttpClient cliente, string token) = await EntrarComoNuevoAsync();
            byte[] foto = FotosDePrueba.Jpg();

            Assert.Equal(HttpStatusCode.NotFound, (await cliente.GetAsync("/Account/Photo")).StatusCode);

            (bool ok, _) = await SubirAsync(cliente, token, foto);
            Assert.True(ok);

            HttpResponseMessage respuesta = await cliente.GetAsync("/Account/Photo");
            Assert.Equal(HttpStatusCode.OK, respuesta.StatusCode);
            Assert.Equal(foto, await respuesta.Content.ReadAsByteArrayAsync());
            Assert.Equal("image/jpeg", respuesta.Content.Headers.ContentType!.MediaType);
            Assert.True(respuesta.Headers.CacheControl!.NoStore);
            Assert.Equal("nosniff", Assert.Single(respuesta.Headers.GetValues("X-Content-Type-Options")));

            UserActivity registro = Assert.Single(await HistorialAsync(usuario.Id));
            Assert.Equal(UserActivityType.ProfileUpdated, registro.Type);
            Assert.Equal("Cambió: Foto de perfil", registro.Details);
        }

        [Fact]
        public async Task CadaUsuarioSoloRecibeLaSuya_AunquePidaLaDeOtro()
        {
            (User uno, HttpClient clienteUno, string tokenUno) = await EntrarComoNuevoAsync();
            (User dos, HttpClient clienteDos, string tokenDos) = await EntrarComoNuevoAsync();
            byte[] fotoUno = FotosDePrueba.Jpg(pixel: 0x21);
            byte[] fotoDos = FotosDePrueba.Jpg(pixel: 0x42);

            Assert.True((await SubirAsync(clienteUno, tokenUno, fotoUno)).Ok);

            // El segundo no tiene foto: no recibe la del primero, ni pidiéndola por su Id.
            Assert.Equal(HttpStatusCode.NotFound,
                (await clienteDos.GetAsync($"/Account/Photo?userId={uno.Id}&userName={uno.UserName}")).StatusCode);

            // Y un formulario manipulado con el Id del primero solo cambia la propia.
            Assert.True((await SubirAsync(clienteDos, tokenDos, fotoDos,
                extra: [("userId", uno.Id), ("userName", uno.UserName!)])).Ok);

            Assert.Equal(fotoUno, await clienteUno.GetByteArrayAsync("/Account/Photo"));
            Assert.Equal(fotoDos, await clienteDos.GetByteArrayAsync($"/Account/Photo?id={uno.Id}"));
        }

        [Theory]
        [InlineData("dibujo.svg", "image/svg+xml")]
        [InlineData("dibujo.jpg", "image/jpeg")] // el nombre y el tipo declarados no importan
        public Task UnSvg_SeRechaza(string nombre, string tipo)
            => AssertRechazaAsync(FotosDePrueba.Svg, nombre, tipo, ProfilePhotoRules.NotJpegMessage);

        [Fact]
        public Task UnArchivoQueNoEsImagen_ConExtensionJpg_SeRechaza()
            => AssertRechazaAsync("MZ esto no es una imagen"u8.ToArray(), "foto.jpg", "image/jpeg",
                ProfilePhotoRules.NotJpegMessage);

        [Fact]
        public Task UnaImagenDemasiadoPesada_SeRechaza()
            => AssertRechazaAsync(FotosDePrueba.Jpg(bytesDeDatos: ProfilePhotoRules.MaxBytes), "foto.jpg", "image/jpeg",
                ProfilePhotoRules.TooLargeMessage);

        [Fact]
        public Task UnaPeticionMuchoMasGrande_SeRechaza()
            => AssertRechazaAsync(new byte[2 * 1024 * 1024], "foto.jpg", "image/jpeg",
                "No llegó ninguna foto, o es demasiado grande.");

        [Fact]
        public Task UnaImagenDeDemasiadosPixeles_SeRechaza()
            => AssertRechazaAsync(FotosDePrueba.Jpg(4000, 4000), "foto.jpg", "image/jpeg",
                ProfilePhotoRules.DimensionsMessage);

        [Fact]
        public async Task Quitar_BorraSoloLaPropia_YQuedaEnElHistorial()
        {
            (User uno, HttpClient clienteUno, string tokenUno) = await EntrarComoNuevoAsync();
            (User dos, HttpClient clienteDos, string tokenDos) = await EntrarComoNuevoAsync();
            await SubirAsync(clienteUno, tokenUno, FotosDePrueba.Jpg());
            await SubirAsync(clienteDos, tokenDos, FotosDePrueba.Jpg());

            HttpResponseMessage respuesta = await clienteUno.PostAsync("/Account/RemovePhoto", new FormUrlEncodedContent(
            [
                new("userId", dos.Id),
                new("__RequestVerificationToken", tokenUno)
            ]));

            Assert.Equal(HttpStatusCode.Redirect, respuesta.StatusCode);
            Assert.False(await TieneFotoAsync(uno.Id));
            Assert.True(await TieneFotoAsync(dos.Id));
            Assert.Contains(await HistorialAsync(uno.Id), a => a.Details == "Quitó la foto de perfil");
        }

        [Fact]
        public async Task SinTokenAntiforgery_NoSeGuarda()
        {
            (User usuario, HttpClient cliente, _) = await EntrarComoNuevoAsync();

            HttpResponseMessage respuesta = await cliente.PostAsync("/Account/UploadPhoto",
                Formulario(FotosDePrueba.Jpg(), "foto.jpg", "image/jpeg"));

            Assert.Equal(HttpStatusCode.BadRequest, respuesta.StatusCode);
            Assert.False(await TieneFotoAsync(usuario.Id));
        }

        [Fact]
        public async Task SinSesion_MandaAlLogin()
        {
            using HttpClient cliente = _app.CrearCliente();

            AyudasDeSesion.AssertMandaAlLogin(await cliente.GetAsync("/Account/Photo"), "Ver la foto");
        }

        [Fact]
        public async Task AlEliminarElUsuario_SuFotoSeBorraConEl()
        {
            // La aplicación desactiva usuarios, no los borra. Esto protege un borrado directo.
            User usuario = await CrearUsuarioSinHistorialAsync();

            using IServiceScope scope = _app.Services.CreateScope();
            DataContext db = scope.ServiceProvider.GetRequiredService<DataContext>();
            db.UserPhotos.Add(new UserPhoto { UserId = usuario.Id, Content = FotosDePrueba.Jpg(), UpdatedAtUtc = DateTime.UtcNow });
            await db.SaveChangesAsync();

            // SQL directo: así borra la base de datos, no Entity Framework.
            int borrados = await db.Database.ExecuteSqlInterpolatedAsync($"DELETE FROM AspNetUsers WHERE Id = {usuario.Id}");

            Assert.True(borrados > 0);
            Assert.False(await TieneFotoAsync(usuario.Id));
        }

        // ---------- Herramientas ----------

        private async Task AssertRechazaAsync(byte[] archivo, string nombre, string tipo, string motivo)
        {
            (User usuario, HttpClient cliente, string token) = await EntrarComoNuevoAsync();

            (bool ok, string mensaje) = await SubirAsync(cliente, token, archivo, nombre, tipo);

            Assert.False(ok);
            Assert.Equal(motivo, mensaje);
            Assert.False(await TieneFotoAsync(usuario.Id));
            Assert.Empty(await HistorialAsync(usuario.Id));
        }

        private async Task<(User, HttpClient, string)> EntrarComoNuevoAsync()
        {
            (User usuario, _) = await _app.CrearCajeroConCajaAsync();
            HttpClient cliente = await _app.CrearClienteConSesionAsync(usuario.Email!);
            string token = AppWebFixture.ExtraerToken(await cliente.GetStringAsync("/Account/UpdateUser"));
            return (usuario, cliente, token);
        }

        private async Task<User> CrearUsuarioSinHistorialAsync()
        {
            using IServiceScope scope = _app.Services.CreateScope();
            DataContext db = scope.ServiceProvider.GetRequiredService<DataContext>();
            string marca = Guid.NewGuid().ToString("N")[..8];

            User usuario = new()
            {
                UserName = $"borrable-{marca}@pruebas.local",
                Email = $"borrable-{marca}@pruebas.local",
                Document = $"BORRABLE-{marca}",
                FirstName = "Borrable",
                LastName = marca,
                SistemaVentasRoleId = await db.SistemaVentasRoles.Select(r => r.Id).FirstAsync()
            };

            IdentityResult resultado = await scope.ServiceProvider.GetRequiredService<UserManager<User>>()
                .CreateAsync(usuario, _app.Clave);
            Assert.True(resultado.Succeeded);
            return usuario;
        }

        private static MultipartFormDataContent Formulario(byte[] archivo, string nombre, string tipo)
        {
            ByteArrayContent parte = new(archivo);
            parte.Headers.ContentType = new MediaTypeHeaderValue(tipo);
            return new MultipartFormDataContent { { parte, "photo", nombre } };
        }

        /// <summary>Igual que profile-photo.js: formulario con archivo y el token en el encabezado.</summary>
        private static async Task<(bool Ok, string Mensaje)> SubirAsync(HttpClient cliente, string token, byte[] archivo,
            string nombre = "foto.jpg", string tipo = "image/jpeg", (string, string)[]? extra = null)
        {
            MultipartFormDataContent formulario = Formulario(archivo, nombre, tipo);
            foreach ((string campo, string valor) in extra ?? [])
                formulario.Add(new StringContent(valor), campo);

            HttpRequestMessage peticion = new(HttpMethod.Post, "/Account/UploadPhoto") { Content = formulario };
            peticion.Headers.Add("RequestVerificationToken", token);

            HttpResponseMessage respuesta = await cliente.SendAsync(peticion);
            Assert.Equal(HttpStatusCode.OK, respuesta.StatusCode);

            using JsonDocument json = JsonDocument.Parse(await respuesta.Content.ReadAsStringAsync());
            return (json.RootElement.GetProperty("isSuccess").GetBoolean(),
                json.RootElement.GetProperty("message").GetString()!);
        }

        private async Task<bool> TieneFotoAsync(string userId)
        {
            using IServiceScope scope = _app.Services.CreateScope();
            return await scope.ServiceProvider.GetRequiredService<DataContext>().UserPhotos.AnyAsync(p => p.UserId == userId);
        }

        private async Task<List<UserActivity>> HistorialAsync(string userId)
        {
            using IServiceScope scope = _app.Services.CreateScope();
            return await scope.ServiceProvider.GetRequiredService<DataContext>().UserActivities.AsNoTracking()
                .Where(a => a.TargetUserId == userId).ToListAsync();
        }
    }
}
