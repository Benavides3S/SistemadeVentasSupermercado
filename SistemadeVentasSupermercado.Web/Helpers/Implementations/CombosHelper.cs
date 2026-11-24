using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.EntityFrameworkCore;
using SistemadeVentasSupermercado.Web.Data;
using SistemadeVentasSupermercado.Web.Helpers.Abstractions;

namespace SistemadeVentasSupermercado.Web.Helpers.Implementations
{
    public class CombosHelper : ICombosHelper

    {
        private readonly DataContext _context;

        public CombosHelper(DataContext context)
        {
            _context = context;
        }

        public async Task<IEnumerable<SelectListItem>> GetComboRoles()
        {
            return await _context.SistemaVentasRoles.Select(r => new SelectListItem
            {
                Text = r.Name,
                Value = r.Id.ToString()
            }).ToListAsync();
        }
        public async Task<IEnumerable<SelectListItem>> GetComboCategories(Guid? includeCategoryId = null)
        {
            // Solo categorías activas, más la que ya tiene el producto (aunque esté inactiva)
            return await _context.Categories
                .Where(c => c.IsActive || c.Id == includeCategoryId)
                .OrderBy(c => c.Name)
                .Select(c => new SelectListItem
                {
                    Text = c.IsActive ? c.Name : c.Name + " (inactiva)",
                    Value = c.Id.ToString()
                })
                .ToListAsync();
        }

        public async Task<IEnumerable<SelectListItem>> GetComboSuppliers()
        {
            return await _context.Suppliers
                .Where(s => s.IsActive)
                .OrderBy(s => s.Name)
                .Select(s => new SelectListItem
                {
                    Text = s.Name,
                    Value = s.Id.ToString()
                })
                .ToListAsync();
        }
    }
}
