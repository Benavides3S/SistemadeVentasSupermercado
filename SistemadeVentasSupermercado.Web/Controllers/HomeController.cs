using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using SistemadeVentasSupermercado.Web.Models;
using System.Diagnostics;

namespace SistemadeVentasSupermercado.Web.Controllers
{
    public class HomeController : Controller
    {
        // Sin sesión se manda al login (antes se veía el inicio con el menú vacío).
        [Authorize]
        public IActionResult Index()
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
