using SistemadeVentasSupermercado.Web.Core;
using SistemadeVentasSupermercado.Web.Core.Pagination;
using SistemadeVentasSupermercado.Web.DTOs;

namespace SistemadeVentasSupermercado.Web.Services.Abstractions
{
    public interface IProductService
    {

        // C - Create (Crear)
        /// <param name="userId">Quién lo crea: queda en el movimiento de "Stock inicial" del historial de inventario.</param>
        public Task<Response<ProductDTO>> CreateAsync(ProductDTO dto, string? userId = null);

        // D - Delete (Eliminar)
        public Task<Response<object>> DeleteAsync(Guid id);

        // U - Update (Actualizar/Editar)
        public Task<Response<ProductDTO>> EditAsync(ProductDTO dto, string? userId = null);

        // R - Read (Leer lista)
        public Task<Response<List<ProductDTO>>> GetListAsync();

        // R - Read (Leer uno)
        public Task<Response<ProductDTO>> GetOneAsync(Guid id);
        Task<Response<PaginationResponse<ProductDTO>>> GetPaginatedListAsync(PaginationRequest request);

    }
}
