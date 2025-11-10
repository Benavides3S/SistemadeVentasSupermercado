using AspNetCoreHero.ToastNotification.Abstractions;
using Microsoft.AspNetCore.Mvc;
using SistemadeVentasSupermercado.Web.Core;
using SistemadeVentasSupermercado.Web.Core.Pagination;
using SistemadeVentasSupermercado.Web.DTOs;
using SistemadeVentasSupermercado.Web.Services.Abstractions;

namespace SistemadeVentasSupermercado.Web.Controllers
{
    public class ClientController : Controller
    {
        private readonly IClientService _clientService;
        private readonly INotyfService _notyfService;

        public ClientController(IClientService clientService, INotyfService notyfService)
        {
            _clientService = clientService;
            _notyfService = notyfService;
        }

        [HttpGet]
        public async Task<IActionResult> Index([FromQuery] PaginationRequest request)
        {
            var response = await _clientService.GetPaginatedListAsync(request);

            if (!response.IsSuccess)
            {
                _notyfService.Error(response.Message);
                return RedirectToAction("Index", "Home");
            }

            return View(response.Result);
        }

        [HttpGet]
        public IActionResult Create() => View();

        [HttpPost]
        public async Task<IActionResult> Create([FromForm] ClientDTO dto)
        {
            if (!ModelState.IsValid)
            {
                _notyfService.Error("Debe corregir los errores de validación");
                return View(dto);
            }

            var response = await _clientService.CreateAsync(dto);

            if (!response.IsSuccess)
            {
                _notyfService.Error(response.Message);
                return View(dto);
            }

            _notyfService.Success(response.Message);
            return RedirectToAction(nameof(Index));
        }

        [HttpGet]
        public async Task<IActionResult> Edit(Guid id)
        {
            var response = await _clientService.GetOneAsync(id);

            if (!response.IsSuccess)
            {
                _notyfService.Error(response.Message);
                return RedirectToAction(nameof(Index));
            }

            return View(response.Result);
        }

        [HttpPost]
        public async Task<IActionResult> Edit([FromForm] ClientDTO dto)
        {
            if (!ModelState.IsValid)
            {
                _notyfService.Error("Debe corregir los errores de validación");
                return View(dto);
            }

            var response = await _clientService.EditAsync(dto);

            if (!response.IsSuccess)
            {
                _notyfService.Error(response.Message);
                return View(dto);
            }

            _notyfService.Success(response.Message);
            return RedirectToAction(nameof(Index));
        }

        [HttpPost]
        public async Task<IActionResult> Delete(Guid id)
        {
            var response = await _clientService.DeleteAsync(id);

            if (!response.IsSuccess)
                _notyfService.Error(response.Message);
            else
                _notyfService.Success(response.Message);

            return RedirectToAction(nameof(Index));
        }
    }
}
