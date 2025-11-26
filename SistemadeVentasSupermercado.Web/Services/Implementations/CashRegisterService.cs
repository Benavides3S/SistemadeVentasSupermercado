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
    public class CashRegisterService : CustomQueryableOperationsService, ICashRegisterService
    {
        private readonly DataContext _context;
        private readonly IMapper _mapper;
        private readonly IUsersService _usersService;

        public CashRegisterService(DataContext context, IMapper mapper, IUsersService usersService)
            : base(context, mapper)
        {
            _context = context;
            _mapper = mapper;
            _usersService = usersService;
        }

        public async Task<Response<CashRegisterDTO>> OpenCashRegisterAsync(OpenCashRegisterDTO dto)
        {
            try
            {
                // Verificar si ya existe una caja abierta para el usuario
                var currentUser = await _usersService.GetCurrentUserAsync();
                if (!currentUser.IsSuccess)
                    return Response<CashRegisterDTO>.Failure("Usuario no autenticado");

                bool hasOpenCashRegister = await _context.CashRegisters
                    .AnyAsync(cr => cr.UserId == Guid.Parse(currentUser.Result.Id) && cr.Status == CashRegisterStatus.Open);

                if (hasOpenCashRegister)
                    return Response<CashRegisterDTO>.Failure("Ya tienes una caja abierta");

                // Crear nueva caja
                var cashRegister = new CashRegister
                {
                    Id = Guid.NewGuid(),
                    UserId = Guid.Parse(currentUser.Result.Id), // Convertir string a Guid
                    OpenDate = DateTime.Now,
                    InitialAmount = dto.InitialAmount,
                    Status = CashRegisterStatus.Open,
                    Observations = dto.Observations
                };

                await _context.CashRegisters.AddAsync(cashRegister);
                await _context.SaveChangesAsync();

                var cashRegisterDto = _mapper.Map<CashRegisterDTO>(cashRegister);
                return Response<CashRegisterDTO>.Success(cashRegisterDto, "Caja abierta exitosamente");
            }
            catch (Exception ex)
            {
                return Response<CashRegisterDTO>.Failure(ex);
            }
        }

        public async Task<Response<CashRegisterDTO>> CloseCashRegisterAsync(CloseCashRegisterDTO dto)
        {
            try
            {
                var cashRegister = await _context.CashRegisters
                    .Include(cr => cr.Sales)
                    .FirstOrDefaultAsync(cr => cr.Id == dto.CashRegisterId);

                if (cashRegister == null)
                    return Response<CashRegisterDTO>.Failure("Caja no encontrada");

                if (cashRegister.Status == CashRegisterStatus.Closed)
                    return Response<CashRegisterDTO>.Failure("La caja ya está cerrada");

                // Calcular totales
                cashRegister.TotalSales = cashRegister.Sales.Sum(s => s.TotalAmount);
                cashRegister.TotalCash = cashRegister.Sales
                    .Where(s => s.PaymentMethod == "Efectivo")
                    .Sum(s => s.TotalAmount);
                cashRegister.TotalCard = cashRegister.Sales
                    .Where(s => s.PaymentMethod == "Tarjeta")
                    .Sum(s => s.TotalAmount);
                cashRegister.TotalTransfer = cashRegister.Sales
                    .Where(s => s.PaymentMethod == "Transferencia")
                    .Sum(s => s.TotalAmount);

                cashRegister.FinalAmount = dto.FinalCashAmount;
                cashRegister.Difference = dto.FinalCashAmount - (cashRegister.InitialAmount + (cashRegister.TotalCash ?? 0));
                cashRegister.CloseDate = DateTime.Now;
                cashRegister.Status = CashRegisterStatus.Closed;
                cashRegister.Observations += $"\nCierre: {dto.Observations}";

                _context.CashRegisters.Update(cashRegister);
                await _context.SaveChangesAsync();

                var cashRegisterDto = _mapper.Map<CashRegisterDTO>(cashRegister);
                return Response<CashRegisterDTO>.Success(cashRegisterDto, "Caja cerrada exitosamente");
            }
            catch (Exception ex)
            {
                return Response<CashRegisterDTO>.Failure(ex);
            }
        }

        public async Task<Response<CashRegisterDTO>> GetCurrentCashRegisterAsync()
        {
            try
            {
                var currentUser = await _usersService.GetCurrentUserAsync();
                if (!currentUser.IsSuccess)
                    return Response<CashRegisterDTO>.Failure("Usuario no autenticado");

                var cashRegister = await _context.CashRegisters
                    .Include(cr => cr.User)
                    .Include(cr => cr.Sales)
                    .FirstOrDefaultAsync(cr => cr.UserId == Guid.Parse(currentUser.Result.Id) &&
                                             cr.Status == CashRegisterStatus.Open);

                if (cashRegister == null)
                    return Response<CashRegisterDTO>.Failure("No hay caja abierta");

                var cashRegisterDto = _mapper.Map<CashRegisterDTO>(cashRegister);
                return Response<CashRegisterDTO>.Success(cashRegisterDto);
            }
            catch (Exception ex)
            {
                return Response<CashRegisterDTO>.Failure(ex);
            }
        }

        public async Task<Response<bool>> HasOpenCashRegisterAsync()
        {
            try
            {
                var currentUser = await _usersService.GetCurrentUserAsync();
                if (!currentUser.IsSuccess)
                    return Response<bool>.Failure("Usuario no autenticado");

                bool hasOpen = await _context.CashRegisters
                    .AnyAsync(cr => cr.UserId == Guid.Parse(currentUser.Result.Id) &&
                                   cr.Status == CashRegisterStatus.Open);

                return Response<bool>.Success(hasOpen);
            }
            catch (Exception ex)
            {
                return Response<bool>.Failure(ex);
            }
        }
        public async Task<Response<CashRegisterDTO>> GetOneAsync(Guid id)
        {
            return await GetOneAsync<CashRegister, CashRegisterDTO>(id);
        }

        public async Task<Response<List<CashRegisterDTO>>> GetListAsync()
        {
            return await GetCompleteListAsync<CashRegister, CashRegisterDTO>();
        }

        public async Task<Response<PaginationResponse<CashRegisterDTO>>> GetPaginatedListAsync(PaginationRequest request)
        {
            IQueryable<CashRegister> query = _context.CashRegisters
                .Include(cr => cr.User)
                .AsQueryable();

            if (!string.IsNullOrWhiteSpace(request.Filter))
            {
                query = query.Where(cr => cr.User.FirstName.ToLower().Contains(request.Filter.ToLower()) ||
                                         cr.User.LastName.ToLower().Contains(request.Filter.ToLower()) ||
                                         cr.Observations.ToLower().Contains(request.Filter.ToLower()));
            }

            query = query.OrderByDescending(cr => cr.OpenDate);

            return await GetPaginationAsync<CashRegister, CashRegisterDTO>(request, query);
        }

        public async Task<Response<CashRegisterDTO>> GetCashRegisterSummaryAsync(Guid id)
        {
            try
            {
                var cashRegister = await _context.CashRegisters
                    .Include(cr => cr.User)
                    .Include(cr => cr.Sales)
                    .FirstOrDefaultAsync(cr => cr.Id == id);

                if (cashRegister == null)
                    return Response<CashRegisterDTO>.Failure("Caja no encontrada");

                var cashRegisterDto = _mapper.Map<CashRegisterDTO>(cashRegister);
                return Response<CashRegisterDTO>.Success(cashRegisterDto);
            }
            catch (Exception ex)
            {
                return Response<CashRegisterDTO>.Failure(ex);
            }
        }

        public async Task<Response<CashRegisterDTO>> GetDailySummaryAsync(DateTime date)
        {
            try
            {
                var startDate = date.Date;
                var endDate = date.Date.AddDays(1).AddTicks(-1);

                var cashRegisters = await _context.CashRegisters
                    .Include(cr => cr.Sales)
                    .Where(cr => cr.OpenDate >= startDate && cr.OpenDate <= endDate)
                    .ToListAsync();

                if (!cashRegisters.Any())
                    return Response<CashRegisterDTO>.Failure("No hay registros para la fecha especificada");

                var summary = new CashRegisterDTO
                {
                    TotalSales = cashRegisters.Sum(cr => cr.TotalSales ?? 0),
                    TotalCash = cashRegisters.Sum(cr => cr.TotalCash ?? 0),
                    TotalCard = cashRegisters.Sum(cr => cr.TotalCard ?? 0),
                    TotalTransfer = cashRegisters.Sum(cr => cr.TotalTransfer ?? 0)
                };

                return Response<CashRegisterDTO>.Success(summary);
            }
            catch (Exception ex)
            {
                return Response<CashRegisterDTO>.Failure(ex);
            }
        }

        public async Task<Response<List<CashRegisterDTO>>> GetCashRegistersByDateRangeAsync(DateTime startDate, DateTime endDate)
        {
            try
            {
                var cashRegisters = await _context.CashRegisters
                    .Include(cr => cr.User)
                    .Where(cr => cr.OpenDate >= startDate && cr.OpenDate <= endDate)
                    .OrderByDescending(cr => cr.OpenDate)
                    .ToListAsync();

                var cashRegisterDtos = _mapper.Map<List<CashRegisterDTO>>(cashRegisters);
                return Response<List<CashRegisterDTO>>.Success(cashRegisterDtos);
            }
            catch (Exception ex)
            {
                return Response<List<CashRegisterDTO>>.Failure(ex);
            }
        }
    }
}
