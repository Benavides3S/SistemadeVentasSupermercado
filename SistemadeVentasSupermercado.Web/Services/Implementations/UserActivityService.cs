using Microsoft.EntityFrameworkCore;
using SistemadeVentasSupermercado.Web.Core;
using SistemadeVentasSupermercado.Web.Core.Pagination;
using SistemadeVentasSupermercado.Web.Data;
using SistemadeVentasSupermercado.Web.Data.Entities;
using SistemadeVentasSupermercado.Web.DTOs;
using SistemadeVentasSupermercado.Web.Services.Abstractions;

namespace SistemadeVentasSupermercado.Web.Services.Implementations
{
    /// <summary>Consulta del historial de actividad. Solo lee: el historial no se edita ni se borra.</summary>
    public class UserActivityService : IUserActivityService
    {
        private readonly DataContext _context;

        public UserActivityService(DataContext context) => _context = context;

        public async Task<Response<PaginationResponse<UserActivityDTO>>> GetPaginatedListAsync(UserActivityQueryDTO request)
        {
            if (request.From.HasValue && request.To.HasValue && request.From > request.To)
                return Response<PaginationResponse<UserActivityDTO>>.Failure(
                    "La fecha \"Desde\" no puede ser posterior a la fecha \"Hasta\".");

            try
            {
                IQueryable<UserActivity> query = _context.UserActivities.AsNoTracking();

                if (request.Type.HasValue)
                    query = query.Where(a => a.Type == request.Type.Value);

                if (request.From.HasValue)
                {
                    DateTime from = ColombiaCalendar.StartOfDayUtc(request.From.Value);
                    query = query.Where(a => a.OccurredAtUtc >= from);
                }

                if (request.To.HasValue && request.To.Value < DateOnly.MaxValue)
                {
                    DateTime until = ColombiaCalendar.StartOfDayUtc(request.To.Value.AddDays(1));
                    query = query.Where(a => a.OccurredAtUtc < until);
                }

                if (!string.IsNullOrWhiteSpace(request.Filter))
                {
                    string filter = request.Filter.Trim();
                    query = query.Where(a => (a.ActorName != null && a.ActorName.Contains(filter))
                                          || (a.TargetName != null && a.TargetName.Contains(filter)));
                }

                IQueryable<UserActivityDTO> projected = query
                    .OrderByDescending(a => a.Number)
                    .Select(a => new UserActivityDTO
                    {
                        Number = a.Number,
                        OccurredAtUtc = a.OccurredAtUtc,
                        Type = a.Type,
                        ActorName = a.ActorName,
                        TargetName = a.TargetName,
                        Details = a.Details,
                        IpAddress = a.IpAddress,
                        AttemptCount = a.AttemptCount,
                        LastAttemptAtUtc = a.LastAttemptAtUtc
                    });

                PagedList<UserActivityDTO> list = await PagedList<UserActivityDTO>.ToPagedListAsync(projected, request);

                return Response<PaginationResponse<UserActivityDTO>>.Success(new PaginationResponse<UserActivityDTO>
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
                return Response<PaginationResponse<UserActivityDTO>>.Failure(ex);
            }
        }
    }
}
