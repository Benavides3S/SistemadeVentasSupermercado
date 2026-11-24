using AspNetCoreHero.ToastNotification.Abstractions;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Rendering;
using SistemadeVentasSupermercado.Web.Core;
using SistemadeVentasSupermercado.Web.Core.Attributes;
using SistemadeVentasSupermercado.Web.Core.Pagination;
using SistemadeVentasSupermercado.Web.Data.Entities;
using SistemadeVentasSupermercado.Web.Core.Mappings;
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

            public UsersController(IUsersService sectionsService, INotyfService notifyService, ICombosHelper combosHelper)
            {
                _usersService = sectionsService;
                _notifyService = notifyService;
                _combosHelper = combosHelper;
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

            Response<TemporaryPasswordDTO> response = await _usersService.CreateAsync(dto, User.Identity!.Name!);

            if (!response.IsSuccess)
            {
                string detalle = response.Errors.Count == 0 ? "" : " " + string.Join(" ", response.Errors);
                _notifyService.Error(response.Message + detalle);
                ModelState.AddModelError(string.Empty, response.Message + detalle);

                dto.SistemaVentasRoles = await _combosHelper.GetComboRoles();
                return View(dto);
            }

            return TemporaryPasswordView(response.Result!);
        }

        /// <summary>
        /// Pone una contraseña temporal nueva (por ejemplo, si el usuario olvidó la suya).
        /// Un Admin solo puede ser restablecido por otro Admin (lo revisa el servicio).
        /// </summary>
        [HttpPost]
        [CustomAuthorize(permission: "resetPasswords", module: "Usuarios")]
        public async Task<IActionResult> ResetPassword([FromRoute] Guid id)
        {
            Response<TemporaryPasswordDTO> response = await _usersService.ResetPasswordAsync(id, User.Identity!.Name!);

            if (!response.IsSuccess)
            {
                _notifyService.Error(response.Message);
                return RedirectToAction(nameof(Index));
            }

            return TemporaryPasswordView(response.Result!);
        }

        /// <summary>
        /// Muestra la contraseña temporal directamente como respuesta del POST (sin redirigir), así no
        /// queda guardada en TempData, en la URL ni en el historial. "no-store": el navegador no la guarda en caché.
        /// </summary>
        private IActionResult TemporaryPasswordView(TemporaryPasswordDTO dto)
        {
            Response.Headers.CacheControl = "no-store";
            return View("TemporaryPassword", dto);
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

                UserDTO dto = user.ToUserDTO();
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

                Response<UserDTO> response = await _usersService.EditAsync(dto, User.Identity!.Name!);

                if (!response.IsSuccess)
                {
                    _notifyService.Error(response.Message);
                    ModelState.AddModelError(string.Empty, response.Message);
                    dto.SistemaVentasRoles = await _combosHelper.GetComboRoles();
                    return View(dto);
                }

                _notifyService.Success(response.Message);
                return RedirectToAction(nameof(Index));
            }
        }
    }

