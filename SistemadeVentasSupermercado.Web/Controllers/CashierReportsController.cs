using System.Security.Claims;
using AspNetCoreHero.ToastNotification.Abstractions;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using SistemadeVentasSupermercado.Web.Core;
using SistemadeVentasSupermercado.Web.Core.Attributes;
using SistemadeVentasSupermercado.Web.DTOs;
using SistemadeVentasSupermercado.Web.Helpers;
using SistemadeVentasSupermercado.Web.Services.Abstractions;

namespace SistemadeVentasSupermercado.Web.Controllers
{
    [Authorize]
    public class CashierReportsController : Controller
    {
        private readonly ICashierReportService _reportService;
        private readonly INotyfService _notyfService;
        private readonly BusinessOptions _business;

        public CashierReportsController(ICashierReportService reportService, INotyfService notyfService,
            IOptions<BusinessOptions> business)
        {
            _reportService = reportService;
            _notyfService = notyfService;
            _business = business.Value;
        }

        private string CurrentUserId => User.FindFirstValue(ClaimTypes.NameIdentifier) ?? string.Empty;

        /// <summary>Por defecto: desde el primer día del mes actual hasta hoy (hora de Colombia).</summary>
        private (DateOnly from, DateOnly to) Range(ReportRequest request)
        {
            DateOnly to = request.To ?? ColombiaCalendar.Today();
            DateOnly from = request.From ?? new DateOnly(to.Year, to.Month, 1);
            ViewBag.From = from;
            ViewBag.To = to;
            ViewBag.Business = _business;
            return (from, to);
        }

        private static object RangeRoute(DateOnly from, DateOnly to)
            => new { From = from.ToString("yyyy-MM-dd"), To = to.ToString("yyyy-MM-dd") };

        [HttpGet]
        [CustomAuthorize("showCashierReport", "Reportes")]
        public async Task<IActionResult> Index([FromQuery] ReportRequest request)
        {
            (DateOnly from, DateOnly to) = Range(request);
            Response<CashierReportDTO> response = await _reportService.GetSummaryAsync(from, to, CurrentUserId);

            if (!response.IsSuccess)
                _notyfService.Error(response.Message);

            return View(response.IsSuccess ? response.Result : null);
        }

        [HttpGet]
        [CustomAuthorize("showCashierReport", "Reportes")]
        public async Task<IActionResult> Detail(Guid id, [FromQuery] ReportRequest request)
        {
            (DateOnly from, DateOnly to) = Range(request);
            Response<CashierDetailsDTO> response =
                await _reportService.GetDetailsAsync(from, to, CurrentUserId, id.ToString());

            if (!response.IsSuccess)
            {
                _notyfService.Error(response.Message);
                return RedirectToAction(nameof(Index), RangeRoute(from, to));
            }

            return View(response.Result);
        }

        /// <summary>Excel de todas las personas, o solo de una si llega cashierId.</summary>
        [HttpGet]
        [CustomAuthorize("showCashierReport", "Reportes")]
        public async Task<IActionResult> Excel([FromQuery] ReportRequest request, Guid? cashierId)
        {
            (DateOnly from, DateOnly to) = Range(request);

            Response<CashierDetailsDTO> details =
                await _reportService.GetDetailsAsync(from, to, CurrentUserId, cashierId?.ToString());
            Response<CashierReportDTO>? summary =
                cashierId is null ? await _reportService.GetSummaryAsync(from, to, CurrentUserId) : null;

            if (!details.IsSuccess || summary is { IsSuccess: false })
            {
                _notyfService.Error(details.IsSuccess ? summary!.Message : details.Message);
                return RedirectToAction(nameof(Index), RangeRoute(from, to));
            }

            List<CashierRowDTO> rows = summary?.Result!.Rows ?? [details.Result!.Cashier!];
            byte[] file = CashierReportExcelBuilder.Build(_business.Nombre, details.Result!, rows);
            string name = $"reporte-por-cajero-{from:yyyy-MM-dd}-a-{to:yyyy-MM-dd}.xlsx";

            // Trae datos de empleados: que el navegador no guarde copia.
            Response.Headers.CacheControl = "no-store";
            return File(file, "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet", name);
        }
    }
}
