using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using SistemadeVentasSupermercado.Web.Services.Abstractions;

namespace SistemadeVentasSupermercado.Web.Core.Filters
{
    /// <summary>
    /// Marca las acciones que se pueden usar aunque el usuario deba cambiar su contraseña
    /// (cambiarla, iniciar y cerrar sesión).
    /// </summary>
    [AttributeUsage(AttributeTargets.Class | AttributeTargets.Method)]
    public class SkipMustChangePasswordAttribute : Attribute
    {
    }

    /// <summary>
    /// Filtro global (Program.cs): si el usuario tiene una contraseña temporal, cualquier página
    /// lo lleva a "Cambiar contraseña". Es un filtro de autorización para que corra antes que los demás.
    /// </summary>
    public class MustChangePasswordFilter : IAsyncAuthorizationFilter
    {
        private readonly IUsersService _usersService;

        public MustChangePasswordFilter(IUsersService usersService)
        {
            _usersService = usersService;
        }

        public async Task OnAuthorizationAsync(AuthorizationFilterContext context)
        {
            string? userName = context.HttpContext.User.Identity?.IsAuthenticated == true
                ? context.HttpContext.User.Identity.Name
                : null;

            if (userName is null)
                return;

            if (context.ActionDescriptor.EndpointMetadata.OfType<SkipMustChangePasswordAttribute>().Any())
                return;

            if (await _usersService.MustChangePasswordAsync(userName))
                context.Result = new RedirectToActionResult("ChangePassword", "Account", null);
        }
    }
}
