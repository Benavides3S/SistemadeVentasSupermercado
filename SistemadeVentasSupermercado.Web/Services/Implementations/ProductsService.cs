using Microsoft.AspNetCore.Mvc.ViewFeatures;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using SistemadeVentasSupermercado.Web.Core;
using SistemadeVentasSupermercado.Web.Core.Mappings;
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

        public ProductsService(DataContext context) : base(context)
        {
            _context = context;
        }

        public async Task<Response<ProductDTO>> CreateAsync(ProductDTO dto, string? userId = null)
        {
            dto.Name = dto.Name.Trim();

            if (await _context.Products.AnyAsync(p => p.Name == dto.Name))
                return Response<ProductDTO>.Failure($"Ya existe un producto con el nombre \"{dto.Name}\"");

            string? barcodeError = await PrepareBarcodeAsync(dto);
            if (barcodeError is not null)
                return Response<ProductDTO>.Failure(barcodeError);

            if (!await _context.Categories.AnyAsync(c => c.Id == dto.CategoryId && c.IsActive))
                return Response<ProductDTO>.Failure("Seleccione una categoría válida y activa");

            // Sin stock inicial no hay nada que registrar en el historial.
            if (dto.Stock <= 0)
                return await CreateAsync<Product, ProductDTO>(dto, d => d.ToEntity());

            try
            {
                // El producto y su movimiento de "Stock inicial" se guardan juntos o no se guarda nada.
                await using IDbContextTransaction transaction = await _context.Database.BeginTransactionAsync();

                Response<ProductDTO> created = await CreateAsync<Product, ProductDTO>(dto, d => d.ToEntity());
                if (!created.IsSuccess)
                    return created; // la transacción se deshace sola

                // El nombre es único, así que identifica al producto recién creado.
                Guid productId = await _context.Products
                    .Where(p => p.Name == dto.Name)
                    .Select(p => p.Id)
                    .SingleAsync();

                await InventoryRecorder.RecordAsync(_context, productId, InventoryMovementType.InitialStock, dto.Stock, userId);
                await _context.SaveChangesAsync();
                await transaction.CommitAsync();

                return created;
            }
            catch (Exception ex)
            {
                return Response<ProductDTO>.Failure(ex);
            }
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

        /// <summary>
        /// Edita los datos del producto. El stock NO se cambia aquí: se ajusta con InventoryService.AdjustAsync
        /// (queda en el historial con su motivo). ProductDTO.ApplyTo no copia Stock, Unit ni Cost.
        /// </summary>
        public async Task<Response<ProductDTO>> EditAsync(ProductDTO dto)
        {
            dto.Name = dto.Name.Trim();

            if (await _context.Products.AnyAsync(p => p.Name == dto.Name && p.Id != dto.Id))
                return Response<ProductDTO>.Failure($"Ya existe otro producto con el nombre \"{dto.Name}\"");

            string? barcodeError = await PrepareBarcodeAsync(dto);
            if (barcodeError is not null)
                return Response<ProductDTO>.Failure(barcodeError);

            if (!await _context.Categories.AnyAsync(c => c.Id == dto.CategoryId))
                return Response<ProductDTO>.Failure("La categoría seleccionada no existe");

            try
            {
                Product? product = await _context.Products.FirstOrDefaultAsync(p => p.Id == dto.Id);
                if (product is null)
                    return Response<ProductDTO>.Failure($"No existe el producto con id: {dto.Id}");

                // ApplyTo no toca el stock, así que EF no lo incluye en el UPDATE: una venta hecha
                // mientras se editaba no se pisa.
                dto.ApplyTo(product);
                await _context.SaveChangesAsync();

                dto.Stock = await CurrentStockAsync(dto.Id);

                return Response<ProductDTO>.Success(dto, $"Producto actualizado con éxito. Stock actual: {DisplayFormat.Quantity(dto.Stock)}");
            }
            catch (Exception ex)
            {
                return Response<ProductDTO>.Failure(ex);
            }
        }

        /// <summary>
        /// Normaliza el código (sin espacios; vacío = sin código), lo valida y revisa que ningún otro
        /// producto lo tenga. Devuelve el mensaje de error o null. El índice único de la base es la garantía final.
        /// </summary>
        private async Task<string?> PrepareBarcodeAsync(ProductDTO dto)
        {
            dto.Barcode = BarcodeHelper.Normalize(dto.Barcode);

            string? error = BarcodeHelper.Validate(dto.Barcode);
            if (error is not null) return error;
            if (dto.Barcode is null) return null;

            string? owner = await _context.Products
                                          .Where(p => p.Barcode == dto.Barcode && p.Id != dto.Id)
                                          .Select(p => p.Name)
                                          .FirstOrDefaultAsync();

            return owner is null ? null : $"El código {dto.Barcode} ya lo tiene el producto \"{owner}\".";
        }

        private Task<decimal> CurrentStockAsync(Guid productId)
            => _context.Products.Where(p => p.Id == productId).Select(p => p.Stock).SingleAsync();


        public async Task<Response<List<ProductDTO>>> GetListAsync()
    => await GetCompleteListAsync<Product, ProductDTO>(p => p.ToDTO(), _context.Products.Include(p => p.Category));


        public async Task<Response<PaginationResponse<ProductDTO>>> GetPaginatedListAsync(PaginationRequest request)
        {
            IQueryable<Product> query = _context.Products.Include(p => p.Category).AsQueryable();

            if (!string.IsNullOrWhiteSpace(request.Filter))
            {
                string filter = request.Filter.ToLower();
                query = query.Where(p => p.Name.ToLower().Contains(filter)
                                      || (p.Barcode != null && p.Barcode.Contains(filter.Replace(" ", "")))
                                      || (p.Description != null && p.Description.ToLower().Contains(filter))
                                      || p.Category!.Name.ToLower().Contains(filter));
            }

            query = query.OrderBy(p => p.Name);

            return await GetPaginationAsync<Product, ProductDTO>(request, p => p.ToDTO(), query);
        }
        public async Task<Response<ProductDTO>> GetOneAsync(Guid id)
      => await GetOneAsync<Product, ProductDTO>(id, p => p.ToDTO(), _context.Products.Include(p => p.Category));
    }
}
