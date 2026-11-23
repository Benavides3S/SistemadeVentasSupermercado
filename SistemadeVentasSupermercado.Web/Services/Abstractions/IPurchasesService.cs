using SistemadeVentasSupermercado.Web.Core;
using SistemadeVentasSupermercado.Web.DTOs;

namespace SistemadeVentasSupermercado.Web.Services.Abstractions
{
    public interface IPurchasesService
    {
        /// <summary>
        /// Registra una compra: suma el stock de cada producto y guarda su último costo, todo o nada.
        /// Un reintento con el mismo <see cref="CreatePurchaseDTO.IdempotencyKey"/> no suma dos veces.
        /// </summary>
        Task<Response<PurchaseResultDTO>> CreateAsync(CreatePurchaseDTO dto, string userId);

        /// <summary>
        /// Anula una compra completada y resta del inventario lo que sumó, todo o nada: solo se puede
        /// si el stock de CADA producto todavía alcanza (si parte ya se vendió, se rechaza y no cambia nada).
        /// No tiene límite de tiempo. El último costo de los productos no se restaura.
        /// </summary>
        Task<Response<PurchaseResultDTO>> CancelAsync(Guid purchaseId, CancelPurchaseDTO dto, string userId);
    }
}
