using Microsoft.EntityFrameworkCore;
using SistemadeVentasSupermercado.Web.Core;
using SistemadeVentasSupermercado.Web.Data;
using SistemadeVentasSupermercado.Web.Data.Entities;
using SistemadeVentasSupermercado.Web.DTOs;
using SistemadeVentasSupermercado.Web.Services.Abstractions;

namespace SistemadeVentasSupermercado.Web.Services.Implementations
{
    public class HomeService : IHomeService
    {
        /// <summary>Un producto con este stock o menos aparece como "poco stock".</summary>
        public const decimal LowStockThreshold = 5m;
        public const int LowStockMaxItems = 10;

        private readonly DataContext _context;

        public HomeService(DataContext context)
        {
            _context = context;
        }

        public async Task<Response<HomeSummaryDTO>> GetSummaryAsync(HomeRequestDTO request)
        {
            try
            {
                HomeSummaryDTO summary = new HomeSummaryDTO();

                if (request.WantsSalesToday)
                {
                    DateTime start = ColombiaCalendar.StartOfDayUtc(ColombiaCalendar.Today());
                    DateTime end = start.AddDays(1);

                    var totals = await _context.Sales.AsNoTracking()
                        .Where(s => s.Status == SaleStatus.Completed && s.CreatedAtUtc >= start && s.CreatedAtUtc < end)
                        .GroupBy(_ => 1)
                        .Select(g => new { Count = g.Count(), Total = g.Sum(s => s.Total) })
                        .FirstOrDefaultAsync();

                    summary.SalesCountToday = totals?.Count ?? 0;
                    summary.SalesTotalToday = totals?.Total ?? 0m;
                }

                if (request.WantsOwnCashSession)
                {
                    DateTime? openedAt = await _context.CashSessions.AsNoTracking()
                        .Where(c => c.UserId == request.UserId && c.Status == CashSessionStatus.Open)
                        .Select(c => (DateTime?)c.OpenedAtUtc)
                        .FirstOrDefaultAsync();

                    summary.HasOpenCashSession = openedAt.HasValue;
                    summary.CashSessionOpenedAtUtc = openedAt;
                }

                if (request.WantsLowStock)
                {
                    // Solo productos que se pueden vender (categoría activa), los más escasos primero.
                    summary.LowStock = await _context.Products.AsNoTracking()
                        .Where(p => p.Stock <= LowStockThreshold && p.Category!.IsActive)
                        .OrderBy(p => p.Stock).ThenBy(p => p.Name)
                        .Take(LowStockMaxItems)
                        .Select(p => new LowStockProductDTO { Id = p.Id, Name = p.Name, Stock = p.Stock, Unit = p.Unit })
                        .ToListAsync();
                }

                return Response<HomeSummaryDTO>.Success(summary);
            }
            catch (Exception ex)
            {
                return Response<HomeSummaryDTO>.Failure(ex);
            }
        }
    }
}
