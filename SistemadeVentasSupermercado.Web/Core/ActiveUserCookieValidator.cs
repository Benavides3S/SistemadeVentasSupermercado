using System.Security.Claims;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using SistemadeVentasSupermercado.Web.Data;

namespace SistemadeVentasSupermercado.Web.Core
{
    /// <summary>
    /// Se ejecuta en CADA petición con cookie de sesión: si el usuario está desactivado (o ya no existe),
    /// la petición queda sin sesión. No depende del sello de seguridad: un usuario desactivado no pasa aunque
    /// su sello coincida. Después corre la revisión del sello. Al estar en la cookie cubre todas las acciones.
    /// </summary>
    public static class ActiveUserCookieValidator
    {
        public static async Task ValidateAsync(CookieValidatePrincipalContext context)
        {
            string? userId = context.Principal?.FindFirstValue(ClaimTypes.NameIdentifier);
            DataContext db = context.HttpContext.RequestServices.GetRequiredService<DataContext>();

            if (userId is null || !await db.Users.AnyAsync(u => u.Id == userId && u.DeactivatedAt == null))
            {
                context.RejectPrincipal();
                await context.HttpContext.SignOutAsync(IdentityConstants.ApplicationScheme);
                return;
            }

            // La revisión normal de Identity (la que ya había).
            await SecurityStampValidator.ValidatePrincipalAsync(context);
        }
    }
}
