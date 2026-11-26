using SistemadeVentasSupermercado.Web.Core;
using SistemadeVentasSupermercado.Web.DTOs;

namespace SistemadeVentasSupermercado.Web.Services.Abstractions
{
    /// <summary>PIN de autorización de supervisor.</summary>
    public interface IAuthorizationPinService
    {
        /// <summary>Verdadero si su rol le permite autorizar alguna operación (y por tanto tener PIN).</summary>
        Task<bool> CanHavePinAsync(string userName);

        Task<bool> HasPinAsync(string userName);

        /// <summary>Crea o cambia el PIN propio. Pide la contraseña; el usuario sale SIEMPRE de la sesión.</summary>
        Task<Response<bool>> SetPinAsync(string userName, SetAuthorizationPinDTO dto);

        /// <summary>Quiénes pueden autorizar esta operación ahora mismo (y ya tienen PIN), sin quien la pide.</summary>
        Task<List<AuthorizerDTO>> GetAuthorizersAsync(SupervisorOperation operation, string requesterUserName);

        /// <summary>
        /// Comprueba el PIN de quien autoriza. Hay que llamarlo ANTES de abrir la transacción de la
        /// operación: así el contador de errores queda guardado aunque la operación se deshaga.
        /// </summary>
        Task<Response<AuthorizerDTO>> VerifyAsync(SupervisorOperation operation, string? authorizerUserId,
            string? pin, string requesterUserName);
    }
}
