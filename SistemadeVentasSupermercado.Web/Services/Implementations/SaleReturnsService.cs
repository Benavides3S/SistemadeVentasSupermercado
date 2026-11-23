using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using SistemadeVentasSupermercado.Web.Core;
using SistemadeVentasSupermercado.Web.Data;
using SistemadeVentasSupermercado.Web.Data.Entities;
using SistemadeVentasSupermercado.Web.DTOs;
using SistemadeVentasSupermercado.Web.Services.Abstractions;

namespace SistemadeVentasSupermercado.Web.Services.Implementations
{
    public class SaleReturnsService : ISaleReturnsService
    {
        private readonly DataContext _context;

        public SaleReturnsService(DataContext context)
        {
            _context = context;
        }

        private static Response<SaleReturnDetailDTO> Fail(string message)
            => Response<SaleReturnDetailDTO>.Failure(message);

        public async Task<Response<ReturnableSaleDTO>> GetReturnableAsync(Guid saleId)
        {
            try
            {
                ReturnableSaleDTO? sale = await _context.Sales
                    .AsNoTracking()
                    .Where(s => s.Id == saleId)
                    .Select(s => new ReturnableSaleDTO
                    {
                        SaleId = s.Id,
                        SaleNumber = s.Number,
                        CreatedAtUtc = s.CreatedAtUtc,
                        PaymentMethod = s.PaymentMethod,
                        Total = s.Total,
                        ReturnedTotal = s.ReturnedTotal
                    })
                    .FirstOrDefaultAsync();

                if (sale is null)
                    return Response<ReturnableSaleDTO>.Failure("La venta no existe");

                Dictionary<Guid, decimal> returned = await ReturnedQuantitiesAsync(saleId);

                List<SaleItem> items = await _context.SaleItems
                    .AsNoTracking()
                    .Where(i => i.SaleId == saleId)
                    .OrderBy(i => i.ProductName)
                    .ToListAsync();

                sale.Lines = items.Select(i => new ReturnableLineDTO
                {
                    SaleItemId = i.Id,
                    ProductName = i.ProductName,
                    UnitPrice = i.UnitPrice,
                    Unit = i.Unit,
                    SoldQuantity = i.Quantity,
                    ReturnedQuantity = returned.GetValueOrDefault(i.Id)
                }).ToList();

                return Response<ReturnableSaleDTO>.Success(sale);
            }
            catch (Exception ex)
            {
                return Response<ReturnableSaleDTO>.Failure(ex);
            }
        }

        /// <summary>Cantidad ya devuelta de cada línea de la venta.</summary>
        private Task<Dictionary<Guid, decimal>> ReturnedQuantitiesAsync(Guid saleId)
            => _context.SaleReturnItems
                .Where(r => r.SaleReturn!.SaleId == saleId)
                .GroupBy(r => r.SaleItemId)
                .Select(g => new { SaleItemId = g.Key, Quantity = g.Sum(r => r.Quantity) })
                .ToDictionaryAsync(x => x.SaleItemId, x => x.Quantity);

        public async Task<Response<SaleReturnDetailDTO>> CreateAsync(Guid saleId, CreateSaleReturnDTO dto, string userId)
        {
            // ---------- Validaciones que no necesitan la base de datos ----------

            string reason = dto.Reason?.Trim() ?? string.Empty;
            if (reason.Length == 0)
                return Fail("El motivo de la devolución es obligatorio");
            if (reason.Length > 200)
                return Fail("El motivo puede tener máximo 200 caracteres");

            List<SaleReturnLineDTO> requested = (dto.Lines ?? new())
                .Select(l => new SaleReturnLineDTO
                {
                    SaleItemId = l.SaleItemId,
                    Quantity = Math.Round(l.Quantity, 3),
                    Restock = l.Restock
                })
                .ToList();

            if (requested.Any(l => l.Quantity < 0))
                return Fail("Las cantidades no pueden ser negativas");

            // El formulario envía todas las líneas: las que quedan en 0 no se devuelven.
            requested = requested.Where(l => l.Quantity > 0).ToList();

            if (requested.Count == 0)
                return Fail("Indica la cantidad de al menos un producto para devolver");

            if (requested.Select(l => l.SaleItemId).Distinct().Count() != requested.Count)
                return Fail("Cada producto puede aparecer una sola vez en la devolución");

            try
            {
                // Si salimos sin llegar a CommitAsync, la transacción se deshace sola.
                await using IDbContextTransaction transaction = await _context.Database.BeginTransactionAsync();

                var sale = await _context.Sales
                    .AsNoTracking()
                    .Where(s => s.Id == saleId)
                    .Select(s => new { s.Number, s.Status, s.PaymentMethod })
                    .FirstOrDefaultAsync();

                if (sale is null)
                    return Fail("La venta no existe");

                const string cancelled = "Esta venta está anulada: no admite devoluciones";
                if (sale.Status != SaleStatus.Completed)
                    return Fail(cancelled);

                // Se reembolsa con el mismo método con que se pagó.
                PaymentMethod refundMethod = sale.PaymentMethod;
                Guid? cashSessionId = null;

                // ---------- Bloqueos, siempre en el mismo orden: turno → venta → productos ----------

                // 1. Si el reembolso es en efectivo, el dinero sale del cajón de quien devuelve:
                //    necesita su caja abierta, y se bloquea para que un cierre simultáneo espere.
                if (refundMethod == PaymentMethod.Cash)
                {
                    const string noSession = "El reembolso es en efectivo: abre tu caja para hacer la devolución";

                    cashSessionId = await _context.CashSessions
                        .Where(c => c.UserId == userId && c.Status == CashSessionStatus.Open)
                        .Select(c => (Guid?)c.Id)
                        .FirstOrDefaultAsync();

                    if (cashSessionId is null)
                        return Fail(noSession);

                    int sessionLocked = await _context.CashSessions
                        .Where(c => c.Id == cashSessionId && c.Status == CashSessionStatus.Open)
                        .ExecuteUpdateAsync(s => s.SetProperty(c => c.Status, c => c.Status));

                    if (sessionLocked == 0)
                        return Fail(noSession);
                }

                // 2. Bloquea la fila de la venta (actualización sin cambio real) hasta el final.
                //    Dos devoluciones simultáneas de la misma venta se hacen una después de la otra,
                //    así la segunda ve lo que devolvió la primera y no se pasa de lo vendido.
                int saleLocked = await _context.Sales
                    .Where(s => s.Id == saleId && s.Status == SaleStatus.Completed)
                    .ExecuteUpdateAsync(s => s.SetProperty(x => x.ReturnedTotal, x => x.ReturnedTotal));

                if (saleLocked == 0)
                    return Fail(cancelled); // la anularon justo antes

                // Con la venta bloqueada, estas lecturas ya no cambian hasta el final.
                Dictionary<Guid, SaleItem> saleItems = await _context.SaleItems
                    .AsNoTracking()
                    .Where(i => i.SaleId == saleId)
                    .ToDictionaryAsync(i => i.Id);

                var alreadyReturned = await _context.SaleReturnItems
                    .Where(r => r.SaleReturn!.SaleId == saleId)
                    .GroupBy(r => r.SaleItemId)
                    .Select(g => new { SaleItemId = g.Key, Quantity = g.Sum(r => r.Quantity), Total = g.Sum(r => r.Total) })
                    .ToDictionaryAsync(x => x.SaleItemId);

                SaleReturn saleReturn = new SaleReturn
                {
                    Id = Guid.NewGuid(),
                    SaleId = saleId,
                    UserId = userId,
                    CashSessionId = cashSessionId,
                    CreatedAtUtc = DateTime.UtcNow,
                    Reason = reason,
                    RefundMethod = refundMethod
                };

                foreach (SaleReturnLineDTO line in requested)
                {
                    if (!saleItems.TryGetValue(line.SaleItemId, out SaleItem? item))
                        return Fail("Uno de los productos no pertenece a esta venta");

                    if (item.Unit == UnitOfMeasure.Unit && line.Quantity != Math.Floor(line.Quantity))
                        return Fail($"\"{item.ProductName}\" se vendió por unidad: la cantidad debe ser un número entero");

                    decimal returnedQty = alreadyReturned.TryGetValue(item.Id, out var prev) ? prev.Quantity : 0m;
                    decimal returnedMoney = alreadyReturned.TryGetValue(item.Id, out prev) ? prev.Total : 0m;
                    decimal remaining = item.Quantity - returnedQty;

                    if (line.Quantity > remaining)
                        return Fail($"De \"{item.ProductName}\" solo se pueden devolver {DisplayFormat.Quantity(remaining)} {item.Unit.Abbreviation()}");

                    // Si se devuelve todo lo que queda de la línea, se reembolsa exactamente el dinero
                    // que falta de esa línea. Así los redondeos de varias devoluciones parciales
                    // nunca suman más de lo que pagó el cliente.
                    decimal total = line.Quantity == remaining
                        ? item.Total - returnedMoney
                        : Math.Round(item.UnitPrice * line.Quantity, 2, MidpointRounding.AwayFromZero);

                    saleReturn.Items.Add(new SaleReturnItem
                    {
                        Id = Guid.NewGuid(),
                        SaleItemId = item.Id,
                        ProductId = item.ProductId,
                        ProductName = item.ProductName,
                        Quantity = line.Quantity,
                        UnitPrice = item.UnitPrice,
                        Unit = item.Unit,
                        Total = total,
                        Restocked = line.Restock
                    });
                }

                saleReturn.Total = saleReturn.Items.Sum(i => i.Total);

                // 3. Suma lo devuelto a la venta (la regla CK_Sales_ReturnedTotal_Range impide pasarse del total).
                decimal returnTotal = saleReturn.Total;
                await _context.Sales
                    .Where(s => s.Id == saleId)
                    .ExecuteUpdateAsync(s => s.SetProperty(x => x.ReturnedTotal, x => x.ReturnedTotal + returnTotal));

                // 4. Devuelve el stock de forma atómica (solo las líneas marcadas), ordenado por ProductId.
                var restock = saleReturn.Items
                    .Where(i => i.Restocked)
                    .GroupBy(i => i.ProductId)
                    .Select(g => new { ProductId = g.Key, Quantity = g.Sum(i => i.Quantity) })
                    .OrderBy(x => x.ProductId);

                // Historial de inventario: un movimiento por producto que vuelve al inventario. El número
                // de la devolución se conoce al guardar, así que se agregan después.
                List<InventoryMovement> movements = new();

                foreach (var r in restock)
                {
                    await _context.Products
                        .Where(p => p.Id == r.ProductId)
                        .ExecuteUpdateAsync(s => s.SetProperty(p => p.Stock, p => p.Stock + r.Quantity));

                    movements.Add(await InventoryRecorder.BuildAsync(
                        _context, r.ProductId, InventoryMovementType.SaleReturn, r.Quantity, userId, referenceId: saleReturn.Id));
                }

                _context.SaleReturns.Add(saleReturn);
                await _context.SaveChangesAsync();

                InventoryRecorder.Add(_context, movements, saleReturn.Number);
                await _context.SaveChangesAsync();
                await transaction.CommitAsync();

                Response<SaleReturnDetailDTO> result = await GetOneAsync(saleReturn.Id);
                if (result.IsSuccess)
                    result.Message = "Devolución registrada con éxito";
                return result;
            }
            catch (Exception ex)
            {
                return Response<SaleReturnDetailDTO>.Failure(ex);
            }
        }

        public async Task<Response<SaleReturnDetailDTO>> GetOneAsync(Guid id)
        {
            try
            {
                SaleReturnDetailDTO? detail = await _context.SaleReturns
                    .AsNoTracking()
                    .Where(r => r.Id == id)
                    .Select(r => new SaleReturnDetailDTO
                    {
                        Id = r.Id,
                        Number = r.Number,
                        SaleId = r.SaleId,
                        SaleNumber = r.Sale!.Number,
                        CreatedAtUtc = r.CreatedAtUtc,
                        UserName = r.User!.FirstName + " " + r.User.LastName,
                        Reason = r.Reason,
                        RefundMethod = r.RefundMethod,
                        Total = r.Total,
                        Items = r.Items
                            .OrderBy(i => i.ProductName)
                            .Select(i => new SaleReturnDetailLineDTO
                            {
                                ProductName = i.ProductName,
                                Quantity = i.Quantity,
                                Unit = i.Unit,
                                UnitPrice = i.UnitPrice,
                                Total = i.Total,
                                Restocked = i.Restocked
                            })
                            .ToList()
                    })
                    .FirstOrDefaultAsync();

                if (detail is null)
                    return Fail("La devolución no existe");

                return Response<SaleReturnDetailDTO>.Success(detail, "Devolución obtenida con éxito");
            }
            catch (Exception ex)
            {
                return Response<SaleReturnDetailDTO>.Failure(ex);
            }
        }
    }
}
