using AspNetCoreHero.ToastNotification.Abstractions;
using AutoMapper;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Rendering;
using SistemadeVentasSupermercado.Web.Core;
using SistemadeVentasSupermercado.Web.Core.Attributes;
using SistemadeVentasSupermercado.Web.Core.Pagination;
using SistemadeVentasSupermercado.Web.Data.Entities;
using SistemadeVentasSupermercado.Web.DTOs;
using SistemadeVentasSupermercado.Web.Helpers.Abstractions;
using SistemadeVentasSupermercado.Web.Services.Abstractions;

namespace SistemadeVentasSupermercado.Web.Controllers
{
    
    
        public class UsersController : Controller
        {
            private readonly IUsersService _usersService;
            private readonly INotyfService _notifyService;
            private readonly ICombosHelper _combosHelper;
            private readonly IMapper _mapper;

            public UsersController(IUsersService sectionsService, INotyfService notifyService, ICombosHelper combosHelper, IMapper mapper)
            {
                _usersService = sectionsService;
                _notifyService = notifyService;
                _combosHelper = combosHelper;
                _mapper = mapper;
            }

            [HttpGet]
            [CustomAuthorize(permission: "showUsers", module: "Usuarios")]
            public async Task<IActionResult> Index([FromQuery] PaginationRequest request)
            {
                Response<PaginationResponse<UserDTO>> response = await _usersService.GetPaginatedListAsync(request);
                return View(response.Result);
            }

            [HttpGet]
            [CustomAuthorize(permission: "createUsers", module: "Usuarios")]
            public async Task<IActionResult> Create()
            {
                IEnumerable<SelectListItem> items = await _combosHelper.GetComboRoles();

                UserDTO dto = new UserDTO
                {
                   SistemaVentasRoles = items,
                };

                return View(dto);
            }

        [HttpPost]
        [CustomAuthorize(permission: "createUsers", module: "Usuarios")]
        public async Task<IActionResult> Create(UserDTO dto)
        {
            if (!ModelState.IsValid)
            {
                _notifyService.Error("Debe ajustar los errores de validación");
                dto.SistemaVentasRoles = await _combosHelper.GetComboRoles();
                return View(dto);
            }

            Response<UserDTO> response = await _usersService.CreateAsync(dto);

            if (!response.IsSuccess)
            {
                // ✅ Mostrar errores específicos
                _notifyService.Error($"Error: {response.Message}");

                // Log adicional en consola
                Console.WriteLine($"ERROR CREANDO USUARIO: {response.Message}");
                if (response.Errors != null)
                {
                    foreach (var error in response.Errors)
                    {
                        Console.WriteLine($" - {error}");
                    }
                }

                dto.SistemaVentasRoles = await _combosHelper.GetComboRoles();
                return View(dto);
            }

            _notifyService.Success(response.Message);
            return RedirectToAction(nameof(Index));
        }


        [HttpGet]
            [CustomAuthorize(permission: "updateUsers", module: "Usuarios")]
            public async Task<IActionResult> Edit(Guid id)
            {
                if (Guid.Empty.Equals(id))
                {
                    return NotFound();
                }

                User user = await _usersService.GetUserByIdAsync(id);

                if (user is null)
                {
                    return NotFound();
                }

                UserDTO dto = _mapper.Map<UserDTO>(user);
                dto.SistemaVentasRoles = await _combosHelper.GetComboRoles();

                return View(dto);
            }

            [HttpPost]
            [CustomAuthorize(permission: "updateUsers", module: "Usuarios")]
            public async Task<IActionResult> Edit(UserDTO dto)
            {
                if (!ModelState.IsValid)
                {
                    _notifyService.Error("Debe ajustar los errores de validación");
                    dto.SistemaVentasRoles = await _combosHelper.GetComboRoles();
                    return View(dto);
                }

                Response<UserDTO> response = await _usersService.EditAsync(dto);

                if (!response.IsSuccess)
                {
                    _notifyService.Error(response.Message);
                    dto.SistemaVentasRoles = await _combosHelper.GetComboRoles();
                    return View(dto);
                }

                _notifyService.Success(response.Message);
                return RedirectToAction(nameof(Index));
            }
        }
    }

