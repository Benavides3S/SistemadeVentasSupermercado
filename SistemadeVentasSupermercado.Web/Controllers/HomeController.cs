using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using SistemadeVentasSupermercado.Web.Core;
using SistemadeVentasSupermercado.Web.DTOs;
using SistemadeVentasSupermercado.Web.Services.Abstractions;
using SistemadeVentasSupermercado.Web.Models;
using System.Diagnostics;
using System.Security.Claims;

namespace SistemadeVentasSupermercado.Web.Controllers
{
    public class HomeController : Controller
    {
        private readonly IHomeService _homeService;
        private readonly IUsersService _usersService;

        public HomeController(IHomeService homeService, IUsersService usersService)
        {
            _homeService = homeService;
            _usersService = usersService;
        }

        // Sin sesión se manda al login (antes se veía el inicio con el menú vacío).
        // Cada bloque del resumen se calcula solo si el usuario tiene el permiso para verlo.
        [Authorize]
        public async Task<IActionResult> Index()
        {
            HomeRequestDTO request = new HomeRequestDTO
            {
                UserId = User.FindFirstValue(ClaimTypes.NameIdentifier) ?? string.Empty,
                WantsSalesToday = await _usersService.CurrentUserIsAuthorizedAsync("showSales", "Ventas"),
                WantsOwnCashSession = await _usersService.CurrentUserIsAuthorizedAsync("manageCashSessions", "Caja"),
                WantsLowStock = await _usersService.CurrentUserIsAuthorizedAsync("showInventory", "Inventario")
                                || await _usersService.CurrentUserIsAuthorizedAsync("showProducts", "Productos")
            };

            Response<HomeSummaryDTO> response = await _homeService.GetSummaryAsync(request);

            // Si el resumen falla, el Inicio igual se muestra (solo con los accesos rápidos).
            return View(response.IsSuccess ? response.Result! : new HomeSummaryDTO());
        }

        [ResponseCache(Duration = 0, Location = ResponseCacheLocation.None, NoStore = true)]
        public IActionResult Error()
        {
            return View(new ErrorViewModel { RequestId = Activity.Current?.Id ?? HttpContext.TraceIdentifier });
        }
    }
}
