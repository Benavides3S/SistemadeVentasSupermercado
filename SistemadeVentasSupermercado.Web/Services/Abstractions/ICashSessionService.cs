using SistemadeVentasSupermercado.Web.Core;
using SistemadeVentasSupermercado.Web.Core.Pagination;
using SistemadeVentasSupermercado.Web.DTOs;

namespace SistemadeVentasSupermercado.Web.Services.Abstractions
{
    public interface ICashSessionService
    {
        /// <summary>Turno abierto del usuario; Result es null si no tiene caja abierta.</summary>
        Task<Response<CashSessionDTO?>> GetCurrentAsync(string userId);

        Task<Response<CashSessionDTO>> OpenAsync(string userId, OpenCashSessionDTO dto);

        /// <summary>
        /// Cierra un turno y guarda el arqueo. Si quien cierra no es el dueño del turno,
        /// solo se permite cuando canCloseOthers es true (supervisor).
        /// </summary>
        Task<Response<CashSessionDTO>> CloseAsync(Guid sessionId, CloseCashSessionDTO dto, string closedByUserId, bool canCloseOthers);

        Task<Response<CashSessionSummaryDTO>> GetSummaryAsync(Guid sessionId);

        /// <summary>Lista de turnos: el más reciente primero.</summary>
        Task<Response<PaginationResponse<CashSessionDTO>>> GetPaginatedListAsync(PaginationRequest request);
    }
}
