using AspNetCoreHero.ToastNotification.Abstractions;
using SistemadeVentasSupermercado.Web.Core;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using SistemadeVentasSupermercado.Web.Data.Entities;
using SistemadeVentasSupermercado.Web.Core.Filters;
using SistemadeVentasSupermercado.Web.Core.Mappings;
using SistemadeVentasSupermercado.Web.DTOs;
using SistemadeVentasSupermercado.Web.Services.Abstractions;

namespace SistemadeVentasSupermercado.Web.Controllers
{
    public class AccountController : Controller
    {
        private readonly IUsersService _usersService;
        private readonly INotyfService _notyfService;

        public AccountController(IUsersService usersService, INotyfService notyfService)
        {
            _usersService = usersService;
            _notyfService = notyfService;
        }

        [HttpGet]
        [AllowAnonymous]
        [SkipMustChangePassword]
        public IActionResult Login()
        {
            return View();
        }

        [HttpPost]
        [AllowAnonymous]
        [SkipMustChangePassword]
        public async Task<IActionResult> Login(LoginDTO dto)
        {
            if (ModelState.IsValid)
            {
                Response<Microsoft.AspNetCore.Identity.SignInResult> result = await _usersService.LoginAsync(dto);

                if (result.IsSuccess)
                {
                    // Si tiene contraseña temporal, el filtro global lo lleva a cambiarla.
                    return RedirectToAction("Index", "Home");
                }

                if (result.Result?.IsLockedOut == true)
                {
                    ModelState.AddModelError(string.Empty,
                        "La cuenta está bloqueada por demasiados intentos fallidos. Intenta de nuevo en 15 minutos " +
                        "o pide a un administrador que restablezca tu contraseña.");
                }
                else
                {
                    ModelState.AddModelError(string.Empty, "Email o contraseña incorrectos");
                }
            }

            else if (!string.IsNullOrEmpty(dto.Email) && !string.IsNullOrEmpty(dto.Password))
            {
                await _usersService.RecordRejectedLoginFormAsync(dto);
            }

            return View(dto);
        }

        [Authorize]
        [HttpGet]
        [SkipMustChangePassword]
        public async Task<IActionResult> Logout()
        {
            await _usersService.LogoutAsync();
            return RedirectToAction(nameof(Login));
        }

        [Authorize]
        [HttpGet]
        [SkipMustChangePassword]
        public async Task<IActionResult> ChangePassword()
        {
            ViewBag.MustChange = await _usersService.MustChangePasswordAsync(User.Identity!.Name!);
            return View(new ChangePasswordDTO());
        }

        [Authorize]
        [HttpPost]
        [SkipMustChangePassword]
        public async Task<IActionResult> ChangePassword(ChangePasswordDTO dto)
        {
            string userName = User.Identity!.Name!;

            if (!ModelState.IsValid)
            {
                ViewBag.MustChange = await _usersService.MustChangePasswordAsync(userName);
                return View(dto);
            }

            Response<User> result = await _usersService.ChangePasswordAsync(userName, dto);

            if (!result.IsSuccess)
            {
                ModelState.AddModelError(string.Empty, result.Message ?? "No se pudo cambiar la contraseña.");
                ViewBag.MustChange = await _usersService.MustChangePasswordAsync(userName);
                return View(new ChangePasswordDTO()); // nunca se devuelven las contraseñas al formulario
            }

            await _usersService.RefreshSignInAsync(result.Result!);
            _notyfService.Success(result.Message);
            return RedirectToAction("Index", "Home");
        }

        [Authorize]
        [HttpGet]
        public async Task<IActionResult> UpdateUser()
        {
            User user = await _usersService.GetUserByEmailAsync(User.Identity.Name);

            if (user is null)
            {
                return NotFound();
            }

            return View(user.ToAccountUserDTO());
        }

        [Authorize]
        [HttpPost]
        public async Task<IActionResult> UpdateUser(AccountUserDTO dto)
        {
            if (ModelState.IsValid)
            {
                Response<AccountUserDTO> result = await _usersService.UpdateUserAsync(dto, User.Identity!.Name!);

                if (result.IsSuccess)
                {
                    _notyfService.Success(result.Message);
                }
                else
                {
                    _notyfService.Error(result.Message);
                }

                return RedirectToAction("Index", "Home");
            }

            _notyfService.Error("Debe ajustar lo errores de validación");
            return View(dto);
        }
    }
}
