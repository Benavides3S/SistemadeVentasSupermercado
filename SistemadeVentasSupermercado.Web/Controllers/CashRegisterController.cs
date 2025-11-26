using AspNetCoreHero.ToastNotification.Abstractions;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using SistemadeVentasSupermercado.Web.Core;
using SistemadeVentasSupermercado.Web.Core.Attributes;
using SistemadeVentasSupermercado.Web.Core.Pagination;
using SistemadeVentasSupermercado.Web.DTOs;
using SistemadeVentasSupermercado.Web.Services.Abstractions;

namespace SistemadeVentasSupermercado.Web.Controllers
{
    [Authorize]
    public class CashRegisterController : Controller
    {
        private readonly ICashRegisterService _cashRegisterService;
        private readonly INotyfService _notyfService;

        public CashRegisterController(ICashRegisterService cashRegisterService, INotyfService notyfService)
        {
            _cashRegisterService = cashRegisterService;
            _notyfService = notyfService;
        }

        [HttpGet]
        [CustomAuthorize(permission: "showCashRegisters", module: "Caja")]
        public async Task<IActionResult> Index([FromQuery] PaginationRequest request)
        {
            Response<PaginationResponse<CashRegisterDTO>> response = await _cashRegisterService.GetPaginatedListAsync(request);

            if (!response.IsSuccess)
            {
                _notyfService.Error(response.Message);
                return RedirectToAction("Index", "Home");
            }

            // Verificar si el usuario tiene caja abierta
            var hasOpenResponse = await _cashRegisterService.HasOpenCashRegisterAsync();
            ViewBag.HasOpenCashRegister = hasOpenResponse.IsSuccess && hasOpenResponse.Result;

            return View(response.Result);
        }

        [HttpGet]
        [CustomAuthorize("openCashRegisters", "Caja")]
        public async Task<IActionResult> Open()
        {
            // Verificar si ya tiene caja abierta
            var hasOpenResponse = await _cashRegisterService.HasOpenCashRegisterAsync();
            if (hasOpenResponse.IsSuccess && hasOpenResponse.Result)
            {
                _notyfService.Warning("Ya tienes una caja abierta");
                return RedirectToAction(nameof(Index));
            }

            return View();
        }

        [HttpPost]
        [CustomAuthorize("openCashRegisters", "Caja")]
        public async Task<IActionResult> Open([FromForm] OpenCashRegisterDTO dto)
        {
            if (!ModelState.IsValid)
            {
                _notyfService.Error("Debe ajustar los errores de validación");
                return View(dto);
            }

            Response<CashRegisterDTO> response = await _cashRegisterService.OpenCashRegisterAsync(dto);

            if (!response.IsSuccess)
            {
                _notyfService.Error(response.Message);
                return View(dto);
            }

            _notyfService.Success(response.Message);
            return RedirectToAction(nameof(Index));
        }

        [HttpGet]
        [CustomAuthorize("closeCashRegisters", "Caja")]
        public async Task<IActionResult> Close()
        {
            // Obtener caja actual abierta
            var currentResponse = await _cashRegisterService.GetCurrentCashRegisterAsync();
            if (!currentResponse.IsSuccess)
            {
                _notyfService.Error(currentResponse.Message);
                return RedirectToAction(nameof(Index));
            }

            var closeDto = new CloseCashRegisterDTO
            {
                CashRegisterId = currentResponse.Result.Id
            };

            ViewBag.CurrentCashRegister = currentResponse.Result;
            return View(closeDto);
        }

        [HttpPost]
        [CustomAuthorize("closeCashRegisters", "Caja")]
        public async Task<IActionResult> Close([FromForm] CloseCashRegisterDTO dto)
        {
            if (!ModelState.IsValid)
            {
                _notyfService.Error("Debe ajustar los errores de validación");

                // Recargar la caja actual para la vista
                var currentResponse = await _cashRegisterService.GetCurrentCashRegisterAsync();
                if (currentResponse.IsSuccess)
                {
                    ViewBag.CurrentCashRegister = currentResponse.Result;
                }

                return View(dto);
            }

            Response<CashRegisterDTO> response = await _cashRegisterService.CloseCashRegisterAsync(dto);

            if (!response.IsSuccess)
            {
                _notyfService.Error(response.Message);

                // Recargar la caja actual para la vista
                var currentResponse = await _cashRegisterService.GetCurrentCashRegisterAsync();
                if (currentResponse.IsSuccess)
                {
                    ViewBag.CurrentCashRegister = currentResponse.Result;
                }

                return View(dto);
            }

            _notyfService.Success(response.Message);
            return RedirectToAction(nameof(Index));
        }

        [HttpGet]
        [CustomAuthorize("showCashRegisters", "Caja")]
        public async Task<IActionResult> Details(Guid id)
        {
            Response<CashRegisterDTO> response = await _cashRegisterService.GetCashRegisterSummaryAsync(id);

            if (!response.IsSuccess)
            {
                _notyfService.Error(response.Message);
                return RedirectToAction(nameof(Index));
            }

            return View(response.Result);
        }

        [HttpGet]
        [CustomAuthorize("showCashRegisters", "Caja")]
        public async Task<IActionResult> DailySummary(DateTime? date)
        {
            var targetDate = date ?? DateTime.Today;
            Response<CashRegisterDTO> response = await _cashRegisterService.GetDailySummaryAsync(targetDate);

            if (!response.IsSuccess)
            {
                _notyfService.Error(response.Message);
            }

            ViewBag.SelectedDate = targetDate;
            return View(response.Result ?? new CashRegisterDTO());
        }

        [HttpGet]
        public async Task<IActionResult> CheckOpenCashRegister()
        {
            var response = await _cashRegisterService.HasOpenCashRegisterAsync();

            if (!response.IsSuccess)
            {
                return Json(new { success = false, message = response.Message });
            }

            return Json(new { success = true, hasOpenCashRegister = response.Result });
        }
    }
}