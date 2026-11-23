using Microsoft.AspNetCore.Mvc.Rendering;

namespace SistemadeVentasSupermercado.Web.Helpers.Abstractions
{
    public interface ICombosHelper
    {
        Task<IEnumerable<SelectListItem>> GetComboRoles();
        Task<IEnumerable<SelectListItem>> GetComboCategories(Guid? includeCategoryId = null);

        /// <summary>Proveedores activos, por nombre (los desactivados no reciben compras nuevas).</summary>
        Task<IEnumerable<SelectListItem>> GetComboSuppliers();

    }
}
