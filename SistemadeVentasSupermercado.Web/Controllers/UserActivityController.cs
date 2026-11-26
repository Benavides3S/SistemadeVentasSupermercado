using AspNetCoreHero.ToastNotification.Abstractions;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using SistemadeVentasSupermercado.Web.Core;
using SistemadeVentasSupermercado.Web.Core.Attributes;
using SistemadeVentasSupermercado.Web.Core.Pagination;
using SistemadeVentasSupermercado.Web.DTOs;
using SistemadeVentasSupermercado.Web.Services.Abstractions;

namespace SistemadeVentasSupermercado.Web.Controllers
{
    [Authorize]
    public class UserActivityController : Controller
    {
        private readonly IUserActivityService _activityService;
        private readonly INotyfService _notyfService;

        public UserActivityController(IUserActivityService activityService, INotyfService notyfService)
        {
            _activityService = activityService;
            _notyfService = notyfService;
        }

        [HttpGet]
        [CustomAuthorize("showUserActivity", "Usuarios")]
        public async Task<IActionResult> Index([FromQuery] UserActivityQueryDTO query)
        {
            Response<PaginationResponse<UserActivityDTO>> response = await _activityService.GetPaginatedListAsync(query);

            if (!response.IsSuccess)
            {
                _notyfService.Error(response.Message);
                // Con las fechas al revés se vuelve a mostrar la pantalla sin esos filtros.
                query.From = null;
                query.To = null;
                response = await _activityService.GetPaginatedListAsync(query);
                if (!response.IsSuccess)
                    return RedirectToAction("Index", "Home");
            }

            ViewBag.Query = query;
            return View(response.Result);
        }
    }
}
