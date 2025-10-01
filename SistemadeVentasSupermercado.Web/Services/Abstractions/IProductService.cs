using SistemadeVentasSupermercado.Web.Core;
using SistemadeVentasSupermercado.Web.Core.Pagination;
using SistemadeVentasSupermercado.Web.DTOs;

namespace SistemadeVentasSupermercado.Web.Services.Abstractions
{
    public interface IProductService
    {

        // C - Create (Crear)
        public Task<Response<ProductDTO>> CreateAsync(ProductDTO dto);

        // D - Delete (Eliminar)
        public Task<Response<object>> DeleteAsync(Guid id);

        // U - Update (Actualizar/Editar)
        public Task<Response<ProductDTO>> EditAsync(ProductDTO dto);

        // R - Read (Leer lista)
        public Task<Response<List<ProductDTO>>> GetListAsync();

        // R - Read (Leer uno)
        public Task<Response<ProductDTO>> GetOneAsync(Guid id);
        Task<Response<PaginationResponse<ProductDTO>>> GetPaginatedListAsync(PaginationRequest request);

    }
}
