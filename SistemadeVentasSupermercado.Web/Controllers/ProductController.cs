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
    public class ProductController : Controller
    {
        private readonly IProductService _productService;
        private readonly INotyfService _notyfService;
        private readonly ICombosHelper _combosHelper;

        public ProductController(IProductService productService, INotyfService notyfService, ICombosHelper combosHelper)
        {
            _productService = productService;
            _notyfService = notyfService;
            _combosHelper = combosHelper;
        }

        private async Task LoadCategoriesAsync(Guid? selectedId = null)
        {
            ViewBag.Categories = await _combosHelper.GetComboCategories(selectedId);
        }

        [HttpGet]
        [CustomAuthorize(permission: "showProducts", module: "Productos")]
        public async Task<IActionResult> Index([FromQuery] PaginationRequest request)
        {
            Response<PaginationResponse<ProductDTO>> response = await _productService.GetPaginatedListAsync(request);

            if (!response.IsSuccess)
            {
                _notyfService.Error(response.Message);
                return RedirectToAction("Index", "Home");
            }

            return View(response.Result);
        }

        [HttpGet]
        [CustomAuthorize("createProducts", "Productos")]
        public async Task<IActionResult> Create()
        {
            await LoadCategoriesAsync();
            return View();
        }

        [HttpPost]
        [CustomAuthorize("createProducts", "Productos")]
        public async Task<IActionResult> Create([FromForm] ProductDTO dto)
        {
            if (!ModelState.IsValid)
            {
                _notyfService.Error("Debe ajustar los errores de validación");
                await LoadCategoriesAsync(dto.CategoryId);
                return View(dto);
            }

            Response<ProductDTO> response = await _productService.CreateAsync(dto, User.FindFirstValue(ClaimTypes.NameIdentifier));

            if (!response.IsSuccess)
            {
                string detalle = response.Errors is null ? "" : string.Join(" | ", response.Errors);
                _notyfService.Error($"{response.Message} {detalle}");
                ModelState.AddModelError(string.Empty, $"{response.Message} {detalle}".Trim());
                await LoadCategoriesAsync(dto.CategoryId);
                return View(dto);
            }

            _notyfService.Success(response.Message);
            return RedirectToAction(nameof(Index));
        }

        [HttpGet]
        [CustomAuthorize("updateProducts", "Productos")]
        public async Task<IActionResult> Edit([FromRoute] Guid id)
        {
            Response<ProductDTO> response = await _productService.GetOneAsync(id);

            if (!response.IsSuccess)
            {
                _notyfService.Error(response.Message);
                return RedirectToAction(nameof(Index));
            }

            await LoadCategoriesAsync(response.Result!.CategoryId);
            return View(response.Result);
        }

        [HttpPost]
        [CustomAuthorize("updateProducts", "Productos")]
        public async Task<IActionResult> Edit([FromForm] ProductDTO dto)
        {
            if (!ModelState.IsValid)
            {
                _notyfService.Error("Debe ajustar los errores de validación");
                await LoadCategoriesAsync(dto.CategoryId);
                return View(dto);
            }

            Response<ProductDTO> response = await _productService.EditAsync(dto, User.FindFirstValue(ClaimTypes.NameIdentifier));

            if (!response.IsSuccess)
            {
                string detalle = response.Errors is null ? "" : string.Join(" | ", response.Errors);
                _notyfService.Error($"{response.Message} {detalle}");
                ModelState.AddModelError(string.Empty, $"{response.Message} {detalle}".Trim());
                await LoadCategoriesAsync(dto.CategoryId);
                return View(dto);
            }

            _notyfService.Success(response.Message);
            return RedirectToAction(nameof(Index));
        }

        [HttpPost]
        [CustomAuthorize("deleteProducts", "Productos")]
        public async Task<IActionResult> Delete([FromRoute] Guid id)
        {
            Response<object> response = await _productService.DeleteAsync(id);

            if (!response.IsSuccess)
                _notyfService.Error(response.Message);
            else
                _notyfService.Success(response.Message);

            return RedirectToAction(nameof(Index));
        }
    }
}