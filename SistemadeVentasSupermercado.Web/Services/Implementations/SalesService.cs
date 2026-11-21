using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using SistemadeVentasSupermercado.Web.Core;
using SistemadeVentasSupermercado.Web.Core.Pagination;
using SistemadeVentasSupermercado.Web.Data;
using SistemadeVentasSupermercado.Web.Data.Entities;
using SistemadeVentasSupermercado.Web.DTOs;
using SistemadeVentasSupermercado.Web.Services.Abstractions;

namespace SistemadeVentasSupermercado.Web.Services.Implementations
{
    public class SalesService : ISalesService
    {
        private readonly DataContext _context;

        public SalesService(DataContext context)
        {
            _context = context;
        }

        private static Response<SaleResultDTO> Fail(string message)
            => Response<SaleResultDTO>.Failure(message);

        public async Task<Response<SaleResultDTO>> CreateAsync(CreateSaleDTO dto, string userId)
        {
            if (dto.Items == null || dto.Items.Count == 0)
                return Fail("La venta debe tener al menos un producto");

            if (!Enum.IsDefined(typeof(PaymentMethod), dto.PaymentMethod))
                return Fail("Método de pago no válido");

            // Une líneas repetidas del mismo producto y las ordena siempre igual:
            // así dos cajeros nunca se bloquean entre sí esperándose mutuamente.
            var lines = dto.Items
                .GroupBy(i => i.ProductId)
                .Select(g => new { ProductId = g.Key, Quantity = Math.Round(g.Sum(x => x.Quantity), 3) })
                .OrderBy(l => l.ProductId)
                .ToList();

            if (lines.Any(l => l.Quantity <= 0))
                return Fail("Las cantidades deben ser mayores que cero");

            try
            {
                // Si salimos sin llegar a CommitAsync, la transacción se deshace sola.
                await using IDbContextTransaction transaction = await _context.Database.BeginTransactionAsync();

                if (dto.ClientId.HasValue &&
                    !await _context.Clients.AnyAsync(c => c.Id == dto.ClientId.Value))
                    return Fail("El cliente seleccionado no existe");

                List<Guid> productIds = lines.Select(l => l.ProductId).ToList();

                Dictionary<Guid, Product> products = await _context.Products
                    .AsNoTracking()
                    .Include(p => p.Category)
                    .Where(p => productIds.Contains(p.Id))
                    .ToDictionaryAsync(p => p.Id);

                if (products.Count != productIds.Count)
                    return Fail("Alguno de los productos ya no existe");

                // Se revisa aquí (no solo en la pantalla) porque el navegador se puede manipular.
                Product? inactive = products.Values.FirstOrDefault(p => p.Category is null || !p.Category.IsActive);
                if (inactive is not null)
                    return Fail($"\"{inactive.Name}\" pertenece a una categoría desactivada y no se puede vender");

                Sale sale = new Sale
                {
                    Id = Guid.NewGuid(),
                    UserId = userId,
                    ClientId = dto.ClientId,
                    PaymentMethod = dto.PaymentMethod,
                    Status = SaleStatus.Completed,
                    CreatedAtUtc = DateTime.UtcNow
                };

                foreach (var line in lines)
                {
                    Product product = products[line.ProductId];
                    decimal quantity = line.Quantity;

                    if (product.Unit == UnitOfMeasure.Unit && quantity != Math.Floor(quantity))
                        return Fail($"\"{product.Name}\" se vende por unidad: la cantidad debe ser un número entero");

                    // Descuento atómico: solo se aplica si todavía hay stock suficiente.
                    int updated = await _context.Products
                        .Where(p => p.Id == product.Id && p.Stock >= quantity)
                        .ExecuteUpdateAsync(s => s.SetProperty(p => p.Stock, p => p.Stock - quantity));

                    if (updated == 0)
                        return Fail($"Stock insuficiente de \"{product.Name}\"");

                    sale.Items.Add(new SaleItem
                    {
                        Id = Guid.NewGuid(),
                        ProductId = product.Id,
                        ProductName = product.Name,
                        UnitPrice = product.Price,
                        Quantity = quantity,
                        Unit = product.Unit,
                        Total = Math.Round(product.Price * quantity, 2, MidpointRounding.AwayFromZero)
                    });
                }

                sale.Total = sale.Items.Sum(i => i.Total);

                _context.Sales.Add(sale);
                await _context.SaveChangesAsync();
                await transaction.CommitAsync();

                return Response<SaleResultDTO>.Success(
                    new SaleResultDTO { Id = sale.Id, Number = sale.Number, Total = sale.Total },
                    "Venta registrada con éxito");
            }
            catch (Exception ex)
            {
                return Response<SaleResultDTO>.Failure(ex);
            }
        }

        // ---------- Consultas para la pantalla de venta ----------

        private const int MaxProductResults = 20;
        private const int MaxClientResults = 10;

        public async Task<Response<List<PosProductDTO>>> SearchProductsAsync(string? term)
        {
            if (string.IsNullOrWhiteSpace(term))
                return Response<List<PosProductDTO>>.Success(new List<PosProductDTO>());

            try
            {
                string filter = term.Trim().ToLower();

                List<PosProductDTO> products = await _context.Products
                    .AsNoTracking()
                    .Where(p => p.Stock > 0
                             && p.Category != null && p.Category.IsActive
                             && p.Name.ToLower().Contains(filter))
                    .OrderBy(p => p.Name)
                    .Take(MaxProductResults)
                    .Select(p => new PosProductDTO
                    {
                        Id = p.Id,
                        Name = p.Name,
                        Price = p.Price,
                        Stock = p.Stock,
                        Unit = p.Unit
                    })
                    .ToListAsync();

                foreach (PosProductDTO product in products)
                {
                    product.UnitAbbreviation = product.Unit.Abbreviation();
                    product.AllowsDecimals = product.Unit != UnitOfMeasure.Unit;
                }

                return Response<List<PosProductDTO>>.Success(products);
            }
            catch (Exception ex)
            {
                return Response<List<PosProductDTO>>.Failure(ex);
            }
        }

        public async Task<Response<List<PosClientDTO>>> SearchClientsAsync(string? term)
        {
            if (string.IsNullOrWhiteSpace(term))
                return Response<List<PosClientDTO>>.Success(new List<PosClientDTO>());

            try
            {
                string filter = term.Trim().ToLower();

                List<PosClientDTO> clients = await _context.Clients
                    .AsNoTracking()
                    .Where(c => c.Name.ToLower().Contains(filter)
                             || (c.Email != null && c.Email.ToLower().Contains(filter)))
                    .OrderBy(c => c.Name)
                    .Take(MaxClientResults)
                    .Select(c => new PosClientDTO { Id = c.Id, Name = c.Name, Email = c.Email })
                    .ToListAsync();

                return Response<List<PosClientDTO>>.Success(clients);
            }
            catch (Exception ex)
            {
                return Response<List<PosClientDTO>>.Failure(ex);
            }
        }

        public async Task<Response<PaginationResponse<SaleListItemDTO>>> GetPaginatedListAsync(PaginationRequest request)
        {
            try
            {
                IQueryable<Sale> query = _context.Sales.AsNoTracking();

                if (!string.IsNullOrWhiteSpace(request.Filter))
                {
                    string filter = request.Filter.Trim().ToLower();

                    // Si escriben un número, se busca la venta por su número exacto.
                    if (int.TryParse(filter, out int number))
                        query = query.Where(s => s.Number == number);
                    else
                        query = query.Where(s => (s.Client != null && s.Client.Name.ToLower().Contains(filter))
                                              || (s.User!.FirstName + " " + s.User.LastName).ToLower().Contains(filter));
                }

                IQueryable<SaleListItemDTO> projected = query
                    .OrderByDescending(s => s.Number)
                    .Select(s => new SaleListItemDTO
                    {
                        Id = s.Id,
                        Number = s.Number,
                        CreatedAtUtc = s.CreatedAtUtc,
                        CashierName = s.User!.FirstName + " " + s.User.LastName,
                        ClientName = s.Client != null ? s.Client.Name : null,
                        PaymentMethod = s.PaymentMethod,
                        Status = s.Status,
                        Total = s.Total
                    });

                PagedList<SaleListItemDTO> list = await PagedList<SaleListItemDTO>.ToPagedListAsync(projected, request);

                PaginationResponse<SaleListItemDTO> response = new PaginationResponse<SaleListItemDTO>
                {
                    List = list,
                    TotalCount = list.TotalCount,
                    RecordsPerPage = list.RecordsPerPage,
                    CurrentPage = list.CurrentPage,
                    TotalPages = list.TotalPages,
                    Filter = request.Filter
                };

                return Response<PaginationResponse<SaleListItemDTO>>.Success(response);
            }
            catch (Exception ex)
            {
                return Response<PaginationResponse<SaleListItemDTO>>.Failure(ex);
            }
        }

        public async Task<Response<SaleDetailDTO>> GetOneAsync(Guid id)
        {
            try
            {
                SaleDetailDTO? sale = await _context.Sales
                    .AsNoTracking()
                    .Where(s => s.Id == id)
                    .Select(s => new SaleDetailDTO
                    {
                        Id = s.Id,
                        Number = s.Number,
                        CreatedAtUtc = s.CreatedAtUtc,
                        CashierName = s.User!.FirstName + " " + s.User.LastName,
                        ClientName = s.Client != null ? s.Client.Name : null,
                        PaymentMethod = s.PaymentMethod,
                        Status = s.Status,
                        Total = s.Total,
                        Items = s.Items
                            .OrderBy(i => i.ProductName)
                            .Select(i => new SaleDetailLineDTO
                            {
                                ProductName = i.ProductName,
                                UnitPrice = i.UnitPrice,
                                Quantity = i.Quantity,
                                Unit = i.Unit,
                                Total = i.Total
                            })
                            .ToList()
                    })
                    .FirstOrDefaultAsync();

                if (sale is null)
                    return Response<SaleDetailDTO>.Failure("La venta no existe");

                return Response<SaleDetailDTO>.Success(sale, "Venta obtenida con éxito");
            }
            catch (Exception ex)
            {
                return Response<SaleDetailDTO>.Failure(ex);
            }
        }
    }
}