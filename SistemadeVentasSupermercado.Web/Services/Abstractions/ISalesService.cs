using SistemadeVentasSupermercado.Web.Core;
using SistemadeVentasSupermercado.Web.Core.Pagination;
using SistemadeVentasSupermercado.Web.DTOs;

namespace SistemadeVentasSupermercado.Web.Services.Abstractions
{
    public interface ISaleService
    {
        // Operaciones básicas
        Task<Response<SaleDTO>> GetOneAsync(Guid id);
        Task<Response<List<SaleDTO>>> GetListAsync();
        Task<Response<PaginationResponse<SaleDTO>>> GetPaginatedListAsync(PaginationRequest request);

        // Operaciones POS
        Task<Response<SaleDTO>> CreateSaleAsync(CreateSaleDTO dto);
        Task<Response<SaleDTO>> CancelSaleAsync(CancelSaleDTO dto);

        // Consultas
        Task<Response<List<SaleDTO>>> GetSalesByDateRangeAsync(DateTime startDate, DateTime endDate);
        Task<Response<List<SaleDTO>>> GetSalesByCashRegisterAsync(Guid cashRegisterId);
        Task<Response<decimal>> GetDailySalesTotalAsync(DateTime date);

        // Funcionalidades adicionales
        Task<Response<ProductDTO>> GetProductByCodeAsync(string code);
        Task<Response<List<PaymentMethodDTO>>> GetActivePaymentMethodsAsync();
    }
}
