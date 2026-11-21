using Microsoft.EntityFrameworkCore;
using SistemadeVentasSupermercado.Web.Core;
using SistemadeVentasSupermercado.Web.Data;
using SistemadeVentasSupermercado.Web.Data.Entities;
using SistemadeVentasSupermercado.Web.DTOs;
using SistemadeVentasSupermercado.Web.Services.Abstractions;

namespace SistemadeVentasSupermercado.Web.Services.Implementations
{
    public class ReportsService : IReportsService
    {
        /// <summary>Un año como máximo, para que una consulta no sea demasiado pesada.</summary>
        public const int MaxRangeDays = 366;

        private readonly DataContext _context;

        public ReportsService(DataContext context)
        {
            _context = context;
        }

        /// <summary>Valida el rango y lo convierte en instantes UTC [inicio, fin).</summary>
        private static string? ValidateRange(DateOnly from, DateOnly to, out DateTime fromUtc, out DateTime toUtcExclusive)
        {
            fromUtc = ColombiaCalendar.StartOfDayUtc(from);
            toUtcExclusive = ColombiaCalendar.StartOfDayUtc(to.AddDays(1));

            if (from > to)
                return "La fecha inicial no puede ser posterior a la final";

            if (to.DayNumber - from.DayNumber + 1 > MaxRangeDays)
                return $"El rango puede tener máximo {MaxRangeDays} días";

            return null;
        }

        public async Task<Response<DailySalesReportDTO>> GetDailySalesAsync(DateOnly from, DateOnly to)
        {
            string? error = ValidateRange(from, to, out DateTime fromUtc, out DateTime toUtc);
            if (error != null)
                return Response<DailySalesReportDTO>.Failure(error);

            try
            {
                // El filtro usa CreatedAtUtc directamente (aprovecha el índice);
                // la agrupación usa la fecha en hora de Colombia (resta de 5 horas en SQL).
                var sales = await _context.Sales
                    .AsNoTracking()
                    .Where(s => s.Status == SaleStatus.Completed && s.CreatedAtUtc >= fromUtc && s.CreatedAtUtc < toUtc)
                    .GroupBy(s => new { Day = s.CreatedAtUtc.AddHours(ColombiaCalendar.UtcOffsetHours).Date, s.PaymentMethod })
                    .Select(g => new { g.Key.Day, g.Key.PaymentMethod, Count = g.Count(), Total = g.Sum(s => s.Total) })
                    .ToListAsync();

                var returns = await _context.SaleReturns
                    .AsNoTracking()
                    .Where(r => r.CreatedAtUtc >= fromUtc && r.CreatedAtUtc < toUtc)
                    .GroupBy(r => r.CreatedAtUtc.AddHours(ColombiaCalendar.UtcOffsetHours).Date)
                    .Select(g => new { Day = g.Key, Count = g.Count(), Total = g.Sum(r => r.Total) })
                    .ToListAsync();

                DailySalesReportDTO report = new DailySalesReportDTO { From = from, To = to };

                // Se incluyen todos los días del rango, también los que no tuvieron movimiento.
                for (DateOnly day = from; day <= to; day = day.AddDays(1))
                {
                    DateTime key = day.ToDateTime(TimeOnly.MinValue);
                    var daySales = sales.Where(s => s.Day == key).ToList();
                    var dayReturns = returns.FirstOrDefault(r => r.Day == key);

                    report.Days.Add(new DailySalesRowDTO
                    {
                        Date = day,
                        SalesCount = daySales.Sum(s => s.Count),
                        SalesTotal = daySales.Sum(s => s.Total),
                        CashTotal = daySales.Where(s => s.PaymentMethod == PaymentMethod.Cash).Sum(s => s.Total),
                        CardTotal = daySales.Where(s => s.PaymentMethod == PaymentMethod.Card).Sum(s => s.Total),
                        TransferTotal = daySales.Where(s => s.PaymentMethod == PaymentMethod.Transfer).Sum(s => s.Total),
                        ReturnsCount = dayReturns?.Count ?? 0,
                        ReturnsTotal = dayReturns?.Total ?? 0m
                    });
                }

                report.Totals = new DailySalesRowDTO
                {
                    SalesCount = report.Days.Sum(d => d.SalesCount),
                    SalesTotal = report.Days.Sum(d => d.SalesTotal),
                    CashTotal = report.Days.Sum(d => d.CashTotal),
                    CardTotal = report.Days.Sum(d => d.CardTotal),
                    TransferTotal = report.Days.Sum(d => d.TransferTotal),
                    ReturnsCount = report.Days.Sum(d => d.ReturnsCount),
                    ReturnsTotal = report.Days.Sum(d => d.ReturnsTotal)
                };

                return Response<DailySalesReportDTO>.Success(report, "Reporte generado con éxito");
            }
            catch (Exception ex)
            {
                return Response<DailySalesReportDTO>.Failure(ex);
            }
        }

        public async Task<Response<List<TopProductDTO>>> GetTopProductsAsync(DateOnly from, DateOnly to, int top = 20)
        {
            string? error = ValidateRange(from, to, out DateTime fromUtc, out DateTime toUtc);
            if (error != null)
                return Response<List<TopProductDTO>>.Failure(error);

            top = Math.Clamp(top, 1, 100);

            try
            {
                var sold = await _context.SaleItems
                    .AsNoTracking()
                    .Where(i => i.Sale!.Status == SaleStatus.Completed
                             && i.Sale.CreatedAtUtc >= fromUtc && i.Sale.CreatedAtUtc < toUtc)
                    .GroupBy(i => i.ProductId)
                    .Select(g => new { ProductId = g.Key, Quantity = g.Sum(i => i.Quantity), Total = g.Sum(i => i.Total) })
                    .ToListAsync();

                // Devoluciones del rango (por la fecha en que se devolvió).
                var returned = await _context.SaleReturnItems
                    .AsNoTracking()
                    .Where(i => i.SaleReturn!.CreatedAtUtc >= fromUtc && i.SaleReturn.CreatedAtUtc < toUtc)
                    .GroupBy(i => i.ProductId)
                    .Select(g => new { ProductId = g.Key, Quantity = g.Sum(i => i.Quantity), Total = g.Sum(i => i.Total) })
                    .ToListAsync();

                List<Guid> productIds = sold.Select(s => s.ProductId).Union(returned.Select(r => r.ProductId)).ToList();

                // Nombre y unidad actuales del producto.
                var products = await _context.Products
                    .AsNoTracking()
                    .Where(p => productIds.Contains(p.Id))
                    .Select(p => new { p.Id, p.Name, p.Unit })
                    .ToDictionaryAsync(p => p.Id);

                List<TopProductDTO> ranking = productIds
                    .Where(products.ContainsKey)
                    .Select(id =>
                    {
                        var s = sold.FirstOrDefault(x => x.ProductId == id);
                        var r = returned.FirstOrDefault(x => x.ProductId == id);
                        return new TopProductDTO
                        {
                            ProductId = id,
                            ProductName = products[id].Name,
                            Unit = products[id].Unit,
                            QuantitySold = s?.Quantity ?? 0m,
                            SalesTotal = s?.Total ?? 0m,
                            QuantityReturned = r?.Quantity ?? 0m,
                            ReturnedTotal = r?.Total ?? 0m
                        };
                    })
                    .OrderByDescending(p => p.NetTotal)
                    .ThenBy(p => p.ProductName)
                    .Take(top)
                    .ToList();

                return Response<List<TopProductDTO>>.Success(ranking, "Reporte generado con éxito");
            }
            catch (Exception ex)
            {
                return Response<List<TopProductDTO>>.Failure(ex);
            }
        }
    }
}
