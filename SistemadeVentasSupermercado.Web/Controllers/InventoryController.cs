using AspNetCoreHero.ToastNotification.Abstractions;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using SistemadeVentasSupermercado.Web.Core;
using SistemadeVentasSupermercado.Web.Core.Attributes;
using SistemadeVentasSupermercado.Web.Core.Pagination;
using SistemadeVentasSupermercado.Web.DTOs;
using SistemadeVentasSupermercado.Web.Services.Abstractions;
using System.Security.Claims;

namespace SistemadeVentasSupermercado.Web.Controllers
{
    [Authorize]
    public class InventoryController : Controller
    {
        private readonly IInventoryService _inventoryService;
        private readonly INotyfService _notyfService;

        public InventoryController(IInventoryService inventoryService, INotyfService notyfService)
        {
            _inventoryService = inventoryService;
            _notyfService = notyfService;
        }

        // ---------- Historial ----------

        /// <summary>
        /// Historial de movimientos. Con ?ProductId= muestra el historial de ese producto
        /// (con su stock actual y el botón para ajustarlo).
        /// </summary>
        [HttpGet]
        [CustomAuthorize("showInventory", "Inventario")]
        public async Task<IActionResult> Index([FromQuery] InventoryQueryDTO query)
        {
            Response<PaginationResponse<InventoryMovementDTO>> response = await _inventoryService.GetMovementsAsync(query);

            if (!response.IsSuccess)
            {
                _notyfService.Error(response.Message);
                // Con las fechas al revés se vuelve a mostrar la pantalla sin esos filtros.
                query.From = null;
                query.To = null;
                response = await _inventoryService.GetMovementsAsync(query);
                if (!response.IsSuccess)
                    return RedirectToAction("Index", "Home");
            }

            if (query.ProductId.HasValue)
            {
                Response<InventoryProductDTO> product = await _inventoryService.GetProductAsync(query.ProductId.Value);
                if (!product.IsSuccess)
                {
                    _notyfService.Error(product.Message);
                    return RedirectToAction(nameof(Index));
                }
                ViewBag.Product = product.Result;
            }

            ViewBag.Query = query;
            return View(response.Result);
        }

        // ---------- Ajuste de stock ----------

        [HttpGet]
        [CustomAuthorize("adjustInventory", "Inventario")]
        public async Task<IActionResult> Adjust([FromQuery] Guid productId)
        {
            Response<InventoryProductDTO> product = await _inventoryService.GetProductAsync(productId);

            if (!product.IsSuccess)
            {
                _notyfService.Error(product.Message);
                return RedirectToAction("Index", "Product");
            }

            ViewBag.Product = product.Result;
            // El código se genera al abrir el formulario: un doble clic o un reintento no aplica el ajuste dos veces.
            return View(new AdjustInventoryDTO { ProductId = productId, IdempotencyKey = Guid.NewGuid() });
        }

        [HttpPost]
        [CustomAuthorize("adjustInventory", "Inventario")]
        public async Task<IActionResult> Adjust([FromForm] AdjustInventoryDTO dto)
        {
            // Quien ajusta es siempre el usuario de la sesión.
            string? userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
            if (string.IsNullOrEmpty(userId))
                return RedirectToAction("Login", "Account");

            Response<AdjustInventoryResultDTO> response = await _inventoryService.AdjustAsync(dto, userId);

            if (response.IsSuccess)
            {
                _notyfService.Success($"{response.Message}. Stock actual: {DisplayFormat.Quantity(response.Result!.StockAfter)}");
                return RedirectToAction(nameof(Index), new { ProductId = dto.ProductId });
            }

            _notyfService.Error(response.Message);
            ModelState.AddModelError(string.Empty, response.Message);

            Response<InventoryProductDTO> product = await _inventoryService.GetProductAsync(dto.ProductId);
            if (!product.IsSuccess)
                return RedirectToAction("Index", "Product");

            ViewBag.Product = product.Result;
            // El ajuste no se aplicó, así que se conserva el mismo código: reenviarlo sigue siendo seguro.
            return View(dto);
        }
    }
}
