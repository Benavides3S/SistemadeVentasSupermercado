using AspNetCoreHero.ToastNotification.Abstractions;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using SistemadeVentasSupermercado.Web.Core;
using SistemadeVentasSupermercado.Web.Core.Attributes;
using SistemadeVentasSupermercado.Web.Core.Pagination;
using SistemadeVentasSupermercado.Web.DTOs;
using SistemadeVentasSupermercado.Web.Services.Abstractions;
using System.Security.Claims;

namespace SistemadeVentasSupermercado.Web.Controllers
{
    [Authorize]
    public class SalesController : Controller
    {
        private readonly ISalesService _salesService;
        private readonly ICashSessionService _cashService;
        private readonly INotyfService _notyfService;
        private readonly BusinessOptions _business;

        public SalesController(ISalesService salesService, ICashSessionService cashService,
            INotyfService notyfService, IOptions<BusinessOptions> business)
        {
            _salesService = salesService;
            _cashService = cashService;
            _notyfService = notyfService;
            _business = business.Value;
        }

        // ---------- Historial y comprobante ----------

        [HttpGet]
        [CustomAuthorize("showSales", "Ventas")]
        public async Task<IActionResult> Index([FromQuery] PaginationRequest request)
        {
            Response<PaginationResponse<SaleListItemDTO>> response = await _salesService.GetPaginatedListAsync(request);

            if (!response.IsSuccess)
            {
                _notyfService.Error(response.Message);
                return RedirectToAction("Index", "Home");
            }

            return View(response.Result);
        }

        [HttpGet]
        [CustomAuthorize("showSales", "Ventas")]
        public async Task<IActionResult> Details([FromRoute] Guid id)
        {
            Response<SaleDetailDTO> response = await _salesService.GetOneAsync(id);

            if (!response.IsSuccess)
            {
                _notyfService.Error(response.Message);
                return RedirectToAction(nameof(Index));
            }

            ViewBag.Business = _business;
            return View(response.Result);
        }

        // ---------- Punto de venta ----------

        [HttpGet]
        [CustomAuthorize("createSales", "Ventas")]
        public async Task<IActionResult> Create()
        {
            // Sin caja abierta no se puede cobrar: se envía al cajero a abrirla primero.
            // (CreateAsync también lo verifica en el servidor.)
            string userId = User.FindFirstValue(ClaimTypes.NameIdentifier) ?? string.Empty;
            Response<CashSessionDTO?> current = await _cashService.GetCurrentAsync(userId);

            if (current.IsSuccess && current.Result is null)
            {
                _notyfService.Warning("Abre la caja para empezar a vender");
                return RedirectToAction("Current", "CashSessions");
            }

            ViewBag.CashSession = current.Result;
            return View();
        }

        /// <summary>Lo llama el buscador del punto de venta (fetch). Devuelve JSON.</summary>
        [HttpGet]
        [CustomAuthorize("createSales", "Ventas")]
        public async Task<IActionResult> SearchProducts([FromQuery] string? term)
        {
            Response<List<PosProductDTO>> response = await _salesService.SearchProductsAsync(term);
            return Json(new { isSuccess = response.IsSuccess, message = response.Message, items = response.Result });
        }

        [HttpGet]
        [CustomAuthorize("createSales", "Ventas")]
        public async Task<IActionResult> SearchClients([FromQuery] string? term)
        {
            Response<List<PosClientDTO>> response = await _salesService.SearchClientsAsync(term);
            return Json(new { isSuccess = response.IsSuccess, message = response.Message, items = response.Result });
        }

        /// <summary>
        /// Cobro. Recibe solo ProductId y cantidad por línea: los precios salen de la base de datos.
        /// El token antiforgery llega en el encabezado RequestVerificationToken (filtro global).
        /// </summary>
        [HttpPost]
        [CustomAuthorize("createSales", "Ventas")]
        public async Task<IActionResult> Create([FromBody] CreateSaleDTO? dto)
        {
            if (dto is null)
                return Json(new { isSuccess = false, message = "Los datos de la venta no son válidos" });

            // El cajero es siempre el usuario de la sesión, nunca un dato enviado por el navegador.
            string? userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
            if (string.IsNullOrEmpty(userId))
                return Json(new { isSuccess = false, message = "Tu sesión expiró, vuelve a iniciar sesión" });

            Response<SaleResultDTO> response = await _salesService.CreateAsync(dto, userId);

            if (!response.IsSuccess)
                return Json(new { isSuccess = false, message = response.Message });

            return Json(new
            {
                isSuccess = true,
                message = response.Message,
                saleId = response.Result!.Id,
                number = response.Result.Number,
                total = response.Result.Total
            });
        }
    }
}
