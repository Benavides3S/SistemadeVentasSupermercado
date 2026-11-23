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
    public class SuppliersController : Controller
    {
        private readonly ISupplierService _supplierService;
        private readonly INotyfService _notyfService;

        public SuppliersController(ISupplierService supplierService, INotyfService notyfService)
        {
            _supplierService = supplierService;
            _notyfService = notyfService;
        }

        [HttpGet]
        [CustomAuthorize("showSuppliers", "Proveedores")]
        public async Task<IActionResult> Index([FromQuery] PaginationRequest request)
        {
            Response<PaginationResponse<SupplierDTO>> response = await _supplierService.GetPaginatedListAsync(request);

            if (!response.IsSuccess)
            {
                _notyfService.Error(response.Message);
                return RedirectToAction("Index", "Home");
            }

            return View(response.Result);
        }

        [HttpGet]
        [CustomAuthorize("createSuppliers", "Proveedores")]
        public IActionResult Create()
        {
            return View(new SupplierDTO());
        }

        [HttpPost]
        [CustomAuthorize("createSuppliers", "Proveedores")]
        public async Task<IActionResult> Create([FromForm] SupplierDTO dto)
        {
            if (!ModelState.IsValid)
            {
                _notyfService.Error("Debe ajustar los errores de validación");
                return View(dto);
            }

            Response<SupplierDTO> response = await _supplierService.CreateAsync(dto);

            if (!response.IsSuccess)
            {
                _notyfService.Error(response.Message);
                return View(dto);
            }

            _notyfService.Success(response.Message);
            return RedirectToAction(nameof(Index));
        }

        [HttpGet]
        [CustomAuthorize("updateSuppliers", "Proveedores")]
        public async Task<IActionResult> Edit([FromRoute] Guid id)
        {
            Response<SupplierDTO> response = await _supplierService.GetOneAsync(id);

            if (!response.IsSuccess)
            {
                _notyfService.Error(response.Message);
                return RedirectToAction(nameof(Index));
            }

            return View(response.Result);
        }

        [HttpPost]
        [CustomAuthorize("updateSuppliers", "Proveedores")]
        public async Task<IActionResult> Edit([FromForm] SupplierDTO dto)
        {
            if (!ModelState.IsValid)
            {
                _notyfService.Error("Debe ajustar los errores de validación");
                return View(dto);
            }

            Response<SupplierDTO> response = await _supplierService.EditAsync(dto);

            if (!response.IsSuccess)
            {
                _notyfService.Error(response.Message);
                return View(dto);
            }

            _notyfService.Success(response.Message);
            return RedirectToAction(nameof(Index));
        }

        [HttpPost]
        [CustomAuthorize("deleteSuppliers", "Proveedores")]
        public async Task<IActionResult> Delete([FromRoute] Guid id)
        {
            Response<object> response = await _supplierService.DeleteAsync(id);

            if (!response.IsSuccess)
                _notyfService.Error(response.Message);
            else
                _notyfService.Success(response.Message);

            return RedirectToAction(nameof(Index));
        }
    }
}
