using AspNetCoreHero.ToastNotification.Abstractions;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using SistemadeVentasSupermercado.Web.Core.Attributes;
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

        // 📋 LISTAR CLIENTES
        [HttpGet]
        [CustomAuthorize(permission: "showClient", module: "Clientes")]
        public async Task<IActionResult> Index([FromQuery] PaginationRequest request)
        {
            Response<PaginationResponse<ClientDTO>> response = await _clientService.GetPaginatedListAsync(request);

            if (!response.IsSuccess)
            {
                _notyfService.Error(response.Message);
                return RedirectToAction("Index", "Home");
            }

            return View(response.Result);
        }

        // ➕ CREAR CLIENTE
        [HttpGet]
        [CustomAuthorize("Clientes", "createClient")]
        public IActionResult Create()
        {
            return View();
        }

        [HttpPost]
        [CustomAuthorize("createClient", "Clientes")]
        public async Task<IActionResult> Create([FromForm] ClientDTO dto)
        {
            if (!ModelState.IsValid)
            {
                _notyfService.Error("Debe ajustar los errores de validación");
                return View(dto);
            }

            Response<ClientDTO> response = await _clientService.CreateAsync(dto);

            if (!response.IsSuccess)
            {
                _notyfService.Error(response.Message);
                return View(dto);
            }

            _notyfService.Success(response.Message);
            return RedirectToAction(nameof(Index));
        }

        // ✏️ EDITAR CLIENTE
        [HttpGet]
        [CustomAuthorize("updateClient", "Clientes")]
        public async Task<IActionResult> Edit([FromRoute] Guid id)
        {
            Response<ClientDTO> response = await _clientService.GetOneAsync(id);

            if (!response.IsSuccess)
            {
                _notyfService.Error(response.Message);
                return RedirectToAction(nameof(Index));
            }

            return View(response.Result);
        }

        [HttpPost]
        [CustomAuthorize("updateClient", "Clientes")]
        public async Task<IActionResult> Edit([FromForm] ClientDTO dto)
        {
            if (!ModelState.IsValid)
            {
                _notyfService.Error("Debe ajustar los errores de validación");
                return View(dto);
            }

            Response<ClientDTO> response = await _clientService.EditAsync(dto);

            if (!response.IsSuccess)
            {
                _notyfService.Error(response.Message);
                return View(dto);
            }

            _notyfService.Success(response.Message);
            return RedirectToAction(nameof(Index));
        }

        // 🗑️ ELIMINAR CLIENTE
        [HttpPost]
        [CustomAuthorize("deleteClient", "Clientes")]
        public async Task<IActionResult> Delete([FromRoute] Guid id)
        {
            Response<object> response = await _clientService.DeleteAsync(id);

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
