using AspNetCoreHero.ToastNotification.Abstractions;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using SistemadeVentasSupermercado.Web.Core;
using SistemadeVentasSupermercado.Web.Core.Attributes;
using SistemadeVentasSupermercado.Web.Core.Pagination;
using SistemadeVentasSupermercado.Web.DTOs;
using SistemadeVentasSupermercado.Web.Helpers.Abstractions;
using SistemadeVentasSupermercado.Web.Services.Abstractions;
using System.Security.Claims;

namespace SistemadeVentasSupermercado.Web.Controllers
{
    [Authorize]
    public class PurchasesController : Controller
    {
        private readonly IPurchasesService _purchasesService;
        private readonly ICombosHelper _combosHelper;
        private readonly INotyfService _notyfService;

        public PurchasesController(IPurchasesService purchasesService, ICombosHelper combosHelper,
            INotyfService notyfService)
        {
            _purchasesService = purchasesService;
            _combosHelper = combosHelper;
            _notyfService = notyfService;
        }

        // ---------- Historial y detalle ----------

        [HttpGet]
        [CustomAuthorize("showPurchases", "Compras")]
        public async Task<IActionResult> Index([FromQuery] PaginationRequest request)
        {
            Response<PaginationResponse<PurchaseListItemDTO>> response = await _purchasesService.GetPaginatedListAsync(request);

            if (!response.IsSuccess)
            {
                _notyfService.Error(response.Message);
                return RedirectToAction("Index", "Home");
            }

            return View(response.Result);
        }

        [HttpGet]
        [CustomAuthorize("showPurchases", "Compras")]
        public async Task<IActionResult> Details([FromRoute] Guid id)
        {
            Response<PurchaseDetailDTO> response = await _purchasesService.GetOneAsync(id);

            if (!response.IsSuccess)
            {
                _notyfService.Error(response.Message);
                return RedirectToAction(nameof(Index));
            }

            return View(response.Result);
        }

        // ---------- Anulación ----------

        [HttpPost]
        [CustomAuthorize("cancelPurchases", "Compras")]
        public async Task<IActionResult> Cancel([FromRoute] Guid id, [FromForm] CancelPurchaseDTO dto)
        {
            if (!ModelState.IsValid)
            {
                _notyfService.Error("Escribe el motivo de la anulación (máximo 200 caracteres)");
                return RedirectToAction(nameof(Details), new { id });
            }

            // Quien anula es siempre el usuario de la sesión.
            string userId = User.FindFirstValue(ClaimTypes.NameIdentifier) ?? string.Empty;
            Response<PurchaseResultDTO> response = await _purchasesService.CancelAsync(id, dto, userId);

            if (response.IsSuccess)
                _notyfService.Success(response.Message);
            else
                _notyfService.Error(response.Message);

            return RedirectToAction(nameof(Details), new { id });
        }

        // ---------- Registrar compra ----------

        [HttpGet]
        [CustomAuthorize("createPurchases", "Compras")]
        public async Task<IActionResult> Create()
        {
            ViewBag.Suppliers = await _combosHelper.GetComboSuppliers();
            return View();
        }

        /// <summary>Lo llama el buscador de la pantalla de compras (fetch). Devuelve JSON.</summary>
        [HttpGet]
        [CustomAuthorize("createPurchases", "Compras")]
        public async Task<IActionResult> SearchProducts([FromQuery] string? term)
        {
            Response<List<PurchaseProductDTO>> response = await _purchasesService.SearchProductsAsync(term);
            return Json(new { isSuccess = response.IsSuccess, message = response.Message, items = response.Result });
        }

        /// <summary>Lo llama la pantalla de compras al escanear (Enter en el buscador). Devuelve JSON.</summary>
        [HttpGet]
        [CustomAuthorize("createPurchases", "Compras")]
        public async Task<IActionResult> FindByBarcode([FromQuery] string? code)
        {
            Response<PurchaseProductDTO?> response = await _purchasesService.FindProductByBarcodeAsync(code);
            return Json(new { isSuccess = response.IsSuccess, message = response.Message, item = response.Result });
        }

        /// <summary>
        /// Registro de la compra. Recibe ProductId, cantidad y costo por línea. Quién compra es siempre
        /// el usuario de la sesión. El token antiforgery llega en el encabezado RequestVerificationToken.
        /// </summary>
        [HttpPost]
        [CustomAuthorize("createPurchases", "Compras")]
        public async Task<IActionResult> Create([FromBody] CreatePurchaseDTO? dto)
        {
            if (dto is null)
                return Json(new { isSuccess = false, message = "Los datos de la compra no son válidos" });

            string? userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
            if (string.IsNullOrEmpty(userId))
                return Json(new { isSuccess = false, message = "Tu sesión expiró, vuelve a iniciar sesión" });

            Response<PurchaseResultDTO> response = await _purchasesService.CreateAsync(dto, userId);

            if (!response.IsSuccess)
                return Json(new { isSuccess = false, message = response.Message });

            return Json(new
            {
                isSuccess = true,
                message = response.Message,
                purchaseId = response.Result!.Id,
                number = response.Result.Number,
                total = response.Result.Total,
                alreadyRegistered = response.Result.AlreadyRegistered,
                warning = response.Result.Warning
            });
        }
    }
}
