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
        private readonly IUsersService _usersService;
        private readonly IAuthorizationPinService _pinService;
        private readonly BusinessOptions _business;

        public SalesController(ISalesService salesService, ICashSessionService cashService,
            INotyfService notyfService, IUsersService usersService, IAuthorizationPinService pinService,
            IOptions<BusinessOptions> business)
        {
            _usersService = usersService;
            _pinService = pinService;
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

        // ---------- Anulación ----------

        [HttpPost]
        [CustomAuthorize("cancelSales", "Ventas")]
        public async Task<IActionResult> Cancel([FromRoute] Guid id, [FromForm] CancelSaleDTO dto)
        {
            if (!ModelState.IsValid)
            {
                _notyfService.Error("Escribe el motivo de la anulación (máximo 200 caracteres)");
                return await BackToSaleAsync(id);
            }

            // Quien anula es siempre el usuario de la sesión.
            string userId = User.FindFirstValue(ClaimTypes.NameIdentifier) ?? string.Empty;
            Response<SaleDetailDTO> response = await _salesService.CancelAsync(id, dto, userId);

            if (response.IsSuccess)
                _notyfService.Success(response.Message);
            else
                _notyfService.Error(response.Message);

            return await BackToSaleAsync(id);
        }

        /// <summary>El comprobante si puede ver ventas; si no, "Anular o devolver" con esa venta.</summary>
        private async Task<IActionResult> BackToSaleAsync(Guid id)
        {
            if (await _usersService.CurrentUserIsAuthorizedAsync("showSales", "Ventas"))
                return RedirectToAction(nameof(Details), new { id });

            Response<SaleDetailDTO> sale = await _salesService.GetOneAsync(id);
            return RedirectToAction(nameof(Assist), new { number = sale.Result?.Number });
        }

        // ---------- Anular o devolver (con autorización de supervisor) ----------

        /// <summary>
        /// El cajero busca UNA venta por el número del comprobante. A propósito no hay lista: con la lista
        /// de sus ventas podría calcular el efectivo esperado antes del arqueo.
        /// </summary>
        [HttpGet]
        [CustomAuthorize("createSales", "Ventas")]
        public async Task<IActionResult> Assist([FromQuery] int? number)
        {
            if (number is null)
            {
                // Un texto que no es un número llega como null.
                if (!ModelState.IsValid)
                    ModelState.AddModelError(string.Empty, "Escribe el número del comprobante (solo números).");
                return View((SaleDetailDTO?)null);
            }

            Guid? id = await _salesService.FindIdByNumberAsync(number.Value);
            if (id is null)
            {
                ModelState.AddModelError(string.Empty, $"No existe la venta N.º {number}.");
                return View((SaleDetailDTO?)null);
            }

            return await AssistViewAsync(id.Value);
        }

        private async Task<IActionResult> AssistViewAsync(Guid id)
        {
            Response<SaleDetailDTO> response = await _salesService.GetOneAsync(id);

            if (!response.IsSuccess)
            {
                _notyfService.Error(response.Message);
                return RedirectToAction(nameof(Assist));
            }

            ViewBag.Business = _business;
            ViewBag.Authorizers = await _pinService.GetAuthorizersAsync(SupervisorOperation.CancelSale, User.Identity!.Name!);
            return View(nameof(Assist), response.Result);
        }

        /// <summary>Anulación que autoriza un supervisor con su PIN. SIEMPRE lo exige.</summary>
        [HttpPost]
        [CustomAuthorize("createSales", "Ventas")]
        public async Task<IActionResult> CancelWithAuthorization([FromRoute] Guid id, [FromForm] CancelSaleDTO dto,
            [FromForm] string? authorizerUserId, [FromForm] string? authorizationPin)
        {
            Response<SaleDetailDTO> sale = await _salesService.GetOneAsync(id);

            if (!sale.IsSuccess)
            {
                _notyfService.Error(sale.Message);
                return RedirectToAction(nameof(Assist));
            }

            // Se revisa antes del PIN, para no pedirlo para una anulación que no se puede hacer.
            string? error = !ModelState.IsValid ? "Escribe el motivo de la anulación (máximo 200 caracteres)."
                : !sale.Result!.CanBeCancelled
                    ? "Esta venta ya no se puede anular (está anulada, tiene devoluciones o su turno de caja cerró)."
                    : null;

            if (error is null)
            {
                Response<AuthorizerDTO> authorizer = await _pinService.VerifyAsync(
                    SupervisorOperation.CancelSale, authorizerUserId, authorizationPin, User.Identity!.Name!);

                if (authorizer.IsSuccess)
                {
                    string userId = User.FindFirstValue(ClaimTypes.NameIdentifier) ?? string.Empty;
                    Response<SaleDetailDTO> response =
                        await _salesService.CancelAsync(id, dto, userId, authorizer.Result!.UserId);

                    if (response.IsSuccess)
                    {
                        _notyfService.Success(response.Message);
                        return RedirectToAction(nameof(Assist), new { number = sale.Result!.Number });
                    }

                    error = response.Message;
                }
                else
                {
                    error = authorizer.Message;
                }
            }

            // Se vuelve a mostrar con lo que se escribió (menos el PIN).
            ModelState.AddModelError(string.Empty, error ?? "No se pudo anular la venta.");
            ViewBag.CancelReason = dto.Reason;
            ViewBag.SelectedAuthorizer = authorizerUserId;
            return await AssistViewAsync(id);
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

        /// <summary>Lo llama el punto de venta al escanear (Enter en el buscador). Devuelve JSON.</summary>
        [HttpGet]
        [CustomAuthorize("createSales", "Ventas")]
        public async Task<IActionResult> FindByBarcode([FromQuery] string? code)
        {
            Response<BarcodeLookupDTO> response = await _salesService.FindByBarcodeAsync(code);
            return Json(new
            {
                isSuccess = response.IsSuccess,
                message = response.Message,
                status = response.Result?.Status.ToString(),
                item = response.Result?.Product
            });
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
                total = response.Result.Total,
                alreadyRegistered = response.Result.AlreadyRegistered
            });
        }
    }
}
