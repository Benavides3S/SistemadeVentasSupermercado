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
    public class SuppliersService : CustomQueryableOperationsService, ISupplierService
    {
        private readonly DataContext _context;

        public SuppliersService(DataContext context) : base(context)
        {
            _context = context;
        }

        // Quita espacios al inicio, al final y los repetidos en medio
        private static string NormalizeName(string name)
            => string.Join(' ', name.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries));

        // Los textos opcionales vacíos se guardan como null (así el índice del NIT no choca con vacíos)
        private static string? NullIfEmpty(string? value)
            => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

        private static void Normalize(SupplierDTO dto)
        {
            dto.Name = NormalizeName(dto.Name);
            dto.TaxId = NullIfEmpty(dto.TaxId);
            dto.Phone = NullIfEmpty(dto.Phone);
            dto.Email = NullIfEmpty(dto.Email);
            dto.Address = NullIfEmpty(dto.Address);
            dto.Notes = NullIfEmpty(dto.Notes);
        }

        public async Task<Response<SupplierDTO>> CreateAsync(SupplierDTO dto)
        {
            Normalize(dto);

            if (await _context.Suppliers.AnyAsync(s => s.Name == dto.Name))
                return Response<SupplierDTO>.Failure($"Ya existe el proveedor \"{dto.Name}\"");

            if (dto.TaxId is not null && await _context.Suppliers.AnyAsync(s => s.TaxId == dto.TaxId))
                return Response<SupplierDTO>.Failure($"Ya existe un proveedor con el NIT/documento \"{dto.TaxId}\"");

            return await CreateAsync<Supplier, SupplierDTO>(dto, d => d.ToEntity());
        }

        public async Task<Response<SupplierDTO>> EditAsync(SupplierDTO dto)
        {
            Normalize(dto);

            if (await _context.Suppliers.AnyAsync(s => s.Name == dto.Name && s.Id != dto.Id))
                return Response<SupplierDTO>.Failure($"Ya existe otro proveedor llamado \"{dto.Name}\"");

            if (dto.TaxId is not null && await _context.Suppliers.AnyAsync(s => s.TaxId == dto.TaxId && s.Id != dto.Id))
                return Response<SupplierDTO>.Failure($"Ya existe otro proveedor con el NIT/documento \"{dto.TaxId}\"");

            return await EditAsync<Supplier, SupplierDTO>(dto, dto.Id, (d, s) => d.ApplyTo(s));
        }

        public async Task<Response<object>> DeleteAsync(Guid id)
        {
            // Las compras guardan el proveedor (FK Restrict): con compras no se borra, se desactiva.
            if (await _context.Purchases.AnyAsync(p => p.SupplierId == id))
                return Response<object>.Failure("Este proveedor tiene compras registradas y no se puede borrar. Desactívalo para que no aparezca en compras nuevas.");

            return await DeleteAsync<Supplier>(id);
        }

        public async Task<Response<SupplierDTO>> GetOneAsync(Guid id)
            => await GetOneAsync<Supplier, SupplierDTO>(id, s => s.ToDTO());

        public async Task<Response<PaginationResponse<SupplierDTO>>> GetPaginatedListAsync(PaginationRequest request)
        {
            IQueryable<Supplier> query = _context.Suppliers.AsQueryable();

            if (!string.IsNullOrWhiteSpace(request.Filter))
            {
                string filter = request.Filter.ToLower();
                query = query.Where(s => s.Name.ToLower().Contains(filter)
                                      || (s.TaxId != null && s.TaxId.ToLower().Contains(filter)));
            }

            query = query.OrderBy(s => s.Name);

            return await GetPaginationAsync<Supplier, SupplierDTO>(request, s => s.ToDTO(), query);
        }
    }
}
