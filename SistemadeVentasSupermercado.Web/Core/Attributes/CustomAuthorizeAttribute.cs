using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using SistemadeVentasSupermercado.Web.Services.Abstractions;

namespace SistemadeVentasSupermercado.Web.Core.Attributes
{
    public class CustomAuthorizeAttribute : TypeFilterAttribute
    {
        public CustomAuthorizeAttribute(string permission, string module) : base(typeof(CustomAuthorizeFilter))
        {
            Arguments = [module, permission];
        }
    }

    public class CustomAuthorizeFilter : IAsyncAuthorizationFilter
    {
        private readonly string _permission;
        private readonly string _module;
        private readonly IUsersService _userService;

        public CustomAuthorizeFilter(string module, string permission, IUsersService userService)
        {
            _module = module;
            _permission = permission;
            _userService = userService;
        }

        public async Task OnAuthorizationAsync(AuthorizationFilterContext context)
        {
            bool userIsAuthenticated = _userService.CurrentUserIsAuthenticaded();

            if (!userIsAuthenticated)
            {
                context.Result = new RedirectToRouteResult(new RouteValueDictionary
                {
                    { "Controller", "Account" },
                    { "Action", "Login" },
                    { "ReturnUrl", context.HttpContext.Request.Path }
                });

                return;
            }

            bool isAuthorized = await _userService.CurrentUserIsAuthorizedAsync(_permission, _module);

            if (!isAuthorized)
            {
                context.Result = new ForbidResult();
            }
        }
    }
}

