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
    public class ReportsController : Controller
    {
        private const int TopOnScreen = 20;
        private const int TopInExcel = 100;

        private readonly IReportsService _reportsService;
        private readonly INotyfService _notyfService;
        private readonly BusinessOptions _business;

        public ReportsController(IReportsService reportsService, INotyfService notyfService, IOptions<BusinessOptions> business)
        {
            _reportsService = reportsService;
            _notyfService = notyfService;
            _business = business.Value;
        }

        /// <summary>Por defecto: desde el primer día del mes actual hasta hoy (hora de Colombia).</summary>
        private static (DateOnly from, DateOnly to) Range(ReportRequest request)
        {
            DateOnly today = ColombiaCalendar.Today();
            DateOnly to = request.To ?? today;
            DateOnly from = request.From ?? new DateOnly(to.Year, to.Month, 1);
            return (from, to);
        }

        [HttpGet]
        [CustomAuthorize("showReports", "Reportes")]
        public async Task<IActionResult> Index([FromQuery] ReportRequest request)
        {
            (DateOnly from, DateOnly to) = Range(request);
            ViewBag.From = from;
            ViewBag.To = to;
            ViewBag.Business = _business;

            Response<DailySalesReportDTO> daily = await _reportsService.GetDailySalesAsync(from, to);
            Response<List<TopProductDTO>> top = await _reportsService.GetTopProductsAsync(from, to, TopOnScreen);

            if (!daily.IsSuccess || !top.IsSuccess)
            {
                _notyfService.Error(daily.IsSuccess ? top.Message : daily.Message);
                ViewBag.Top = new List<TopProductDTO>();
                return View((DailySalesReportDTO?)null);
            }

            ViewBag.Top = top.Result;
            return View(daily.Result);
        }

        [HttpGet]
        [CustomAuthorize("showReports", "Reportes")]
        public async Task<IActionResult> Excel([FromQuery] ReportRequest request)
        {
            (DateOnly from, DateOnly to) = Range(request);

            Response<DailySalesReportDTO> daily = await _reportsService.GetDailySalesAsync(from, to);
            Response<List<TopProductDTO>> top = await _reportsService.GetTopProductsAsync(from, to, TopInExcel);

            if (!daily.IsSuccess || !top.IsSuccess)
            {
                _notyfService.Error(daily.IsSuccess ? top.Message : daily.Message);
                return RedirectToAction(nameof(Index), new { From = from.ToString("yyyy-MM-dd"), To = to.ToString("yyyy-MM-dd") });
            }

            byte[] file = ReportExcelBuilder.Build(_business.Nombre, daily.Result!, top.Result!);
            string name = $"reporte-ventas-{from:yyyy-MM-dd}-a-{to:yyyy-MM-dd}.xlsx";

            return File(file, "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet", name);
        }
    }
}
