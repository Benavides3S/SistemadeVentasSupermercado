using SistemadeVentasSupermercado.Web.Core;
using SistemadeVentasSupermercado.Web.Core.Pagination;
using SistemadeVentasSupermercado.Web.DTOs;


namespace SistemadeVentasSupermercado.Web.Services.Abstractions
{
    public interface IRolesService
    {
        public Task<Response<SistemaVentaRoleDTO>> CreateAsync(SistemaVentaRoleDTO dto);
        public Task<Response<object>> DeleteAsync(Guid id);
        public Task<Response<SistemaVentaRoleDTO>> EditAsync(SistemaVentaRoleDTO dto);
        public Task<Response<SistemaVentaRoleDTO>> GetOneAsync(Guid id);
        public Task<Response<PaginationResponse<SistemaVentaRoleDTO>>> GetPaginatedListAsync(PaginationRequest request);
        public Task<Response<List<PermissionsForRoleDTO>>> GetPermissionsAsync();
    }
}

