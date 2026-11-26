using AspNetCoreHero.ToastNotification.Abstractions;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using SistemadeVentasSupermercado.Web.Core;
using SistemadeVentasSupermercado.Web.Core.Attributes;
using SistemadeVentasSupermercado.Web.DTOs;
using SistemadeVentasSupermercado.Web.Services.Abstractions;
using System.Security.Claims;

namespace SistemadeVentasSupermercado.Web.Controllers
{
    [Authorize]
    public class SaleReturnsController : Controller
    {
        private readonly ISaleReturnsService _returnsService;
        private readonly IUsersService _usersService;
        private readonly INotyfService _notyfService;
        private readonly IAuthorizationPinService _pinService;
        private readonly BusinessOptions _business;

        public SaleReturnsController(ISaleReturnsService returnsService, IUsersService usersService,
            INotyfService notyfService, IAuthorizationPinService pinService, IOptions<BusinessOptions> business)
        {
            _pinService = pinService;
            _returnsService = returnsService;
            _usersService = usersService;
            _notyfService = notyfService;
            _business = business.Value;
        }

        /// <summary>Formulario: lo que todavía se puede devolver de cada línea de la venta.</summary>
        [HttpGet]
        [CustomAuthorize("createReturns", "Ventas")]
        public async Task<IActionResult> Create([FromQuery] Guid saleId)
        {
            Response<ReturnableSaleDTO> response = await _returnsService.GetReturnableAsync(saleId);

            if (!response.IsSuccess)
            {
                _notyfService.Error(response.Message);
                return RedirectToAction("Index", "Sales");
            }

            if (response.Result!.Lines.All(l => l.RemainingQuantity <= 0))
            {
                _notyfService.Warning("Ya se devolvió todo lo de esta venta");
                return RedirectToAction("Details", "Sales", new { id = saleId });
            }

            return View(response.Result);
        }

        [HttpPost]
        [CustomAuthorize("createReturns", "Ventas")]
        public async Task<IActionResult> Create([FromQuery] Guid saleId, [FromForm] CreateSaleReturnDTO dto)
        {
            // Quien devuelve es siempre el usuario de la sesión.
            string userId = User.FindFirstValue(ClaimTypes.NameIdentifier) ?? string.Empty;
            Response<SaleReturnDetailDTO> response = await _returnsService.CreateAsync(saleId, dto, userId);

            if (!response.IsSuccess)
            {
                _notyfService.Error(response.Message);
                return RedirectToAction(nameof(Create), new { saleId });
            }

            _notyfService.Success(response.Message);
            return RedirectToAction(nameof(Details), new { id = response.Result!.Id });
        }

        // ---------- Devolución que autoriza un supervisor ----------

        [HttpGet]
        [CustomAuthorize("createSales", "Ventas")]
        public async Task<IActionResult> CreateWithAuthorization([FromQuery] Guid saleId)
            => await AuthorizationFormAsync(saleId);

        /// <summary>Devolución que autoriza un supervisor con su PIN. SIEMPRE lo exige.</summary>
        [HttpPost]
        [CustomAuthorize("createSales", "Ventas")]
        public async Task<IActionResult> CreateWithAuthorization([FromQuery] Guid saleId,
            [FromForm] CreateSaleReturnDTO dto, [FromForm] string? authorizerUserId,
            [FromForm] string? authorizationPin)
        {
            Response<AuthorizerDTO> authorizer = await _pinService.VerifyAsync(
                SupervisorOperation.CreateReturn, authorizerUserId, authorizationPin, User.Identity!.Name!);
            string? error = authorizer.Message;

            if (authorizer.IsSuccess)
            {
                string userId = User.FindFirstValue(ClaimTypes.NameIdentifier) ?? string.Empty;
                Response<SaleReturnDetailDTO> response =
                    await _returnsService.CreateAsync(saleId, dto, userId, authorizer.Result!.UserId);

                if (response.IsSuccess)
                {
                    _notyfService.Success(response.Message);
                    return RedirectToAction(nameof(Details), new { id = response.Result!.Id });
                }

                error = response.Message;
            }

            // Se vuelve a mostrar con las cantidades y el motivo escritos (menos el PIN).
            ModelState.AddModelError(string.Empty, error ?? "No se pudo registrar la devolución.");
            ViewBag.Posted = dto;
            ViewBag.SelectedAuthorizer = authorizerUserId;
            return await AuthorizationFormAsync(saleId);
        }

        private async Task<IActionResult> AuthorizationFormAsync(Guid saleId)
        {
            Response<ReturnableSaleDTO> response = await _returnsService.GetReturnableAsync(saleId);

            if (!response.IsSuccess)
            {
                _notyfService.Error(response.Message);
                return RedirectToAction("Assist", "Sales");
            }

            if (response.Result!.Lines.All(l => l.RemainingQuantity <= 0))
            {
                _notyfService.Warning("Ya se devolvió todo lo de esta venta");
                return RedirectToAction("Assist", "Sales", new { number = response.Result.SaleNumber });
            }

            ViewBag.RequiresAuthorization = true;
            ViewBag.Authorizers = await _pinService.GetAuthorizersAsync(SupervisorOperation.CreateReturn, User.Identity!.Name!);
            return View(nameof(Create), response.Result);
        }

        /// <summary>
        /// Comprobante de devolución: lo ve quien puede hacer devoluciones o ver ventas, y el cajero que
        /// la hizo (con autorización). Esta combinación no cabe en un solo [CustomAuthorize].
        /// </summary>
        [HttpGet]
        public async Task<IActionResult> Details([FromRoute] Guid id)
        {
            bool allowed = await _usersService.CurrentUserIsAuthorizedAsync("createReturns", "Ventas")
                        || await _usersService.CurrentUserIsAuthorizedAsync("showSales", "Ventas");
            bool couldBeOwn = !allowed && await _usersService.CurrentUserIsAuthorizedAsync("createSales", "Ventas");
            if (!allowed && !couldBeOwn)
                return Forbid();

            Response<SaleReturnDetailDTO> response = await _returnsService.GetOneAsync(id);

            // Si no es suya, se niega igual exista o no.
            if (!allowed && (!response.IsSuccess || response.Result!.UserId != User.FindFirstValue(ClaimTypes.NameIdentifier)))
                return Forbid();

            if (!response.IsSuccess)
            {
                _notyfService.Error(response.Message);
                return RedirectToAction("Index", "Home");
            }

            ViewBag.Business = _business;
            return View(response.Result);
        }
    }
}
