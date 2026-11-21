using AspNetCoreHero.ToastNotification.Abstractions;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using SistemadeVentasSupermercado.Web.Core;
using SistemadeVentasSupermercado.Web.Core.Attributes;
using SistemadeVentasSupermercado.Web.Core.Pagination;
using SistemadeVentasSupermercado.Web.Data.Entities;
using SistemadeVentasSupermercado.Web.DTOs;
using SistemadeVentasSupermercado.Web.Services.Abstractions;
using System.Security.Claims;

namespace SistemadeVentasSupermercado.Web.Controllers
{
    [Authorize]
    public class CashSessionsController : Controller
    {
        private readonly ICashSessionService _cashService;
        private readonly IUsersService _usersService;
        private readonly INotyfService _notyfService;
        private readonly BusinessOptions _business;

        public CashSessionsController(ICashSessionService cashService, IUsersService usersService,
            INotyfService notyfService, IOptions<BusinessOptions> business)
        {
            _cashService = cashService;
            _usersService = usersService;
            _notyfService = notyfService;
            _business = business.Value;
        }

        // El usuario siempre sale de la sesión, nunca del formulario.
        private string CurrentUserId => User.FindFirstValue(ClaimTypes.NameIdentifier) ?? string.Empty;

        // ---------- Mi caja (cajero) ----------

        /// <summary>Si no hay caja abierta muestra el formulario de apertura; si la hay, el de cierre.</summary>
        [HttpGet]
        [CustomAuthorize("manageCashSessions", "Caja")]
        public async Task<IActionResult> Current()
        {
            Response<CashSessionDTO?> response = await _cashService.GetCurrentAsync(CurrentUserId);

            if (!response.IsSuccess)
            {
                _notyfService.Error(response.Message);
                return RedirectToAction("Index", "Home");
            }

            return View(response.Result);
        }

        [HttpPost]
        [CustomAuthorize("manageCashSessions", "Caja")]
        public async Task<IActionResult> Open([FromForm] OpenCashSessionDTO dto)
        {
            if (!ModelState.IsValid)
            {
                _notyfService.Error("El monto inicial no es válido");
                return RedirectToAction(nameof(Current));
            }

            Response<CashSessionDTO> response = await _cashService.OpenAsync(CurrentUserId, dto);

            if (!response.IsSuccess)
            {
                _notyfService.Error(response.Message);
                return RedirectToAction(nameof(Current));
            }

            _notyfService.Success(response.Message);

            // Si puede vender, va directo al punto de venta.
            if (await _usersService.CurrentUserIsAuthorizedAsync("createSales", "Ventas"))
                return RedirectToAction("Create", "Sales");

            return RedirectToAction(nameof(Current));
        }

        [HttpPost]
        [CustomAuthorize("manageCashSessions", "Caja")]
        public async Task<IActionResult> Close([FromForm] CloseCashSessionDTO dto)
        {
            if (!ModelState.IsValid)
            {
                _notyfService.Error("Revisa el efectivo contado y las observaciones");
                return RedirectToAction(nameof(Current));
            }

            Response<CashSessionDTO?> current = await _cashService.GetCurrentAsync(CurrentUserId);
            if (!current.IsSuccess || current.Result is null)
            {
                _notyfService.Error("No tienes una caja abierta");
                return RedirectToAction(nameof(Current));
            }

            Response<CashSessionDTO> response = await _cashService.CloseAsync(current.Result.Id, dto, CurrentUserId, canCloseOthers: false);

            if (!response.IsSuccess)
            {
                _notyfService.Error(response.Message);
                return RedirectToAction(nameof(Current));
            }

            _notyfService.Success(response.Message);
            return RedirectToAction(nameof(Report), new { id = response.Result!.Id });
        }

        // ---------- Supervisión ----------

        [HttpGet]
        [CustomAuthorize("showCashSessions", "Caja")]
        public async Task<IActionResult> Index([FromQuery] PaginationRequest request)
        {
            Response<PaginationResponse<CashSessionDTO>> response = await _cashService.GetPaginatedListAsync(request);

            if (!response.IsSuccess)
            {
                _notyfService.Error(response.Message);
                return RedirectToAction("Index", "Home");
            }

            return View(response.Result);
        }

        /// <summary>Un supervisor cierra la caja que otro cajero olvidó cerrar.</summary>
        [HttpPost]
        [CustomAuthorize("showCashSessions", "Caja")]
        public async Task<IActionResult> CloseOther([FromRoute] Guid id, [FromForm] CloseCashSessionDTO dto)
        {
            if (!ModelState.IsValid)
            {
                _notyfService.Error("Revisa el efectivo contado y las observaciones");
                return RedirectToAction(nameof(Report), new { id });
            }

            Response<CashSessionDTO> response = await _cashService.CloseAsync(id, dto, CurrentUserId, canCloseOthers: true);

            if (response.IsSuccess)
                _notyfService.Success(response.Message);
            else
                _notyfService.Error(response.Message);

            return RedirectToAction(nameof(Report), new { id });
        }

        // ---------- Reporte de cierre ----------

        /// <summary>
        /// Lo ve quien tiene showCashSessions (cualquier turno), o el propio cajero cuando
        /// su turno ya está CERRADO (arqueo ciego: mientras está abierta no ve el esperado).
        /// Este permiso combinado no cabe en un solo [CustomAuthorize], por eso se revisa aquí.
        /// </summary>
        [HttpGet]
        public async Task<IActionResult> Report([FromRoute] Guid id)
        {
            Response<CashSessionSummaryDTO> response = await _cashService.GetSummaryAsync(id);

            if (!response.IsSuccess)
            {
                _notyfService.Error(response.Message);
                return RedirectToAction("Index", "Home");
            }

            CashSessionDTO session = response.Result!.Session;
            bool isSupervisor = await _usersService.CurrentUserIsAuthorizedAsync("showCashSessions", "Caja");
            bool isOwnerAfterClose = session.UserId == CurrentUserId
                                  && session.Status == CashSessionStatus.Closed
                                  && await _usersService.CurrentUserIsAuthorizedAsync("manageCashSessions", "Caja");

            if (!isSupervisor && !isOwnerAfterClose)
                return Forbid();

            ViewBag.Business = _business;
            ViewBag.IsSupervisor = isSupervisor;
            return View(response.Result);
        }
    }
}
