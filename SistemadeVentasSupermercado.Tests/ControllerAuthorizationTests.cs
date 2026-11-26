using System.Reflection;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using SistemadeVentasSupermercado.Web.Core.Attributes;

namespace SistemadeVentasSupermercado.Tests
{
    /// <summary>
    /// Red de seguridad: ninguna acción de un controlador puede quedar sin decisión de seguridad.
    /// Cada acción debe llevar, en la propia acción, [CustomAuthorize] (permiso) o [AllowAnonymous].
    /// Excepciones: (1) las de SoloSesion, que solo exigen sesión iniciada (cualquier rol) y deben llevar
    /// [Authorize]; (2) las de PermisoDentroDeLaAccion, que revisan el permiso en el código y tienen pruebas
    /// HTTP propias. Una acción nueva sin atributo hace fallar esta prueba hasta que se proteja o se agregue
    /// a una de esas listas A PROPÓSITO.
    /// </summary>
    public class ControllerAuthorizationTests
    {
        // "Controlador.Acción": solo exigen estar logueado, sin permiso específico.
        private static readonly HashSet<string> SoloSesion =
        [
            "Home.Index",
            "Account.Logout",
            "Account.ChangePassword",
            "Account.UpdateUser",
            // Foto de perfil: cada quien solo la suya (la acción no recibe un usuario).
            "Account.Photo",
            "Account.UploadPhoto",
            "Account.RemovePhoto",
            // Bloqueo de pantalla: cualquiera con sesión, y solo sobre la suya.
            "Account.Lock",
            "Account.Activity",
            "Account.Unlock",
            "Account.Locked",
        ];

        // "Controlador.Acción": el controlador exige sesión y la acción revisa el permiso dentro del código,
        // porque necesita una combinación que un solo [CustomAuthorize] no expresa. Cada una tiene pruebas
        // HTTP en PermissionsHttpTests (permitidos y denegados con datos reales).
        private static readonly HashSet<string> PermisoDentroDeLaAccion =
        [
            "SaleReturns.Details",   // createReturns o showSales, o quien hizo la devolución
            "Account.AuthorizationPin", // quien tiene cancelSales o createReturns (SupervisorAuthorizationHttpTests)
            "CashSessions.Report",   // showCashSessions, o el dueño con el turno ya cerrado
        ];

        private static IEnumerable<(Type Controlador, MethodInfo Accion)> Acciones() =>
            typeof(Program).Assembly.GetTypes()
                .Where(t => typeof(ControllerBase).IsAssignableFrom(t) && !t.IsAbstract)
                .SelectMany(t => t.GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly)
                    .Where(m => !m.IsSpecialName && m.GetCustomAttribute<NonActionAttribute>() is null)
                    .Select(m => (t, m)));

        private static string Nombre(Type c, MethodInfo m) => $"{c.Name.Replace("Controller", "")}.{m.Name}";

        [Fact]
        public void TodaAccion_TieneCustomAuthorizeOAllowAnonymous()
        {
            List<string> sinProteccion = [];

            foreach ((Type controlador, MethodInfo accion) in Acciones())
            {
                string nombre = Nombre(controlador, accion);

                bool explicita = accion.GetCustomAttribute<CustomAuthorizeAttribute>() is not null
                              || accion.GetCustomAttribute<AllowAnonymousAttribute>() is not null;
                if (explicita) continue;

                bool soloSesion = SoloSesion.Contains(nombre)
                               && (accion.GetCustomAttribute<AuthorizeAttribute>() is not null
                                   || controlador.GetCustomAttribute<AuthorizeAttribute>() is not null);
                if (soloSesion) continue;

                // Debe exigir sesión (en el controlador o en la acción).
                bool permisoDentro = PermisoDentroDeLaAccion.Contains(nombre)
                                  && (accion.GetCustomAttribute<AuthorizeAttribute>() is not null
                                      || controlador.GetCustomAttribute<AuthorizeAttribute>() is not null);
                if (permisoDentro) continue;

                sinProteccion.Add(nombre);
            }

            Assert.True(sinProteccion.Count == 0,
                "Acciones sin [CustomAuthorize] ni [AllowAnonymous] explícito: " + string.Join(", ", sinProteccion.Order()));
        }

        [Fact]
        public void ListaSoloSesion_NoTieneEntradasViejas()
        {
            HashSet<string> existentes = Acciones().Select(a => Nombre(a.Controlador, a.Accion)).ToHashSet();

            string[] viejas = SoloSesion.Concat(PermisoDentroDeLaAccion).Where(n => !existentes.Contains(n)).ToArray();

            Assert.True(viejas.Length == 0, "Entradas de SoloSesion que ya no existen: " + string.Join(", ", viejas));
        }
    }
}
