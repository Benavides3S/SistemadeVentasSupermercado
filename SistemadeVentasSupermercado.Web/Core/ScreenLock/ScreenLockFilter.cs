using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;

namespace SistemadeVentasSupermercado.Web.Core.ScreenLock
{
    /// <summary>
    /// Marca las acciones que funcionan con la pantalla bloqueada (bloquear, desbloquear, cerrar sesión).
    /// Tampoco cuentan como actividad.
    /// </summary>
    [AttributeUsage(AttributeTargets.Class | AttributeTargets.Method)]
    public class AllowWhenLockedAttribute : Attribute
    {
    }

    /// <summary>
    /// Filtro global: con la sesión bloqueada no se ejecuta ninguna acción. El bloqueo está en el
    /// servidor, así que quitar la capa de la pantalla o recargar no sirve.
    /// Una página se manda a la pantalla de desbloqueo; una petición de JavaScript recibe 423 (Locked).
    /// </summary>
    public class ScreenLockFilter : IAsyncAuthorizationFilter, IOrderedFilter
    {
        private readonly ScreenLockStore _store;

        public ScreenLockFilter(ScreenLockStore store) => _store = store;

        // Antes que los demás: bloqueada, no se revisa ni se hace nada más.
        public int Order => int.MinValue;

        public Task OnAuthorizationAsync(AuthorizationFilterContext context)
        {
            HttpContext http = context.HttpContext;
            string? sessionId = ScreenLockSession.CurrentId(http);

            if (sessionId is null
                || context.ActionDescriptor.EndpointMetadata.OfType<AllowWhenLockedAttribute>().Any()
                || _store.TryUse(sessionId))
                return Task.CompletedTask;

            HttpRequest request = http.Request;

            if (request.Headers.Accept.ToString().Contains("text/html", StringComparison.OrdinalIgnoreCase))
            {
                // Solo se puede volver a una página; un formulario enviado no se repite.
                string? returnUrl = HttpMethods.IsGet(request.Method)
                    ? $"{request.PathBase}{request.Path}{request.QueryString}"
                    : null;
                context.Result = new RedirectToActionResult("Locked", "Account", new { returnUrl });
            }
            else
            {
                context.Result = new StatusCodeResult(StatusCodes.Status423Locked);
            }

            return Task.CompletedTask;
        }
    }
}
