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
    public class DiscountController : Controller
    {
        private readonly IDiscountService _discountService;
        private readonly INotyfService _notyfService;

        public DiscountController(IDiscountService discountService, INotyfService notyfService)
        {
            _discountService = discountService;
            _notyfService = notyfService;
        }

        [HttpGet]
        [CustomAuthorize(permission: "showDiscounts", module: "Configuración")]
        public async Task<IActionResult> Index([FromQuery] PaginationRequest request)
        {
            Response<PaginationResponse<DiscountDTO>> response = await _discountService.GetPaginatedListAsync(request);

            if (!response.IsSuccess)
            {
                _notyfService.Error(response.Message);
                return RedirectToAction("Index", "Home");
            }

            return View(response.Result);
        }

        [HttpGet]
        [CustomAuthorize("Configuración", "createDiscounts")]
        public IActionResult Create()
        {
            return View();
        }

        [HttpPost]
        [CustomAuthorize("createDiscounts", "Configuración")]
        public async Task<IActionResult> Create([FromForm] DiscountDTO dto)
        {
            if (!ModelState.IsValid)
            {
                _notyfService.Error("Debe ajustar los errores de validación");
                return View(dto);
            }

            Response<DiscountDTO> response = await _discountService.CreateAsync(dto);

            if (!response.IsSuccess)
            {
                _notyfService.Error(response.Message);
                return View(dto);
            }

            _notyfService.Success(response.Message);
            return RedirectToAction(nameof(Index));
        }

        [HttpGet]
        [CustomAuthorize("updateDiscounts", "Configuración")]
        public async Task<IActionResult> Edit([FromRoute] Guid id)
        {
            Response<DiscountDTO> response = await _discountService.GetOneAsync(id);

            if (!response.IsSuccess)
            {
                _notyfService.Error(response.Message);
                return RedirectToAction(nameof(Index));
            }

            return View(response.Result);
        }

        [HttpPost]
        [CustomAuthorize("updateDiscounts", "Configuración")]
        public async Task<IActionResult> Edit([FromForm] DiscountDTO dto)
        {
            if (!ModelState.IsValid)
            {
                _notyfService.Error("Debe ajustar los errores de validación");
                return View(dto);
            }

            Response<DiscountDTO> response = await _discountService.EditAsync(dto);

            if (!response.IsSuccess)
            {
                _notyfService.Error(response.Message);
                return View(dto);
            }

            _notyfService.Success(response.Message);
            return RedirectToAction(nameof(Index));
        }

        [HttpPost]
        [CustomAuthorize("deleteDiscounts", "Configuración")]
        public async Task<IActionResult> Delete([FromRoute] Guid id)
        {
            Response<object> response = await _discountService.DeleteAsync(id);

            if (!response.IsSuccess)
            {
                _notyfService.Error(response.Message);
            }
            else
            {
                _notyfService.Success(response.Message);
            }

            return RedirectToAction(nameof(Index));
        }
    }
}
