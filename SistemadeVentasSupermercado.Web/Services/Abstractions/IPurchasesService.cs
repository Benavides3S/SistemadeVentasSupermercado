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
    }
}
