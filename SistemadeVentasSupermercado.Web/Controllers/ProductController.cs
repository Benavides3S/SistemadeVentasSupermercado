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
    }
}
