using Microsoft.EntityFrameworkCore;
using SistemadeVentasSupermercado.Web.Core;
using SistemadeVentasSupermercado.Web.Data;
using SistemadeVentasSupermercado.Web.Data.Entities;
using SistemadeVentasSupermercado.Web.DTOs;
using SistemadeVentasSupermercado.Web.Services.Abstractions;

namespace SistemadeVentasSupermercado.Web.Services.Implementations
{
    /// <summary>
    /// Qué hizo cada persona en la caja. Solo lee. El resumen y el detalle salen de la misma carga,
    /// así sus cifras no pueden contradecirse.
    /// </summary>
    public class CashierReportService : ICashierReportService
    {
        public const string OwnFiguresMessage =
            "Tienes la caja abierta: no puedes ver tus propias cifras hasta cerrarla.";

        private readonly DataContext _context;

        public CashierReportService(DataContext context) => _context = context;

        public async Task<Response<CashierReportDTO>> GetSummaryAsync(DateOnly from, DateOnly to, string viewerUserId)
        {
            string? error = ReportsService.ValidateRange(from, to, out DateTime fromUtc, out DateTime toUtc);
            if (error != null)
                return Response<CashierReportDTO>.Failure(error);

            try
            {
                bool hideOwn = await HasOpenSessionAsync(viewerUserId);
                Activity activity = await LoadAsync(fromUtc, toUtc);
                Dictionary<string, CashierRowDTO> rows = await BuildRowsAsync(activity);

                if (hideOwn)
                    rows.Remove(viewerUserId);

                CashierReportDTO report = new()
                {
                    From = from,
                    To = to,
                    OwnFiguresHidden = hideOwn,
                    Rows = rows.Values.Where(HasActivity).OrderBy(r => r.Name).ThenBy(r => r.UserId).ToList()
                };
                report.Totals = Sum(report.Rows);

                return Response<CashierReportDTO>.Success(report, "Reporte generado con éxito");
            }
            catch (Exception ex)
            {
                return Response<CashierReportDTO>.Failure(ex);
            }
        }

        public async Task<Response<CashierDetailsDTO>> GetDetailsAsync(DateOnly from, DateOnly to,
            string viewerUserId, string? cashierId = null)
        {
            string? error = ReportsService.ValidateRange(from, to, out DateTime fromUtc, out DateTime toUtc);
            if (error != null)
                return Response<CashierDetailsDTO>.Failure(error);

            try
            {
                bool hideOwn = await HasOpenSessionAsync(viewerUserId);
                if (hideOwn && cashierId == viewerUserId)
                    return Response<CashierDetailsDTO>.Failure(OwnFiguresMessage);

                var person = cashierId is null
                    ? null
                    : await _context.Users.AsNoTracking().Where(u => u.Id == cashierId)
                        .Select(u => new { u.Id, u.FirstName, u.LastName, u.DeactivatedAt })
                        .FirstOrDefaultAsync();
                if (cashierId is not null && person is null)
                    return Response<CashierDetailsDTO>.Failure("No existe el usuario.");

                Activity activity = await LoadAsync(fromUtc, toUtc);
                Dictionary<string, CashierRowDTO> rows = await BuildRowsAsync(activity);

                string NameOf(string? id)
                    => id is not null && rows.TryGetValue(id, out CashierRowDTO? row) ? row.Name : "(sin registro)";

                // De una persona: donde participó. De todas: todo, menos lo que sea SOLO de quien consulta con caja abierta.
                bool Include(string chargedBy, string? doneBy)
                    => cashierId is not null
                        ? chargedBy == cashierId || doneBy == cashierId
                        : !(hideOwn && chargedBy == viewerUserId && (doneBy ?? viewerUserId) == viewerUserId);

                CashierDetailsDTO details = new()
                {
                    From = from,
                    To = to,
                    OwnFiguresHidden = hideOwn,
                    Cashier = person is null ? null
                        : rows.GetValueOrDefault(person.Id) ?? new CashierRowDTO
                        {
                            UserId = person.Id,
                            Name = $"{person.FirstName} {person.LastName}",
                            IsDeactivated = person.DeactivatedAt != null
                        },
                    Cancellations = activity.Cancellations
                        .Where(c => Include(c.ChargedById, c.CancelledById))
                        .OrderByDescending(c => c.CancelledAtUtc)
                        .Select(c => new CashierCancellationDTO
                        {
                            SaleId = c.SaleId,
                            SaleNumber = c.SaleNumber,
                            SoldAtUtc = c.SoldAtUtc,
                            CancelledAtUtc = c.CancelledAtUtc,
                            Total = c.Total,
                            Reason = c.Reason ?? string.Empty,
                            ChargedByName = NameOf(c.ChargedById),
                            CancelledByName = NameOf(c.CancelledById),
                            AuthorizedByName = c.AuthorizedById is null ? null : NameOf(c.AuthorizedById)
                        })
                        .ToList(),
                    Returns = activity.Returns
                        .Where(r => Include(r.ChargedById, r.RegisteredById))
                        .OrderByDescending(r => r.CreatedAtUtc)
                        .Select(r => new CashierReturnDTO
                        {
                            ReturnId = r.ReturnId,
                            ReturnNumber = r.ReturnNumber,
                            SaleId = r.SaleId,
                            SaleNumber = r.SaleNumber,
                            CreatedAtUtc = r.CreatedAtUtc,
                            Total = r.Total,
                            RefundMethod = r.RefundMethod,
                            Reason = r.Reason,
                            ChargedByName = NameOf(r.ChargedById),
                            RegisteredByName = NameOf(r.RegisteredById),
                            AuthorizedByName = r.AuthorizedById is null ? null : NameOf(r.AuthorizedById)
                        })
                        .ToList(),
                    // Los turnos son solo de su cajero.
                    Sessions = activity.Sessions
                        .Where(s => cashierId is not null ? s.CashierId == cashierId : !(hideOwn && s.CashierId == viewerUserId))
                        .OrderByDescending(s => s.Number)
                        .Select(s => new CashierSessionDTO
                        {
                            SessionId = s.SessionId,
                            Number = s.Number,
                            CashierName = NameOf(s.CashierId),
                            IsOpen = s.Status == CashSessionStatus.Open,
                            OpenedAtUtc = s.OpenedAtUtc,
                            ClosedAtUtc = s.ClosedAtUtc,
                            ExpectedAmount = s.ExpectedAmount,
                            CountedAmount = s.CountedAmount,
                            Difference = s.Difference,
                            Notes = s.Notes,
                            ClosedByName = s.ClosedById is null ? null : NameOf(s.ClosedById),
                            ClosedByOther = s.ClosedById is not null && s.ClosedById != s.CashierId
                        })
                        .ToList()
                };

                return Response<CashierDetailsDTO>.Success(details, "Reporte generado con éxito");
            }
            catch (Exception ex)
            {
                return Response<CashierDetailsDTO>.Failure(ex);
            }
        }

        private Task<bool> HasOpenSessionAsync(string userId)
            => _context.CashSessions.AnyAsync(c => c.UserId == userId && c.Status == CashSessionStatus.Open);

        // ---------- Carga de datos ----------

        private sealed record SalesGroup(string UserId, PaymentMethod Method, int Count, decimal Total);

        private sealed record Cancellation(Guid SaleId, int SaleNumber, DateTime SoldAtUtc, DateTime CancelledAtUtc,
            decimal Total, string? Reason, string ChargedById, string? CancelledById, string? AuthorizedById);

        private sealed record Return(Guid ReturnId, int ReturnNumber, Guid SaleId, int SaleNumber, DateTime CreatedAtUtc,
            decimal Total, PaymentMethod RefundMethod, string Reason, string ChargedById, string RegisteredById,
            string? AuthorizedById);

        private sealed record Session(Guid SessionId, int Number, string CashierId, CashSessionStatus Status,
            DateTime OpenedAtUtc, DateTime? ClosedAtUtc, decimal? ExpectedAmount, decimal? CountedAmount,
            decimal? Difference, string? Notes, string? ClosedById);

        private sealed record Activity(List<SalesGroup> Sales, List<Cancellation> Cancellations,
            List<Return> Returns, List<Session> Sessions);

        private async Task<Activity> LoadAsync(DateTime fromUtc, DateTime toUtc)
        {
            var sales = await _context.Sales.AsNoTracking()
                .Where(s => s.Status == SaleStatus.Completed && s.CreatedAtUtc >= fromUtc && s.CreatedAtUtc < toUtc)
                .GroupBy(s => new { s.UserId, s.PaymentMethod })
                .Select(g => new { g.Key.UserId, g.Key.PaymentMethod, Count = g.Count(), Total = g.Sum(s => s.Total) })
                .ToListAsync();

            var cancellations = await _context.Sales.AsNoTracking()
                .Where(s => s.Status == SaleStatus.Cancelled && s.CancelledAtUtc >= fromUtc && s.CancelledAtUtc < toUtc)
                .Select(s => new
                {
                    s.Id, s.Number, s.CreatedAtUtc, s.CancelledAtUtc, s.Total, s.CancelReason, s.UserId, s.CancelledByUserId,
                    s.CancelAuthorizedByUserId
                })
                .ToListAsync();

            var returns = await _context.SaleReturns.AsNoTracking()
                .Where(r => r.CreatedAtUtc >= fromUtc && r.CreatedAtUtc < toUtc)
                .Select(r => new
                {
                    r.Id, r.Number, r.SaleId, SaleNumber = r.Sale!.Number, r.CreatedAtUtc, r.Total, r.RefundMethod,
                    r.Reason, ChargedById = r.Sale.UserId, r.UserId, r.AuthorizedByUserId
                })
                .ToListAsync();

            // Cerrados en el rango, más los que están abiertos ahora.
            var sessions = await _context.CashSessions.AsNoTracking()
                .Where(c => c.Status == CashSessionStatus.Open
                         || (c.ClosedAtUtc >= fromUtc && c.ClosedAtUtc < toUtc))
                .Select(c => new
                {
                    c.Id, c.Number, c.UserId, c.Status, c.OpenedAtUtc, c.ClosedAtUtc, c.ExpectedAmount, c.CountedAmount,
                    c.Difference, c.Notes, c.ClosedByUserId
                })
                .ToListAsync();

            return new Activity(
                sales.Select(s => new SalesGroup(s.UserId, s.PaymentMethod, s.Count, s.Total)).ToList(),
                cancellations.Select(c => new Cancellation(c.Id, c.Number, c.CreatedAtUtc, c.CancelledAtUtc!.Value,
                    c.Total, c.CancelReason, c.UserId, c.CancelledByUserId, c.CancelAuthorizedByUserId)).ToList(),
                returns.Select(r => new Return(r.Id, r.Number, r.SaleId, r.SaleNumber, r.CreatedAtUtc, r.Total,
                    r.RefundMethod, r.Reason, r.ChargedById, r.UserId, r.AuthorizedByUserId)).ToList(),
                sessions.Select(c => new Session(c.Id, c.Number, c.UserId, c.Status, c.OpenedAtUtc, c.ClosedAtUtc,
                    c.ExpectedAmount, c.CountedAmount, c.Difference, c.Notes, c.ClosedByUserId)).ToList());
        }

        /// <summary>
        /// Una fila por cada persona que aparece. Quien solo cerró el turno de otro queda con la fila en ceros
        /// (hace falta su nombre para el detalle) y no sale en el resumen.
        /// </summary>
        private async Task<Dictionary<string, CashierRowDTO>> BuildRowsAsync(Activity activity)
        {
            List<string> ids = activity.Sales.Select(s => s.UserId)
                .Concat(activity.Cancellations.SelectMany(c => new[] { c.ChargedById, c.CancelledById }).OfType<string>())
                .Concat(activity.Returns.SelectMany(r => new[] { r.ChargedById, r.RegisteredById }))
                // Quien solo autorizó necesita su nombre en el detalle (en el resumen no suma nada).
                .Concat(activity.Cancellations.Select(c => c.AuthorizedById).OfType<string>())
                .Concat(activity.Returns.Select(r => r.AuthorizedById).OfType<string>())
                .Concat(activity.Sessions.SelectMany(s => new[] { s.CashierId, s.ClosedById }).OfType<string>())
                .Distinct()
                .ToList();

            var users = await _context.Users.AsNoTracking()
                .Where(u => ids.Contains(u.Id))
                .Select(u => new { u.Id, u.FirstName, u.LastName, u.DeactivatedAt })
                .ToListAsync();

            Dictionary<string, CashierRowDTO> rows = users.ToDictionary(u => u.Id, u => new CashierRowDTO
            {
                UserId = u.Id,
                Name = $"{u.FirstName} {u.LastName}",
                IsDeactivated = u.DeactivatedAt != null
            });

            foreach (SalesGroup group in activity.Sales)
            {
                CashierRowDTO row = rows[group.UserId];
                row.SalesCount += group.Count;
                row.SalesTotal += group.Total;
                if (group.Method == PaymentMethod.Cash) row.CashTotal += group.Total;
                else if (group.Method == PaymentMethod.Card) row.CardTotal += group.Total;
                else if (group.Method == PaymentMethod.Transfer) row.TransferTotal += group.Total;
            }

            foreach (Cancellation cancellation in activity.Cancellations)
            {
                CashierRowDTO charged = rows[cancellation.ChargedById];
                charged.OwnSalesCancelledCount++;
                charged.OwnSalesCancelledTotal += cancellation.Total;

                if (cancellation.CancelledById is not null)
                {
                    CashierRowDTO done = rows[cancellation.CancelledById];
                    done.CancellationsDoneCount++;
                    done.CancellationsDoneTotal += cancellation.Total;
                }
            }

            foreach (Return item in activity.Returns)
            {
                CashierRowDTO registered = rows[item.RegisteredById];
                registered.ReturnsRegisteredCount++;
                registered.ReturnsRegisteredTotal += item.Total;

                CashierRowDTO charged = rows[item.ChargedById];
                charged.ReturnsOnOwnSalesCount++;
                charged.ReturnsOnOwnSalesTotal += item.Total;
            }

            foreach (Session session in activity.Sessions)
            {
                CashierRowDTO row = rows[session.CashierId];
                if (session.Status == CashSessionStatus.Open)
                {
                    row.OpenSessionsCount++;
                    continue;
                }

                row.ClosedSessionsCount++;
                decimal difference = session.Difference ?? 0m; // contado − esperado
                if (difference != 0) row.UnbalancedSessionsCount++;
                if (difference < 0) row.ShortageTotal -= difference;
                else row.SurplusTotal += difference;
            }

            return rows;
        }

        private static bool HasActivity(CashierRowDTO row)
            => row.SalesCount + row.OwnSalesCancelledCount + row.CancellationsDoneCount + row.ReturnsRegisteredCount
             + row.ReturnsOnOwnSalesCount + row.ClosedSessionsCount + row.OpenSessionsCount > 0;

        private static CashierRowDTO Sum(List<CashierRowDTO> rows) => new()
        {
            Name = "TOTAL",
            SalesCount = rows.Sum(r => r.SalesCount),
            SalesTotal = rows.Sum(r => r.SalesTotal),
            CashTotal = rows.Sum(r => r.CashTotal),
            CardTotal = rows.Sum(r => r.CardTotal),
            TransferTotal = rows.Sum(r => r.TransferTotal),
            OwnSalesCancelledCount = rows.Sum(r => r.OwnSalesCancelledCount),
            OwnSalesCancelledTotal = rows.Sum(r => r.OwnSalesCancelledTotal),
            CancellationsDoneCount = rows.Sum(r => r.CancellationsDoneCount),
            CancellationsDoneTotal = rows.Sum(r => r.CancellationsDoneTotal),
            ReturnsRegisteredCount = rows.Sum(r => r.ReturnsRegisteredCount),
            ReturnsRegisteredTotal = rows.Sum(r => r.ReturnsRegisteredTotal),
            ReturnsOnOwnSalesCount = rows.Sum(r => r.ReturnsOnOwnSalesCount),
            ReturnsOnOwnSalesTotal = rows.Sum(r => r.ReturnsOnOwnSalesTotal),
            ClosedSessionsCount = rows.Sum(r => r.ClosedSessionsCount),
            UnbalancedSessionsCount = rows.Sum(r => r.UnbalancedSessionsCount),
            ShortageTotal = rows.Sum(r => r.ShortageTotal),
            SurplusTotal = rows.Sum(r => r.SurplusTotal),
            OpenSessionsCount = rows.Sum(r => r.OpenSessionsCount)
        };
    }
}
