using SistemadeVentasSupermercado.Web.Core;
using SistemadeVentasSupermercado.Web.Core.Pagination;
using SistemadeVentasSupermercado.Web.DTOs;

namespace SistemadeVentasSupermercado.Web.Services.Abstractions
{
    public interface IPaymentMethodService
    {
        Task<Response<PaymentMethodDTO>> CreateAsync(PaymentMethodDTO dto);
        Task<Response<object>> DeleteAsync(Guid id);
        Task<Response<PaymentMethodDTO>> EditAsync(PaymentMethodDTO dto);
        Task<Response<List<PaymentMethodDTO>>> GetListAsync();
        Task<Response<PaymentMethodDTO>> GetOneAsync(Guid id);
        Task<Response<PaginationResponse<PaymentMethodDTO>>> GetPaginatedListAsync(PaginationRequest request);
        Task<Response<List<PaymentMethodDTO>>> GetActivePaymentMethodsAsync();
    }


}

