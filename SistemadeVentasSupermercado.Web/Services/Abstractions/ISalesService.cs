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

        /// <summary>Clientes por nombre o correo (máximo 10).</summary>
        Task<Response<List<PosClientDTO>>> SearchClientsAsync(string? term);

        /// <summary>Historial: la venta más reciente primero.</summary>
        Task<Response<PaginationResponse<SaleListItemDTO>>> GetPaginatedListAsync(PaginationRequest request);

        Task<Response<SaleDetailDTO>> GetOneAsync(Guid id);
    }
}
