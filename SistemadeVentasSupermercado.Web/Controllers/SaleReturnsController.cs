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
        private readonly BusinessOptions _business;

        public SaleReturnsController(ISaleReturnsService returnsService, IUsersService usersService,
            INotyfService notyfService, IOptions<BusinessOptions> business)
        {
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

        /// <summary>
        /// Comprobante de devolución: lo ve quien puede hacer devoluciones o ver ventas.
        /// Este "uno u otro permiso" no cabe en un solo [CustomAuthorize], por eso se revisa aquí.
        /// </summary>
        [HttpGet]
        public async Task<IActionResult> Details([FromRoute] Guid id)
        {
            bool allowed = await _usersService.CurrentUserIsAuthorizedAsync("createReturns", "Ventas")
                        || await _usersService.CurrentUserIsAuthorizedAsync("showSales", "Ventas");
            if (!allowed)
                return Forbid();

            Response<SaleReturnDetailDTO> response = await _returnsService.GetOneAsync(id);

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
