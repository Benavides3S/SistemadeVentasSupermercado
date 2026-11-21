using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using SistemadeVentasSupermercado.Web.Core;
using SistemadeVentasSupermercado.Web.Core.Pagination;
using SistemadeVentasSupermercado.Web.Data;
using SistemadeVentasSupermercado.Web.Data.Entities;
using SistemadeVentasSupermercado.Web.DTOs;
using SistemadeVentasSupermercado.Web.Services.Abstractions;

namespace SistemadeVentasSupermercado.Web.Services.Implementations
{
    public class CashSessionService : ICashSessionService
    {
        private readonly DataContext _context;

        public CashSessionService(DataContext context)
        {
            _context = context;
        }

        // Proyección común de un turno a DTO (se traduce a SQL).
        private IQueryable<CashSessionDTO> Projected(IQueryable<CashSession> query) => query.Select(c => new CashSessionDTO
        {
            Id = c.Id,
            Number = c.Number,
            UserId = c.UserId,
            CashierName = c.User!.FirstName + " " + c.User.LastName,
            OpenedAtUtc = c.OpenedAtUtc,
            OpeningAmount = c.OpeningAmount,
            Status = c.Status,
            ClosedAtUtc = c.ClosedAtUtc,
            ClosedByName = c.ClosedByUser != null ? c.ClosedByUser.FirstName + " " + c.ClosedByUser.LastName : null,
            CountedAmount = c.CountedAmount,
            ExpectedAmount = c.ExpectedAmount,
            Difference = c.Difference,
            Notes = c.Notes
        });

        private Task<CashSessionDTO?> FindAsync(Guid id)
            => Projected(_context.CashSessions.AsNoTracking().Where(c => c.Id == id)).FirstOrDefaultAsync();

        public async Task<Response<CashSessionDTO?>> GetCurrentAsync(string userId)
        {
            try
            {
                CashSessionDTO? current = await Projected(_context.CashSessions.AsNoTracking()
                        .Where(c => c.UserId == userId && c.Status == CashSessionStatus.Open))
                    .FirstOrDefaultAsync();

                return Response<CashSessionDTO?>.Success(current);
            }
            catch (Exception ex)
            {
                return Response<CashSessionDTO?>.Failure(ex);
            }
        }

        public async Task<Response<CashSessionDTO>> OpenAsync(string userId, OpenCashSessionDTO dto)
        {
            if (dto.OpeningAmount < 0)
                return Response<CashSessionDTO>.Failure("El monto inicial no puede ser negativo");

            const string alreadyOpen = "Ya tienes una caja abierta";

            try
            {
                if (await _context.CashSessions.AnyAsync(c => c.UserId == userId && c.Status == CashSessionStatus.Open))
                    return Response<CashSessionDTO>.Failure(alreadyOpen);

                CashSession session = new CashSession
                {
                    Id = Guid.NewGuid(),
                    UserId = userId,
                    OpenedAtUtc = DateTime.UtcNow,
                    OpeningAmount = Math.Round(dto.OpeningAmount, 2, MidpointRounding.AwayFromZero),
                    Status = CashSessionStatus.Open
                };

                _context.CashSessions.Add(session);
                await _context.SaveChangesAsync();

                return Response<CashSessionDTO>.Success((await FindAsync(session.Id))!, "Caja abierta con éxito");
            }
            catch (DbUpdateException ex) when (IsUniqueViolation(ex))
            {
                // Dos aperturas a la vez: el índice único filtrado de la base de datos rechazó la segunda.
                return Response<CashSessionDTO>.Failure(alreadyOpen);
            }
            catch (Exception ex)
            {
                return Response<CashSessionDTO>.Failure(ex);
            }
        }

        public async Task<Response<CashSessionDTO>> CloseAsync(Guid sessionId, CloseCashSessionDTO dto, string closedByUserId, bool canCloseOthers)
        {
            if (dto.CountedAmount is null || dto.CountedAmount < 0)
                return Response<CashSessionDTO>.Failure("El efectivo contado no puede ser negativo");

            try
            {
                await using IDbContextTransaction transaction = await _context.Database.BeginTransactionAsync();

                CashSession? session = await _context.CashSessions.AsNoTracking().FirstOrDefaultAsync(c => c.Id == sessionId);

                if (session is null)
                    return Response<CashSessionDTO>.Failure("El turno de caja no existe");

                if (session.UserId != closedByUserId && !canCloseOthers)
                    return Response<CashSessionDTO>.Failure("Solo puedes cerrar tu propia caja");

                // 1. Se marca como cerrada de forma atómica (solo si sigue abierta).
                //    Esto bloquea la fila del turno: si hay ventas guardándose en este turno,
                //    el cierre espera a que terminen, y ninguna venta nueva puede entrar después.
                int closed = await _context.CashSessions
                    .Where(c => c.Id == sessionId && c.Status == CashSessionStatus.Open)
                    .ExecuteUpdateAsync(s => s.SetProperty(c => c.Status, CashSessionStatus.Closed));

                if (closed == 0)
                    return Response<CashSessionDTO>.Failure("Esta caja ya estaba cerrada");

                // 2. Con el turno ya bloqueado, se calcula el efectivo esperado.
                decimal expected = await ExpectedCashAsync(sessionId, session.OpeningAmount);
                decimal counted = Math.Round(dto.CountedAmount.Value, 2, MidpointRounding.AwayFromZero);
                string? notes = string.IsNullOrWhiteSpace(dto.Notes) ? null : dto.Notes.Trim();
                DateTime now = DateTime.UtcNow;

                await _context.CashSessions
                    .Where(c => c.Id == sessionId)
                    .ExecuteUpdateAsync(s => s
                        .SetProperty(c => c.ClosedAtUtc, now)
                        .SetProperty(c => c.ClosedByUserId, closedByUserId)
                        .SetProperty(c => c.CountedAmount, counted)
                        .SetProperty(c => c.ExpectedAmount, expected)
                        .SetProperty(c => c.Difference, counted - expected)
                        .SetProperty(c => c.Notes, notes));

                await transaction.CommitAsync();

                return Response<CashSessionDTO>.Success((await FindAsync(sessionId))!, "Caja cerrada con éxito");
            }
            catch (Exception ex)
            {
                return Response<CashSessionDTO>.Failure(ex);
            }
        }

        /// <summary>Monto inicial + ventas completadas en efectivo del turno.</summary>
        private async Task<decimal> ExpectedCashAsync(Guid sessionId, decimal openingAmount)
        {
            decimal cashSales = await _context.Sales
                .Where(s => s.CashSessionId == sessionId
                         && s.Status == SaleStatus.Completed
                         && s.PaymentMethod == PaymentMethod.Cash)
                .SumAsync(s => (decimal?)s.Total) ?? 0m;

            return openingAmount + cashSales;
        }

        public async Task<Response<CashSessionSummaryDTO>> GetSummaryAsync(Guid sessionId)
        {
            try
            {
                CashSessionDTO? session = await FindAsync(sessionId);
                if (session is null)
                    return Response<CashSessionSummaryDTO>.Failure("El turno de caja no existe");

                var groups = await _context.Sales
                    .AsNoTracking()
                    .Where(s => s.CashSessionId == sessionId)
                    .GroupBy(s => new { s.Status, s.PaymentMethod })
                    .Select(g => new { g.Key.Status, g.Key.PaymentMethod, Count = g.Count(), Total = g.Sum(s => s.Total) })
                    .ToListAsync();

                var completed = groups.Where(g => g.Status == SaleStatus.Completed).ToList();
                var cancelled = groups.Where(g => g.Status == SaleStatus.Cancelled).ToList();

                CashSessionSummaryDTO summary = new CashSessionSummaryDTO
                {
                    Session = session,
                    Payments = Enum.GetValues<PaymentMethod>()
                        .Select(m => new PaymentTotalDTO
                        {
                            PaymentMethod = m,
                            Count = completed.Where(g => g.PaymentMethod == m).Sum(g => g.Count),
                            Total = completed.Where(g => g.PaymentMethod == m).Sum(g => g.Total)
                        })
                        .ToList(),
                    SalesCount = completed.Sum(g => g.Count),
                    SalesTotal = completed.Sum(g => g.Total),
                    CancelledCount = cancelled.Sum(g => g.Count),
                    CancelledTotal = cancelled.Sum(g => g.Total)
                };

                decimal cashSales = summary.Payments.Single(p => p.PaymentMethod == PaymentMethod.Cash).Total;

                // Si está cerrada, se muestra lo que quedó guardado en el cierre.
                summary.ExpectedCash = session.ExpectedAmount ?? session.OpeningAmount + cashSales;

                return Response<CashSessionSummaryDTO>.Success(summary, "Reporte de caja obtenido con éxito");
            }
            catch (Exception ex)
            {
                return Response<CashSessionSummaryDTO>.Failure(ex);
            }
        }

        public async Task<Response<PaginationResponse<CashSessionDTO>>> GetPaginatedListAsync(PaginationRequest request)
        {
            try
            {
                IQueryable<CashSession> query = _context.CashSessions.AsNoTracking();

                if (!string.IsNullOrWhiteSpace(request.Filter))
                {
                    string filter = request.Filter.Trim().ToLower();

                    if (int.TryParse(filter, out int number))
                        query = query.Where(c => c.Number == number);
                    else
                        query = query.Where(c => (c.User!.FirstName + " " + c.User.LastName).ToLower().Contains(filter));
                }

                IQueryable<CashSessionDTO> projected = Projected(query.OrderByDescending(c => c.Number));

                PagedList<CashSessionDTO> list = await PagedList<CashSessionDTO>.ToPagedListAsync(projected, request);

                return Response<PaginationResponse<CashSessionDTO>>.Success(new PaginationResponse<CashSessionDTO>
                {
                    List = list,
                    TotalCount = list.TotalCount,
                    RecordsPerPage = list.RecordsPerPage,
                    CurrentPage = list.CurrentPage,
                    TotalPages = list.TotalPages,
                    Filter = request.Filter
                });
            }
            catch (Exception ex)
            {
                return Response<PaginationResponse<CashSessionDTO>>.Failure(ex);
            }
        }

        /// <summary>2601 y 2627 son los códigos de SQL Server para "valor duplicado en índice único".</summary>
        private static bool IsUniqueViolation(DbUpdateException ex)
            => ex.GetBaseException() is SqlException sql && (sql.Number == 2601 || sql.Number == 2627);
    }
}
