using SistemadeVentasSupermercado.Web.Core;
using SistemadeVentasSupermercado.Web.Core.Pagination;
using SistemadeVentasSupermercado.Web.DTOs;

namespace SistemadeVentasSupermercado.Web.Services.Abstractions
{
    public interface ICategoryService
    {
        Task<Response<CategoryDTO>> CreateAsync(CategoryDTO dto);
        Task<Response<object>> DeleteAsync(Guid id);
        Task<Response<CategoryDTO>> EditAsync(CategoryDTO dto);
        Task<Response<CategoryDTO>> GetOneAsync(Guid id);
        Task<Response<PaginationResponse<CategoryDTO>>> GetPaginatedListAsync(PaginationRequest request);
    }
}