using SistemadeVentasSupermercado.Web.Core;
using SistemadeVentasSupermercado.Web.DTOs;

namespace SistemadeVentasSupermercado.Web.Services.Abstractions
{
    public interface ISalesService
    {
        Task<Response<SaleResultDTO>> CreateAsync(CreateSaleDTO dto, string userId);
    }
}