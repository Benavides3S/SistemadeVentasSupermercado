using SistemadeVentasSupermercado.Web.Core;
using SistemadeVentasSupermercado.Web.DTOs;

namespace SistemadeVentasSupermercado.Web.Services.Abstractions
{
    /// <summary>
    /// Reporte por cajero. viewerUserId es quien consulta: si tiene la caja abierta no ve sus propias cifras.
    /// </summary>
    public interface ICashierReportService
    {
        Task<Response<CashierReportDTO>> GetSummaryAsync(DateOnly from, DateOnly to, string viewerUserId);

        /// <summary>Anulaciones, devoluciones y turnos de una persona, o de todas si cashierId es null.</summary>
        Task<Response<CashierDetailsDTO>> GetDetailsAsync(DateOnly from, DateOnly to, string viewerUserId, string? cashierId = null);
    }
}
