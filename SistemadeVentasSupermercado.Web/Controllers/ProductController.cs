using AspNetCoreHero.ToastNotification.Abstractions;
using Microsoft.AspNetCore.Mvc;
using SistemadeVentasSupermercado.Web.Core;
using SistemadeVentasSupermercado.Web.DTOs;
using SistemadeVentasSupermercado.Web.Services.Abstractions;

namespace SistemadeVentasSupermercado.Web.Controllers
{
    public class ProductController : Controller

    {
        private readonly IProductService _productService;
        private readonly INotyfService _notyfService;

        public ProductController(IProductService productService, INotyfService notyfService)
        {
            _productService = productService;
            _notyfService = notyfService;
        }
        [HttpGet]
        public async Task<IActionResult> Index()
        {
            Response<List<ProductDTO>> response = await _productService.GetListAsync();
            if (!response.IsSuccess)
            {
                _notyfService.Error(response.Message);
                return RedirectToAction("Index", "Home");
            }
            return View(response.Result);
        }

        [HttpGet]
        public IActionResult Create()
        {
            return View();
        }
        [HttpPost]
        
        public async Task<IActionResult> Create([FromForm] ProductDTO dto)
        {
            
            if (!ModelState.IsValid)
            {
                _notyfService.Error("debe ajustar los errores de validacion");
                return View(dto);
            }
            Response<ProductDTO> response = await _productService.CreateAsync(dto);
            if (!response.IsSuccess)
            {
                _notyfService.Error(response.Message);
                return View(dto);
            }
            _notyfService.Success(response.Message);
            return RedirectToAction(nameof(Index));
        }
        [HttpGet]
        public async Task<IActionResult> Edit([FromRoute] Guid id)
        {
            Response<ProductDTO> response = await _productService.GetOneAsync(id);

            if (!response.IsSuccess)
            {
                _notyfService.Error(response.Message);
                return RedirectToAction(nameof(Index));
            }

            return View(response.Result);
        }

        [HttpPost]
        public async Task<IActionResult> Edit([FromForm] ProductDTO dto)
        {
            
            if (!ModelState.IsValid)
            {
                _notyfService.Error("Debe ajustar los errores de validación");
                return View(dto);
            }

            
            Response<ProductDTO> response = await _productService.EditAsync(dto);

           
            if (!response.IsSuccess)
            {
              
                _notyfService.Error(response.Message);
                return View(dto);
            }

           
            _notyfService.Success(response.Message);
            return RedirectToAction(nameof(Index));
        }
        [HttpPost]
        public async Task<IActionResult> Delete([FromRoute] Guid id)
        {
        
            Response<object> response = await _productService.DeleteAsync(id);

            
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
