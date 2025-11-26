using SistemadeVentasSupermercado.Web.Core;
using SistemadeVentasSupermercado.Web.Core.Pagination;
using SistemadeVentasSupermercado.Web.DTOs;

namespace SistemadeVentasSupermercado.Web.Services.Abstractions
{
    public interface ICashRegisterService
    {
        // Operaciones básicas
        Task<Response<CashRegisterDTO>> GetOneAsync(Guid id);
        Task<Response<List<CashRegisterDTO>>> GetListAsync();
        Task<Response<PaginationResponse<CashRegisterDTO>>> GetPaginatedListAsync(PaginationRequest request);

        // Operaciones específicas de caja
        Task<Response<CashRegisterDTO>> OpenCashRegisterAsync(OpenCashRegisterDTO dto);
        Task<Response<CashRegisterDTO>> CloseCashRegisterAsync(CloseCashRegisterDTO dto);
        Task<Response<CashRegisterDTO>> GetCurrentCashRegisterAsync();
        Task<Response<bool>> HasOpenCashRegisterAsync();
        Task<Response<CashRegisterDTO>> GetCashRegisterSummaryAsync(Guid id);

        // Reportes
        Task<Response<CashRegisterDTO>> GetDailySummaryAsync(DateTime date);
        Task<Response<List<CashRegisterDTO>>> GetCashRegistersByDateRangeAsync(DateTime startDate, DateTime endDate);
    }
}