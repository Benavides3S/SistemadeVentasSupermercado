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

        
    }
}
