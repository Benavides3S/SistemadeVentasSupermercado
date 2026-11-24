using SistemadeVentasSupermercado.Web.Core;
using SistemadeVentasSupermercado.Web.Core.Pagination;
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

        /// <summary>Historial: la compra más reciente primero. El filtro busca por número, proveedor, factura o usuario.</summary>
        Task<Response<PaginationResponse<PurchaseListItemDTO>>> GetPaginatedListAsync(PaginationRequest request);

        Task<Response<PurchaseDetailDTO>> GetOneAsync(Guid id);

        /// <summary>
        /// Productos para comprar, por nombre o código de barras (máximo 20). A diferencia del punto de venta,
        /// incluye productos sin stock y de categorías desactivadas.
        /// </summary>
        Task<Response<List<PurchaseProductDTO>>> SearchProductsAsync(string? term);

        /// <summary>
        /// Busca el código de barras exacto (lector de la pantalla de compras). Si no existe devuelve éxito
        /// con <c>Result</c> vacío y un mensaje que lo explica.
        /// </summary>
        Task<Response<PurchaseProductDTO?>> FindProductByBarcodeAsync(string? code);
    }
}
