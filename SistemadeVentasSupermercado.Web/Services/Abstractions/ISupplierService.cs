using SistemadeVentasSupermercado.Web.Core;
using SistemadeVentasSupermercado.Web.Core.Pagination;
using SistemadeVentasSupermercado.Web.DTOs;

namespace SistemadeVentasSupermercado.Web.Services.Abstractions
{
    public interface ISupplierService
    {
        Task<Response<SupplierDTO>> CreateAsync(SupplierDTO dto);
        Task<Response<object>> DeleteAsync(Guid id);
        Task<Response<SupplierDTO>> EditAsync(SupplierDTO dto);
        Task<Response<SupplierDTO>> GetOneAsync(Guid id);
        Task<Response<PaginationResponse<SupplierDTO>>> GetPaginatedListAsync(PaginationRequest request);
    }
}
