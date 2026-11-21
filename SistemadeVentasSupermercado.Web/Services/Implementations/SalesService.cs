using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using SistemadeVentasSupermercado.Web.Core;
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
                    .Where(p => productIds.Contains(p.Id))
                    .ToDictionaryAsync(p => p.Id);

                if (products.Count != productIds.Count)
                    return Fail("Alguno de los productos ya no existe");

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
    }
}