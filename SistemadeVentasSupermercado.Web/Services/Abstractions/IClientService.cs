using SistemadeVentasSupermercado.Web.Core;
using SistemadeVentasSupermercado.Web.Core.Pagination;
using SistemadeVentasSupermercado.Web.DTOs;

namespace SistemadeVentasSupermercado.Web.Services.Abstractions
{
    public interface IClientService
    {
        Task<Response<ClientDTO>> CreateAsync(ClientDTO dto);
        Task<Response<object>> DeleteAsync(Guid id);
        Task<Response<ClientDTO>> EditAsync(ClientDTO dto);
        Task<Response<List<ClientDTO>>> GetListAsync();
        Task<Response<ClientDTO>> GetOneAsync(Guid id);
        Task<Response<PaginationResponse<ClientDTO>>> GetPaginatedListAsync(PaginationRequest request);
    }
}
