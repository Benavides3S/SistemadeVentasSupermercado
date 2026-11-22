using AutoMapper;
using Microsoft.AspNetCore.Mvc.ViewFeatures;
using Microsoft.EntityFrameworkCore;
using SistemadeVentasSupermercado.Web.Core;
using SistemadeVentasSupermercado.Web.Core.Pagination;
using SistemadeVentasSupermercado.Web.Data;
using SistemadeVentasSupermercado.Web.Data.Entities;
using SistemadeVentasSupermercado.Web.DTOs;
using SistemadeVentasSupermercado.Web.Services.Abstractions;

namespace SistemadeVentasSupermercado.Web.Services.Implementations
{
    public class ProductsService : CustomQueryableOperationsService, IProductService
    {
        private readonly DataContext _context;
        private readonly IMapper _mapper;

        public ProductsService(DataContext context, IMapper mapper) : base(context, mapper)
        {
            _context = context;
            _mapper = mapper;
        }

        public async Task<Response<ProductDTO>> CreateAsync(ProductDTO dto)
        {
            dto.Name = dto.Name.Trim();

            if (await _context.Products.AnyAsync(p => p.Name == dto.Name))
                return Response<ProductDTO>.Failure($"Ya existe un producto con el nombre \"{dto.Name}\"");

            if (!await _context.Categories.AnyAsync(c => c.Id == dto.CategoryId && c.IsActive))
                return Response<ProductDTO>.Failure("Seleccione una categoría válida y activa");

            return await CreateAsync<Product, ProductDTO>(dto);
        }

        public async Task<Response<object>> DeleteAsync(Guid id)
        {
            //try
            //{
            //    // Buscar el producto en la base de datos
            //    Product? product = await _context.Products.FirstOrDefaultAsync(p => p.Id == id);

            //    if (product == null)
            //    {
            //        return Response<object>.Failure($"No se encontró el producto con Id {id}");
            //    }

            //    // Eliminar el producto
            //    _context.Products.Remove(product);
            //    await _context.SaveChangesAsync();

            //    return Response<object>.Success(null, "Producto eliminado con éxito");
            //}
            //catch (Exception ex)
            //{
            //    return Response<object>.Failure(ex);
            //}
            return await DeleteAsync<Product>(id);
        }

        public async Task<Response<ProductDTO>> EditAsync(ProductDTO dto)
        {
            dto.Name = dto.Name.Trim();

            if (await _context.Products.AnyAsync(p => p.Name == dto.Name && p.Id != dto.Id))
                return Response<ProductDTO>.Failure($"Ya existe otro producto con el nombre \"{dto.Name}\"");

            if (!await _context.Categories.AnyAsync(c => c.Id == dto.CategoryId))
                return Response<ProductDTO>.Failure("La categoría seleccionada no existe");

            return await EditAsync<Product, ProductDTO>(dto, dto.Id);
        }


        public async Task<Response<List<ProductDTO>>> GetListAsync()
    => await GetCompleteListAsync<Product, ProductDTO>(_context.Products.Include(p => p.Category));


        public async Task<Response<PaginationResponse<ProductDTO>>> GetPaginatedListAsync(PaginationRequest request)
        {
            IQueryable<Product> query = _context.Products.Include(p => p.Category).AsQueryable();

            if (!string.IsNullOrWhiteSpace(request.Filter))
            {
                string filter = request.Filter.ToLower();
                query = query.Where(p => p.Name.ToLower().Contains(filter)
                                      || (p.Description != null && p.Description.ToLower().Contains(filter))
                                      || p.Category!.Name.ToLower().Contains(filter));
            }

            query = query.OrderBy(p => p.Name);

            return await GetPaginationAsync<Product, ProductDTO>(request, query);
        }
        public async Task<Response<ProductDTO>> GetOneAsync(Guid id)
      => await GetOneAsync<Product, ProductDTO>(id, _context.Products.Include(p => p.Category));
    }
}
