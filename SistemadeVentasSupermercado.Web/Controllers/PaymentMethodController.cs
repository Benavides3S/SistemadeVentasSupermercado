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
    public class PaymentMethodController : Controller
    {
        private readonly IPaymentMethodService _paymentMethodService;
        private readonly INotyfService _notyfService;

        public PaymentMethodController(IPaymentMethodService paymentMethodService, INotyfService notyfService)
        {
            _paymentMethodService = paymentMethodService;
            _notyfService = notyfService;
        }

        [HttpGet]
        [CustomAuthorize(permission: "showPaymentMethods", module: "Configuración")]
        public async Task<IActionResult> Index([FromQuery] PaginationRequest request)
        {
            Response<PaginationResponse<PaymentMethodDTO>> response = await _paymentMethodService.GetPaginatedListAsync(request);

            if (!response.IsSuccess)
            {
                _notyfService.Error(response.Message);
                return RedirectToAction("Index", "Home");
            }

            return View(response.Result);
        }

        [HttpGet]
        [CustomAuthorize("Configuración", "createPaymentMethods")]
        public IActionResult Create()
        {
            return View();
        }

        [HttpPost]
        [CustomAuthorize("createPaymentMethods", "Configuración")]
        public async Task<IActionResult> Create([FromForm] PaymentMethodDTO dto)
        {
            if (!ModelState.IsValid)
            {
                _notyfService.Error("Debe ajustar los errores de validación");
                return View(dto);
            }

            Response<PaymentMethodDTO> response = await _paymentMethodService.CreateAsync(dto);

            if (!response.IsSuccess)
            {
                _notyfService.Error(response.Message);
                return View(dto);
            }

            _notyfService.Success(response.Message);
            return RedirectToAction(nameof(Index));
        }

        [HttpGet]
        [CustomAuthorize("updatePaymentMethods", "Configuración")]
        public async Task<IActionResult> Edit([FromRoute] Guid id)
        {
            Response<PaymentMethodDTO> response = await _paymentMethodService.GetOneAsync(id);

            if (!response.IsSuccess)
            {
                _notyfService.Error(response.Message);
                return RedirectToAction(nameof(Index));
            }

            return View(response.Result);
        }

        [HttpPost]
        [CustomAuthorize("updatePaymentMethods", "Configuración")]
        public async Task<IActionResult> Edit([FromForm] PaymentMethodDTO dto)
        {
            if (!ModelState.IsValid)
            {
                _notyfService.Error("Debe ajustar los errores de validación");
                return View(dto);
            }

            Response<PaymentMethodDTO> response = await _paymentMethodService.EditAsync(dto);

            if (!response.IsSuccess)
            {
                _notyfService.Error(response.Message);
                return View(dto);
            }

            _notyfService.Success(response.Message);
            return RedirectToAction(nameof(Index));
        }

        [HttpPost]
        [CustomAuthorize("deletePaymentMethods", "Configuración")]
        public async Task<IActionResult> Delete([FromRoute] Guid id)
        {
            Response<object> response = await _paymentMethodService.DeleteAsync(id);

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
