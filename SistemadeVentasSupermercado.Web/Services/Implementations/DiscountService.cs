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
    public class DiscountService : CustomQueryableOperationsService, IDiscountService
    {
        private readonly DataContext _context;
        private readonly IMapper _mapper;

        public DiscountService(DataContext context, IMapper mapper) : base(context, mapper)
        {
            _context = context;
            _mapper = mapper;
        }

        public async Task<Response<DiscountDTO>> CreateAsync(DiscountDTO dto)
        {
            return await CreateAsync<Discount, DiscountDTO>(dto);
        }

        public async Task<Response<object>> DeleteAsync(Guid id)
        {
            return await DeleteAsync<Discount>(id);
        }

        public async Task<Response<DiscountDTO>> EditAsync(DiscountDTO dto)
        {
            return await EditAsync<Discount, DiscountDTO>(dto, dto.Id);
        }

        public async Task<Response<List<DiscountDTO>>> GetListAsync()
        {
            return await GetCompleteListAsync<Discount, DiscountDTO>();
        }

        public async Task<Response<DiscountDTO>> GetOneAsync(Guid id)
        {
            return await GetOneAsync<Discount, DiscountDTO>(id);
        }

        public async Task<Response<PaginationResponse<DiscountDTO>>> GetPaginatedListAsync(PaginationRequest request)
        {
            IQueryable<Discount> query = _context.Discounts.AsQueryable();

            if (!string.IsNullOrWhiteSpace(request.Filter))
            {
                query = query.Where(d => d.Name.ToLower().Contains(request.Filter.ToLower())
                                         || (d.Description != null && d.Description.ToLower().Contains(request.Filter.ToLower())));
            }

            return await GetPaginationAsync<Discount, DiscountDTO>(request, query);
        }

        public async Task<Response<List<DiscountDTO>>> GetActiveDiscountsAsync()
        {
            try
            {
                var now = DateTime.Now;
                List<Discount> discounts = await _context.Discounts
                    .Where(d => d.IsActive &&
                               (!d.StartDate.HasValue || d.StartDate <= now) &&
                               (!d.EndDate.HasValue || d.EndDate >= now))
                    .OrderBy(d => d.Name)
                    .ToListAsync();

                List<DiscountDTO> list = _mapper.Map<List<DiscountDTO>>(discounts);
                return Response<List<DiscountDTO>>.Success(list);
            }
            catch (Exception ex)
            {
                return Response<List<DiscountDTO>>.Failure(ex);
            }
        }
    }
}
