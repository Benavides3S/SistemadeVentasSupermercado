using SistemadeVentasSupermercado.Web.Core;
using SistemadeVentasSupermercado.Web.Core.Pagination;
using SistemadeVentasSupermercado.Web.DTOs;

namespace SistemadeVentasSupermercado.Web.Services.Abstractions
{
    public interface IDiscountService
    {
        Task<Response<DiscountDTO>> CreateAsync(DiscountDTO dto);
        Task<Response<object>> DeleteAsync(Guid id);
        Task<Response<DiscountDTO>> EditAsync(DiscountDTO dto);
        Task<Response<List<DiscountDTO>>> GetListAsync();
        Task<Response<DiscountDTO>> GetOneAsync(Guid id);
        Task<Response<PaginationResponse<DiscountDTO>>> GetPaginatedListAsync(PaginationRequest request);
        Task<Response<List<DiscountDTO>>> GetActiveDiscountsAsync();
    }
}
