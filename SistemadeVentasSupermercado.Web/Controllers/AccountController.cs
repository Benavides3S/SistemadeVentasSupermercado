using AspNetCoreHero.ToastNotification.Abstractions;
using SistemadeVentasSupermercado.Web.Core;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using SistemadeVentasSupermercado.Web.Data.Entities;
using SistemadeVentasSupermercado.Web.Core.Filters;
using SistemadeVentasSupermercado.Web.Core.Mappings;
using SistemadeVentasSupermercado.Web.Core.ScreenLock;
using SistemadeVentasSupermercado.Web.DTOs;
using SistemadeVentasSupermercado.Web.Services.Abstractions;

namespace SistemadeVentasSupermercado.Web.Controllers
{
    public class AccountController : Controller
    {
        private readonly IUsersService _usersService;
        private readonly INotyfService _notyfService;
        private readonly IAuthorizationPinService _pinService;
        private readonly ScreenLockStore _screenLock;
        private readonly IProfilePhotoService _photoService;

        public AccountController(IUsersService usersService, INotyfService notyfService,
            IAuthorizationPinService pinService, ScreenLockStore screenLock, IProfilePhotoService photoService)
        {
            _photoService = photoService;
            _screenLock = screenLock;
            _pinService = pinService;
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

        // ---------- Bloqueo de pantalla ----------
        // La sesión es SIEMPRE la de la cookie: nadie puede bloquear ni desbloquear otra.

        /// <summary>Bloquea la pantalla (botón o inactividad). Lo llama session-lock.js.</summary>
        [Authorize]
        [HttpPost]
        [SkipMustChangePassword]
        [AllowWhenLocked]
        public IActionResult Lock()
        {
            _screenLock.Lock(ScreenLockSession.CurrentId(HttpContext)!);
            return NoContent();
        }

        /// <summary>
        /// "Hay alguien usando la pantalla". No hace nada: al pasar por el filtro cuenta como actividad
        /// (y si la sesión ya está bloqueada, el filtro responde 423).
        /// </summary>
        [Authorize]
        [HttpPost]
        [SkipMustChangePassword]
        public IActionResult Activity() => NoContent();

        /// <summary>Desbloqueo desde la capa que tapa la pantalla (fetch). Devuelve JSON.</summary>
        [Authorize]
        [HttpPost]
        [SkipMustChangePassword]
        [AllowWhenLocked]
        public async Task<IActionResult> Unlock([FromForm] string? password)
        {
            Response<bool> result = await TryUnlockAsync(password);
            return Json(new { isSuccess = result.IsSuccess, message = result.Message });
        }

        /// <summary>Pantalla de desbloqueo, para cuando se abre o recarga una página estando bloqueada.</summary>
        [Authorize]
        [HttpGet]
        [SkipMustChangePassword]
        [AllowWhenLocked]
        public IActionResult Locked([FromQuery] string? returnUrl)
        {
            if (!_screenLock.IsLocked(ScreenLockSession.CurrentId(HttpContext)!))
                return LocalOrHome(returnUrl);

            ViewBag.ReturnUrl = returnUrl;
            return View();
        }

        [Authorize]
        [HttpPost]
        [SkipMustChangePassword]
        [AllowWhenLocked]
        public async Task<IActionResult> Locked([FromForm] string? password, [FromForm] string? returnUrl)
        {
            Response<bool> result = await TryUnlockAsync(password);
            if (result.IsSuccess)
                return LocalOrHome(returnUrl);

            ModelState.AddModelError(string.Empty, result.Message ?? "No se pudo desbloquear.");
            ViewBag.ReturnUrl = returnUrl;
            return View();
        }

        private async Task<Response<bool>> TryUnlockAsync(string? password)
        {
            Response<bool> result = await _usersService.CheckUnlockPasswordAsync(User.Identity!.Name!, password);
            if (result.IsSuccess)
                _screenLock.Unlock(ScreenLockSession.CurrentId(HttpContext)!);
            return result;
        }

        // Nunca a otro sitio: returnUrl viene del navegador.
        private IActionResult LocalOrHome(string? returnUrl)
            => Url.IsLocalUrl(returnUrl) ? LocalRedirect(returnUrl!) : RedirectToAction("Index", "Home");

        [Authorize]
        [HttpGet]
        [SkipMustChangePassword]
        [AllowWhenLocked]
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

        /// <summary>
        /// "Mi PIN": solo para quien puede autorizar (permiso de anular o de devolver). Esa combinación
        /// se revisa aquí. El usuario es SIEMPRE el de la sesión.
        /// </summary>
        [Authorize]
        [HttpGet]
        public async Task<IActionResult> AuthorizationPin()
        {
            if (!await _pinService.CanHavePinAsync(User.Identity!.Name!))
                return Forbid();

            ViewBag.HasPin = await _pinService.HasPinAsync(User.Identity.Name!);
            return View(new SetAuthorizationPinDTO());
        }

        [Authorize]
        [HttpPost]
        public async Task<IActionResult> AuthorizationPin(SetAuthorizationPinDTO dto)
        {
            string userName = User.Identity!.Name!;
            if (!await _pinService.CanHavePinAsync(userName))
                return Forbid();

            if (ModelState.IsValid)
            {
                Response<bool> result = await _pinService.SetPinAsync(userName, dto);

                if (result.IsSuccess)
                {
                    _notyfService.Success(result.Message);
                    return RedirectToAction(nameof(AuthorizationPin));
                }

                ModelState.AddModelError(string.Empty, result.Message ?? "No se pudo guardar el PIN.");
            }

            ViewBag.HasPin = await _pinService.HasPinAsync(userName);
            return View(new SetAuthorizationPinDTO()); // ni la contraseña ni el PIN vuelven al formulario
        }

        // ---------- Foto de perfil ----------
        // Ninguna de estas acciones recibe un usuario: es SIEMPRE el de la sesión.

        /// <summary>
        /// La foto propia. La dirección es la misma para todos, así que el navegador NO puede guardarla:
        /// en un computador compartido, el siguiente usuario vería la del anterior.
        /// </summary>
        [Authorize]
        [HttpGet]
        [SkipMustChangePassword]
        public async Task<IActionResult> Photo()
        {
            Response.Headers.CacheControl = "no-store";
            Response.Headers.XContentTypeOptions = "nosniff";

            byte[]? content = await _photoService.GetAsync(User.Identity!.Name!);
            return content is null ? NotFound() : File(content, "image/jpeg");
        }

        /// <summary>Recibe la foto ya recortada por profile-photo.js. Devuelve JSON.</summary>
        [Authorize]
        [HttpPost]
        [RequestSizeLimit(ProfilePhotoRules.MaxRequestBytes)]
        [RequestFormLimits(MultipartBodyLengthLimit = ProfilePhotoRules.MaxRequestBytes)]
        public async Task<IActionResult> UploadPhoto(IFormFile? photo)
        {
            if (photo is null || photo.Length == 0)
                return PhotoResult(Response<bool>.Failure("No llegó ninguna foto, o es demasiado grande."));

            // Antes de leerla en memoria.
            if (photo.Length > ProfilePhotoRules.MaxBytes)
                return PhotoResult(Response<bool>.Failure(ProfilePhotoRules.TooLargeMessage));

            using MemoryStream content = new();
            await photo.CopyToAsync(content);

            return PhotoResult(await _photoService.SaveAsync(User.Identity!.Name!, content.ToArray()));
        }

        private IActionResult PhotoResult(Core.Response<bool> result)
        {
            if (result.IsSuccess)
                _notyfService.Success(result.Message);

            return Json(new { isSuccess = result.IsSuccess, message = result.Message });
        }

        [Authorize]
        [HttpPost]
        public async Task<IActionResult> RemovePhoto()
        {
            Response<bool> result = await _photoService.RemoveAsync(User.Identity!.Name!);

            if (result.IsSuccess)
                _notyfService.Success(result.Message);
            else
                _notyfService.Error(result.Message);

            return RedirectToAction(nameof(UpdateUser));
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
