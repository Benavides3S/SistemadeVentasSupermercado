using SistemadeVentasSupermercado.Web.Core;
using SistemadeVentasSupermercado.Web.DTOs;

namespace SistemadeVentasSupermercado.Web.Services.Abstractions
{
    public interface IProductService
    {

        public Task<Response<ProductDTO>> CreateAsync(ProductDTO dto);
        public Task<Response<List<ProductDTO>>> GetListAsync();

    }
}
