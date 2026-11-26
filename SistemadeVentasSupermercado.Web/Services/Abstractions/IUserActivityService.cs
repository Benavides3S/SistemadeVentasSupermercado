using SistemadeVentasSupermercado.Web.Core;
using SistemadeVentasSupermercado.Web.Core.Pagination;
using SistemadeVentasSupermercado.Web.DTOs;

namespace SistemadeVentasSupermercado.Web.Services.Abstractions
{
    public interface IUserActivityService
    {
        Task<Response<PaginationResponse<UserActivityDTO>>> GetPaginatedListAsync(UserActivityQueryDTO request);
    }
}
