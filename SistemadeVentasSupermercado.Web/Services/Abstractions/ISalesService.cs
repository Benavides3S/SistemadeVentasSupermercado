using SistemadeVentasSupermercado.Web.Core;
using SistemadeVentasSupermercado.Web.Core.Pagination;
using SistemadeVentasSupermercado.Web.DTOs;

namespace SistemadeVentasSupermercado.Web.Services.Abstractions
{
    public interface ISalesService
    {
        Task<Response<SaleResultDTO>> CreateAsync(CreateSaleDTO dto, string userId);

        /// <summary>Productos vendibles: con stock y de una categoría activa (máximo 20).</summary>
        Task<Response<List<PosProductDTO>>> SearchProductsAsync(string? term);

        /// <summary>Busca el código de barras exacto (lector del punto de venta). El mensaje explica si no se puede vender.</summary>
        Task<Response<BarcodeLookupDTO>> FindByBarcodeAsync(string? code);

        /// <summary>Clientes por nombre o correo (máximo 10).</summary>
        Task<Response<List<PosClientDTO>>> SearchClientsAsync(string? term);

        /// <summary>Historial: la venta más reciente primero.</summary>
        Task<Response<PaginationResponse<SaleListItemDTO>>> GetPaginatedListAsync(PaginationRequest request);

        Task<Response<SaleDetailDTO>> GetOneAsync(Guid id);

        /// <summary>
        /// Anula una venta completada y devuelve su stock. Solo mientras el turno de caja
        /// de la venta siga abierto (después se usa una devolución).
        /// authorizedByUserId: el supervisor cuyo PIN YA se comprobó (null si quien anula tiene el permiso).
        /// Nunca sale de un dato del navegador sin comprobar.
        /// </summary>
        Task<Response<SaleDetailDTO>> CancelAsync(Guid saleId, CancelSaleDTO dto, string userId,
            string? authorizedByUserId = null);
    }
}
