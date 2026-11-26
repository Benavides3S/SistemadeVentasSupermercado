using Microsoft.EntityFrameworkCore;
using SistemadeVentasSupermercado.Web.Data;
using SistemadeVentasSupermercado.Web.Data.Entities;
using SistemadeVentasSupermercado.Web.Services.Abstractions;

namespace SistemadeVentasSupermercado.Web.Services
{
    /// <summary>
    /// Deja en el historial "quién autorizó qué a quién". Se llama dentro de la transacción de la operación
    /// (anulación o devolución): quedan la operación y su registro, o ninguno.
    /// </summary>
    public static class SupervisorAuthorizationLog
    {
        public static async Task StageAsync(DataContext context, IUserActivityRecorder? recorder,
            string authorizerUserId, string executorUserId, string details)
        {
            // Sin historial no se acepta una operación autorizada.
            if (recorder is null)
                throw new InvalidOperationException("Falta el registro de actividad para una operación autorizada.");

            string authorizerName = await context.Users
                .Where(u => u.Id == authorizerUserId)
                .Select(u => u.UserName!)
                .SingleAsync();
            User executor = await context.Users.AsNoTracking().SingleAsync(u => u.Id == executorUserId);

            // "Quién" es quien autorizó; "Sobre" es quien hizo la operación en su sesión.
            await recorder.StageAsync(UserActivityType.SupervisorAuthorizationGranted, authorizerName, executor,
                details: details);
        }
    }
}
