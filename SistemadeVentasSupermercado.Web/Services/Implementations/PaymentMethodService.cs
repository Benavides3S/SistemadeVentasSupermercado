using AutoMapper;
using Microsoft.EntityFrameworkCore;
using SistemadeVentasSupermercado.Web.Core;
using SistemadeVentasSupermercado.Web.Core.Pagination;
using SistemadeVentasSupermercado.Web.Data;
using SistemadeVentasSupermercado.Web.Data.Entities;
using SistemadeVentasSupermercado.Web.DTOs;
using SistemadeVentasSupermercado.Web.Services.Abstractions;

namespace SistemadeVentasSupermercado.Web.Services.Implementations
{
    public class PaymentMethodService : CustomQueryableOperationsService, IPaymentMethodService
    {
        private readonly DataContext _context;
        private readonly IMapper _mapper;

        public PaymentMethodService(DataContext context, IMapper mapper) : base(context, mapper)
        {
            _context = context;
            _mapper = mapper;
        }

        public async Task<Response<PaymentMethodDTO>> CreateAsync(PaymentMethodDTO dto)
        {
            return await CreateAsync<PaymentMethod, PaymentMethodDTO>(dto);
        }

        public async Task<Response<object>> DeleteAsync(Guid id)
        {
            return await DeleteAsync<PaymentMethod>(id);
        }

        public async Task<Response<PaymentMethodDTO>> EditAsync(PaymentMethodDTO dto)
        {
            return await EditAsync<PaymentMethod, PaymentMethodDTO>(dto, dto.Id);
        }

        public async Task<Response<List<PaymentMethodDTO>>> GetListAsync()
        {
            return await GetCompleteListAsync<PaymentMethod, PaymentMethodDTO>();
        }

        public async Task<Response<PaymentMethodDTO>> GetOneAsync(Guid id)
        {
            return await GetOneAsync<PaymentMethod, PaymentMethodDTO>(id);
        }

        public async Task<Response<PaginationResponse<PaymentMethodDTO>>> GetPaginatedListAsync(PaginationRequest request)
        {
            IQueryable<PaymentMethod> query = _context.PaymentMethods.AsQueryable();

            if (!string.IsNullOrWhiteSpace(request.Filter))
            {
                query = query.Where(p => p.Name.ToLower().Contains(request.Filter.ToLower())
                                         || (p.Description != null && p.Description.ToLower().Contains(request.Filter.ToLower())));
            }

            return await GetPaginationAsync<PaymentMethod, PaymentMethodDTO>(request, query);
        }

        public async Task<Response<List<PaymentMethodDTO>>> GetActivePaymentMethodsAsync()
        {
            try
            {
                List<PaymentMethod> paymentMethods = await _context.PaymentMethods
                    .Where(p => p.IsActive)
                    .OrderBy(p => p.Name)
                    .ToListAsync();

                List<PaymentMethodDTO> list = _mapper.Map<List<PaymentMethodDTO>>(paymentMethods);
                return Response<List<PaymentMethodDTO>>.Success(list);
            }
            catch (Exception ex)
            {
                return Response<List<PaymentMethodDTO>>.Failure(ex);
            }
        }
    }
}
