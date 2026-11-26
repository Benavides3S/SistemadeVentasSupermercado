using Microsoft.AspNetCore.Authentication;

namespace SistemadeVentasSupermercado.Web.Core.ScreenLock
{
    /// <summary>El Id de sesión que viaja (firmado y cifrado) en la cookie de inicio de sesión.</summary>
    public static class ScreenLockSession
    {
        private const string PropertyKey = "sv.sesion";
        private const string ItemKey = "sv.sesion";

        /// <summary>
        /// Al crear la cookie. Si ya trae Id (cambio de contraseña: RefreshSignInAsync conserva las
        /// propiedades) se mantiene su estado.
        /// </summary>
        public static void Assign(HttpContext http, AuthenticationProperties properties)
        {
            if (properties.Items.ContainsKey(PropertyKey))
                return;

            string id = Guid.NewGuid().ToString("N");
            properties.Items[PropertyKey] = id;
            http.RequestServices.GetRequiredService<ScreenLockStore>().Start(id);
        }

        /// <summary>Al validar la cookie en cada petición. False si la cookie no trae Id.</summary>
        public static bool Remember(HttpContext http, AuthenticationProperties properties)
        {
            if (!properties.Items.TryGetValue(PropertyKey, out string? id) || string.IsNullOrEmpty(id))
                return false;

            http.Items[ItemKey] = id;
            return true;
        }

        /// <summary>Id de la sesión de esta petición (null si no hay sesión).</summary>
        public static string? CurrentId(HttpContext http) => http.Items[ItemKey] as string;
    }
}
