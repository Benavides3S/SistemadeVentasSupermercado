using Microsoft.AspNetCore.Mvc.Rendering;

namespace SistemadeVentasSupermercado.Web.Helpers.Abstractions
{
    public interface ICombosHelper
    {
        Task<IEnumerable<SelectListItem>> GetComboRoles();
        
    }
}
