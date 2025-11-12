using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using SistemadeVentasSupermercado.Web.Models;
using SistemadeVentasSupermercado.Web.Services.Abstractions;
using SistemadeVentasSupermercado.Web.Services.Implementations;
using System.Diagnostics;

namespace SistemadeVentasSupermercado.Web.Controllers
{
    public class HomeController : Controller
    {
        private readonly ILogger<HomeController> _logger;
        private readonly IUsersService _usersService;

        public HomeController(ILogger<HomeController> logger, IUsersService usersService)
        {
            _logger = logger;
            _usersService = usersService;
        }
        public async Task<IActionResult> Dashboard()
        {
           

            return View();
        }
        [AllowAnonymous] // Temporalmente sin autorización para probar
        public async Task<IActionResult> DebugPermissions()
        {
            _logger.LogInformation("=== DIAGNÓSTICO MANUAL DE PERMISOS ===");
            await _usersService.LogCurrentUserPermissions();
            _logger.LogInformation("=== FIN DIAGNÓSTICO MANUAL ===");

            return Content("Revisa los logs en Visual Studio - Output window");
        }
        public IActionResult Index()
        {
            return View();
        }

        public IActionResult Privacy()
        {
            return View();
        }

        [ResponseCache(Duration = 0, Location = ResponseCacheLocation.None, NoStore = true)]
        public IActionResult Error()
        {
            return View(new ErrorViewModel { RequestId = Activity.Current?.Id ?? HttpContext.TraceIdentifier });
        }
    }
}
