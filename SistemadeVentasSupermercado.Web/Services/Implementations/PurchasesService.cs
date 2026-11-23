using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using SistemadeVentasSupermercado.Web.Core;
using SistemadeVentasSupermercado.Web.Data;
using SistemadeVentasSupermercado.Web.Data.Entities;
using SistemadeVentasSupermercado.Web.DTOs;
using SistemadeVentasSupermercado.Web.Services.Abstractions;

namespace SistemadeVentasSupermercado.Web.Services.Implementations
{
    public class PurchasesService : IPurchasesService
    {
        private readonly DataContext _context;

        // Límites para no desbordar decimal(18,2) / decimal(18,3) con datos absurdos.
        private const decimal MaxUnitCost = 1_000_000_000m;
        private const decimal MaxQuantity = 1_000_000m;

        private const string IdempotencyIndexName = "IX_Purchases_IdempotencyKey_UserId";

        public PurchasesService(DataContext context)
        {
            _context = context;
        }

        private static Response<PurchaseResultDTO> Fail(string message)
            => Response<PurchaseResultDTO>.Failure(message);

        /// <summary>
        /// Busca una compra del mismo usuario con el mismo código (un reintento).
        /// Se filtra también por usuario: nadie puede ver la compra de otro adivinando su código.
        /// </summary>
        private async Task<Response<PurchaseResultDTO>?> FindAlreadyRegisteredAsync(Guid idempotencyKey, string userId)
        {
            PurchaseResultDTO? existing = await _context.Purchases
                .AsNoTracking()
                .Where(p => p.IdempotencyKey == idempotencyKey && p.UserId == userId)
                .Select(p => new PurchaseResultDTO { Id = p.Id, Number = p.Number, Total = p.Total, AlreadyRegistered = true })
                .FirstOrDefaultAsync();

            return existing is null
                ? null
                : Response<PurchaseResultDTO>.Success(existing, "Esta compra ya estaba registrada");
        }

        /// <summary>2601 y 2627: valor duplicado en un índice único; se revisa que sea el del código de registro.</summary>
        private static bool IsDuplicateIdempotencyKey(DbUpdateException ex)
            => ex.GetBaseException() is SqlException sql
               && (sql.Number == 2601 || sql.Number == 2627)
               && sql.Message.Contains(IdempotencyIndexName, StringComparison.OrdinalIgnoreCase);

        private static string? NullIfEmpty(string? text)
        {
            string trimmed = text?.Trim() ?? string.Empty;
            return trimmed.Length == 0 ? null : trimmed;
        }

        public async Task<Response<PurchaseResultDTO>> CreateAsync(CreatePurchaseDTO dto, string userId)
        {
            // Sin código no se puede detectar un reintento (pantalla vieja abierta en el navegador).
            if (dto.IdempotencyKey == Guid.Empty)
                return Fail("Falta el código del registro. Recarga la página (F5) e inténtalo de nuevo.");

            if (dto.Items == null || dto.Items.Count == 0)
                return Fail("La compra debe tener al menos un producto");

            string? invoice = NullIfEmpty(dto.SupplierInvoiceNumber);
            string? notes = NullIfEmpty(dto.Notes);

            if (invoice is { Length: > 50 })
                return Fail("El número de factura puede tener máximo 50 caracteres");

            if (notes is { Length: > 200 })
                return Fail("Las notas pueden tener máximo 200 caracteres");

            // Se redondea igual que la base de datos (cantidad a 3 decimales, costo a 2).
            var rawLines = dto.Items
                .Select(i => new
                {
                    i.ProductId,
                    Quantity = Math.Round(i.Quantity, 3),
                    UnitCost = Math.Round(i.UnitCost, 2, MidpointRounding.AwayFromZero)
                })
                .ToList();

            if (rawLines.Any(l => l.Quantity <= 0))
                return Fail("Las cantidades deben ser mayores que cero");

            if (rawLines.Any(l => l.UnitCost <= 0))
                return Fail("Los costos deben ser mayores que cero");

            if (rawLines.Any(l => l.Quantity > MaxQuantity || l.UnitCost > MaxUnitCost))
                return Fail("Alguna cantidad o costo es demasiado grande");

            // Une las líneas del mismo producto con el mismo costo y las ordena por ProductId
            // (el orden es estable): así dos operaciones nunca se bloquean esperándose mutuamente.
            // Si un producto llega con costos distintos queda en líneas separadas y el último costo
            // ingresado es el que queda como "último costo" del producto.
            var lines = rawLines
                .Select((l, index) => new { l.ProductId, l.UnitCost, l.Quantity, Order = index })
                .GroupBy(l => new { l.ProductId, l.UnitCost })
                .Select(g => new
                {
                    g.Key.ProductId,
                    g.Key.UnitCost,
                    Quantity = g.Sum(x => x.Quantity),
                    Order = g.Max(x => x.Order) // posición de la última aparición: la última en ingresarse gana
                })
                .OrderBy(l => l.ProductId)
                .ThenBy(l => l.Order)
                .ToList();

            try
            {
                // ¿Es un reintento de una compra que ya quedó? Se revisa antes que todo lo demás.
                Response<PurchaseResultDTO>? alreadyRegistered = await FindAlreadyRegisteredAsync(dto.IdempotencyKey, userId);
                if (alreadyRegistered is not null)
                    return alreadyRegistered;

                // Si salimos sin llegar a CommitAsync, la transacción se deshace sola.
                await using IDbContextTransaction transaction = await _context.Database.BeginTransactionAsync();

                var supplier = await _context.Suppliers
                    .AsNoTracking()
                    .Where(s => s.Id == dto.SupplierId)
                    .Select(s => new { s.IsActive })
                    .FirstOrDefaultAsync();

                if (supplier is null)
                    return Fail("El proveedor seleccionado no existe");

                if (!supplier.IsActive)
                    return Fail("El proveedor está desactivado: actívalo antes de registrar compras");

                List<Guid> productIds = lines.Select(l => l.ProductId).Distinct().ToList();

                // Se permite comprar productos de categorías desactivadas (solo se bloquea su venta).
                Dictionary<Guid, Product> products = await _context.Products
                    .AsNoTracking()
                    .Where(p => productIds.Contains(p.Id))
                    .ToDictionaryAsync(p => p.Id);

                if (products.Count != productIds.Count)
                    return Fail("Alguno de los productos ya no existe");

                // Aviso (no error) si la factura ya se registró para este proveedor.
                string? warning = null;
                if (invoice is not null &&
                    await _context.Purchases.AnyAsync(p => p.SupplierId == dto.SupplierId
                                                           && p.SupplierInvoiceNumber == invoice
                                                           && p.Status == PurchaseStatus.Completed))
                {
                    warning = $"Ya hay otra compra de este proveedor con la factura \"{invoice}\". Revisa que no sea un duplicado.";
                }

                Purchase purchase = new Purchase
                {
                    Id = Guid.NewGuid(),
                    UserId = userId,
                    SupplierId = dto.SupplierId,
                    SupplierInvoiceNumber = invoice,
                    Notes = notes,
                    Status = PurchaseStatus.Completed,
                    CreatedAtUtc = DateTime.UtcNow,
                    IdempotencyKey = dto.IdempotencyKey
                };

                foreach (var line in lines)
                {
                    Product product = products[line.ProductId];
                    decimal quantity = line.Quantity;
                    decimal unitCost = line.UnitCost;

                    if (product.Unit == UnitOfMeasure.Unit && quantity != Math.Floor(quantity))
                        return Fail($"\"{product.Name}\" se maneja por unidad: la cantidad debe ser un número entero");

                    // Suma atómica del stock y último costo, sin leer-modificar-guardar.
                    int updated = await _context.Products
                        .Where(p => p.Id == product.Id)
                        .ExecuteUpdateAsync(s => s
                            .SetProperty(p => p.Stock, p => p.Stock + quantity)
                            .SetProperty(p => p.Cost, (decimal?)unitCost));

                    if (updated == 0)
                        return Fail($"El producto \"{product.Name}\" ya no existe");

                    purchase.Items.Add(new PurchaseItem
                    {
                        Id = Guid.NewGuid(),
                        ProductId = product.Id,
                        ProductName = product.Name,
                        Unit = product.Unit,
                        UnitCost = unitCost,
                        Quantity = quantity,
                        Total = Math.Round(unitCost * quantity, 2, MidpointRounding.AwayFromZero)
                    });
                }

                purchase.Total = purchase.Items.Sum(i => i.Total);

                _context.Purchases.Add(purchase);
                await _context.SaveChangesAsync();
                await transaction.CommitAsync();

                return Response<PurchaseResultDTO>.Success(
                    new PurchaseResultDTO { Id = purchase.Id, Number = purchase.Number, Total = purchase.Total, Warning = warning },
                    "Compra registrada con éxito");
            }
            catch (DbUpdateException ex) when (IsDuplicateIdempotencyKey(ex))
            {
                // Respaldo: la base de datos rechazó un segundo registro con el mismo código.
                // La transacción ya se deshizo (el stock volvió), así que se devuelve la compra que sí quedó.
                return await FindAlreadyRegisteredAsync(dto.IdempotencyKey, userId)
                       ?? Fail("No se pudo registrar la compra. Revisa el historial antes de volver a intentarlo.");
            }
            catch (Exception ex)
            {
                return Response<PurchaseResultDTO>.Failure(ex);
            }
        }

        // ---------- Anulación ----------

        public async Task<Response<PurchaseResultDTO>> CancelAsync(Guid purchaseId, CancelPurchaseDTO dto, string userId)
        {
            string reason = dto.Reason?.Trim() ?? string.Empty;

            if (reason.Length == 0)
                return Fail("El motivo de la anulación es obligatorio");

            if (reason.Length > 200)
                return Fail("El motivo puede tener máximo 200 caracteres");

            const string alreadyCancelled = "Esta compra ya está anulada";

            try
            {
                // Si salimos sin llegar a CommitAsync, la transacción se deshace sola
                // (también el cambio de estado si algún producto no alcanza).
                await using IDbContextTransaction transaction = await _context.Database.BeginTransactionAsync();

                var purchase = await _context.Purchases
                    .AsNoTracking()
                    .Where(p => p.Id == purchaseId)
                    .Select(p => new { p.Status, p.Number, p.Total })
                    .FirstOrDefaultAsync();

                if (purchase is null)
                    return Fail("La compra no existe");

                if (purchase.Status != PurchaseStatus.Completed)
                    return Fail(alreadyCancelled);

                // 1. Cambio de estado atómico: solo si sigue "Completada". Si dos personas anulan a la vez,
                //    una sola actualiza la fila (y la otra espera aquí), así el stock no se resta dos veces.
                //    Orden de bloqueo: compra → productos por ProductId.
                DateTime now = DateTime.UtcNow;
                int cancelled = await _context.Purchases
                    .Where(p => p.Id == purchaseId && p.Status == PurchaseStatus.Completed)
                    .ExecuteUpdateAsync(s => s
                        .SetProperty(x => x.Status, PurchaseStatus.Cancelled)
                        .SetProperty(x => x.CancelledAtUtc, now)
                        .SetProperty(x => x.CancelReason, reason)
                        .SetProperty(x => x.CancelledByUserId, userId));

                if (cancelled == 0)
                    return Fail(alreadyCancelled);

                // 2. Resta el stock de forma atómica, ordenado por ProductId, solo si todavía alcanza.
                var lines = await _context.PurchaseItems
                    .Where(i => i.PurchaseId == purchaseId)
                    .GroupBy(i => i.ProductId)
                    .Select(g => new { ProductId = g.Key, Quantity = g.Sum(i => i.Quantity) })
                    .ToListAsync();

                foreach (var line in lines.OrderBy(l => l.ProductId))
                {
                    decimal quantity = line.Quantity;

                    int updated = await _context.Products
                        .Where(p => p.Id == line.ProductId && p.Stock >= quantity)
                        .ExecuteUpdateAsync(s => s.SetProperty(p => p.Stock, p => p.Stock - quantity));

                    if (updated == 0)
                    {
                        var product = await _context.Products
                            .AsNoTracking()
                            .Where(p => p.Id == line.ProductId)
                            .Select(p => new { p.Name, p.Stock })
                            .FirstOrDefaultAsync();

                        return Fail(product is null
                            ? "Uno de los productos de esta compra ya no existe"
                            : $"No se puede anular: de \"{product.Name}\" se compraron {DisplayFormat.Quantity(quantity)} " +
                              $"y en el inventario solo quedan {DisplayFormat.Quantity(product.Stock)} " +
                              "(el resto ya se vendió o se ajustó). La compra sigue vigente.");
                    }
                }

                await transaction.CommitAsync();

                return Response<PurchaseResultDTO>.Success(
                    new PurchaseResultDTO { Id = purchaseId, Number = purchase.Number, Total = purchase.Total },
                    "Compra anulada con éxito");
            }
            catch (Exception ex)
            {
                return Response<PurchaseResultDTO>.Failure(ex);
            }
        }
    }
}
