using Microsoft.EntityFrameworkCore;
using SistemadeVentasSupermercado.Web.Core;
using SistemadeVentasSupermercado.Web.Core.Mappings;
using SistemadeVentasSupermercado.Web.Core.Pagination;
using SistemadeVentasSupermercado.Web.Data;
using SistemadeVentasSupermercado.Web.Data.Entities;
using SistemadeVentasSupermercado.Web.DTOs;
using SistemadeVentasSupermercado.Web.Services.Abstractions;

namespace SistemadeVentasSupermercado.Web.Services.Implementations
{
    public class CategoriesService : CustomQueryableOperationsService, ICategoryService
    {
        private readonly DataContext _context;

        public CategoriesService(DataContext context) : base(context)
        {
            _context = context;
        }

        // Quita espacios al inicio, al final y los repetidos en medio
        private static string NormalizeName(string name)
            => string.Join(' ', name.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries));

        public async Task<Response<CategoryDTO>> CreateAsync(CategoryDTO dto)
        {
            dto.Name = NormalizeName(dto.Name);

            if (await _context.Categories.AnyAsync(c => c.Name == dto.Name))
                return Response<CategoryDTO>.Failure($"Ya existe la categoría \"{dto.Name}\"");

            return await CreateAsync<Category, CategoryDTO>(dto, d => d.ToEntity());
        }

        public async Task<Response<CategoryDTO>> EditAsync(CategoryDTO dto)
        {
            dto.Name = NormalizeName(dto.Name);

            if (await _context.Categories.AnyAsync(c => c.Name == dto.Name && c.Id != dto.Id))
                return Response<CategoryDTO>.Failure($"Ya existe otra categoría llamada \"{dto.Name}\"");

            return await EditAsync<Category, CategoryDTO>(dto, dto.Id, (d, c) => d.ApplyTo(c));
        }

        public async Task<Response<object>> DeleteAsync(Guid id)
        {
            if (await _context.Products.AnyAsync(p => p.CategoryId == id))
                return Response<object>.Failure("No se puede eliminar: la categoría tiene productos. Puedes desactivarla en lugar de borrarla.");

            return await DeleteAsync<Category>(id);
        }

        public async Task<Response<CategoryDTO>> GetOneAsync(Guid id)
            => await GetOneAsync<Category, CategoryDTO>(id, c => c.ToDTO());

        public async Task<Response<PaginationResponse<CategoryDTO>>> GetPaginatedListAsync(PaginationRequest request)
        {
            IQueryable<Category> query = _context.Categories.AsQueryable();

            if (!string.IsNullOrWhiteSpace(request.Filter))
            {
                string filter = request.Filter.ToLower();
                query = query.Where(c => c.Name.ToLower().Contains(filter));
            }

            query = query.OrderBy(c => c.Name);

            return await GetPaginationAsync<Category, CategoryDTO>(request, c => c.ToDTO(), query);
        }
    }
}
