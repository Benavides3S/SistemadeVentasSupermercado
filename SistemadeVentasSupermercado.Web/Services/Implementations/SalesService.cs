using Microsoft.Data.SqlClient;
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

        private const string IdempotencyIndexName = "IX_Sales_IdempotencyKey_UserId";

        /// <summary>
        /// Busca una venta del mismo cajero con el mismo código de cobro (un reintento).
        /// Se filtra también por cajero: nadie puede ver la venta de otro adivinando su código.
        /// </summary>
        private async Task<Response<SaleResultDTO>?> FindAlreadyRegisteredAsync(Guid idempotencyKey, string userId)
        {
            SaleResultDTO? existing = await _context.Sales
                .AsNoTracking()
                .Where(s => s.IdempotencyKey == idempotencyKey && s.UserId == userId)
                .Select(s => new SaleResultDTO { Id = s.Id, Number = s.Number, Total = s.Total, AlreadyRegistered = true })
                .FirstOrDefaultAsync();

            return existing is null
                ? null
                : Response<SaleResultDTO>.Success(existing, "Esta venta ya estaba registrada");
        }

        /// <summary>2601 y 2627: valor duplicado en un índice único; se revisa que sea el del código de cobro.</summary>
        private static bool IsDuplicateIdempotencyKey(DbUpdateException ex)
            => ex.GetBaseException() is SqlException sql
               && (sql.Number == 2601 || sql.Number == 2627)
               && sql.Message.Contains(IdempotencyIndexName, StringComparison.OrdinalIgnoreCase);

        public async Task<Response<SaleResultDTO>> CreateAsync(CreateSaleDTO dto, string userId)
        {
            // Sin código no se puede detectar un reintento. Pasa si la página quedó abierta
            // desde antes de este cambio (pos.js viejo en el navegador).
            if (dto.IdempotencyKey == Guid.Empty)
                return Fail("Falta el código del cobro. Recarga la página (F5) e inténtalo de nuevo.");

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
                // ¿Es un reintento de una venta que ya quedó? (por ejemplo, se cortó la red antes de
                // recibir la respuesta). Se revisa ANTES de mirar la caja: si la venta quedó y luego
                // cerraron el turno, el cajero debe ver su venta y no "Abre la caja".
                Response<SaleResultDTO>? alreadyRegistered = await FindAlreadyRegisteredAsync(dto.IdempotencyKey, userId);
                if (alreadyRegistered is not null)
                    return alreadyRegistered;

                // Si salimos sin llegar a CommitAsync, la transacción se deshace sola.
                await using IDbContextTransaction transaction = await _context.Database.BeginTransactionAsync();

                // Toda venta pertenece al turno de caja abierto del cajero.
                const string noOpenSession = "Abre la caja para empezar a vender";

                Guid? cashSessionId = await _context.CashSessions
                    .Where(c => c.UserId == userId && c.Status == CashSessionStatus.Open)
                    .Select(c => (Guid?)c.Id)
                    .FirstOrDefaultAsync();

                if (cashSessionId is null)
                    return Fail(noOpenSession);

                // Bloquea la fila del turno hasta el final de la transacción (actualización sin cambio real).
                // Así un cierre de caja simultáneo espera a que esta venta termine, y si la caja
                // se cerró justo antes, no se actualiza ninguna fila y la venta se rechaza.
                // Orden de bloqueo en toda venta: primero el turno, después los productos por ProductId.
                int sessionLocked = await _context.CashSessions
                    .Where(c => c.Id == cashSessionId && c.Status == CashSessionStatus.Open)
                    .ExecuteUpdateAsync(s => s.SetProperty(c => c.Status, c => c.Status));

                if (sessionLocked == 0)
                    return Fail(noOpenSession);

                // Se revisa otra vez con el turno bloqueado: si dos reintentos llegan a la vez, el
                // segundo esperó aquí a que el primero terminara, y ahora encuentra su venta.
                alreadyRegistered = await FindAlreadyRegisteredAsync(dto.IdempotencyKey, userId);
                if (alreadyRegistered is not null)
                    return alreadyRegistered;

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
                    CashSessionId = cashSessionId,
                    PaymentMethod = dto.PaymentMethod,
                    Status = SaleStatus.Completed,
                    CreatedAtUtc = DateTime.UtcNow,
                    IdempotencyKey = dto.IdempotencyKey
                };

                // Historial de inventario: se arma un movimiento por producto justo después de descontarlo
                // y se guarda junto con la venta (el número de la venta se conoce al guardar).
                List<InventoryMovement> movements = new();

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

                    movements.Add(await InventoryRecorder.BuildAsync(
                        _context, product.Id, InventoryMovementType.Sale, -quantity, userId, referenceId: sale.Id));

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

                // Ya existe el número de la venta: se guardan los movimientos del historial (misma transacción).
                InventoryRecorder.Add(_context, movements, sale.Number);
                await _context.SaveChangesAsync();
                await transaction.CommitAsync();

                return Response<SaleResultDTO>.Success(
                    new SaleResultDTO { Id = sale.Id, Number = sale.Number, Total = sale.Total },
                    "Venta registrada con éxito");
            }
            catch (DbUpdateException ex) when (IsDuplicateIdempotencyKey(ex))
            {
                // Respaldo: la base de datos rechazó un segundo registro con el mismo código.
                // La transacción ya se deshizo (el stock volvió), así que se devuelve la venta que sí quedó.
                return await FindAlreadyRegisteredAsync(dto.IdempotencyKey, userId)
                       ?? Fail("No se pudo registrar la venta. Revisa el historial antes de volver a cobrar.");
            }
            catch (Exception ex)
            {
                return Response<SaleResultDTO>.Failure(ex);
            }
        }

        // ---------- Anulación ----------

        public async Task<Response<SaleDetailDTO>> CancelAsync(Guid saleId, CancelSaleDTO dto, string userId)
        {
            string reason = dto.Reason?.Trim() ?? string.Empty;

            if (reason.Length == 0)
                return Response<SaleDetailDTO>.Failure("El motivo de la anulación es obligatorio");

            if (reason.Length > 200)
                return Response<SaleDetailDTO>.Failure("El motivo puede tener máximo 200 caracteres");

            const string alreadyCancelled = "Esta venta ya está anulada";
            const string sessionClosed = "El turno de caja de esta venta ya cerró: haz una devolución en lugar de anularla";

            try
            {
                // Si salimos sin llegar a CommitAsync, la transacción se deshace sola.
                await using IDbContextTransaction transaction = await _context.Database.BeginTransactionAsync();

                var sale = await _context.Sales
                    .AsNoTracking()
                    .Where(s => s.Id == saleId)
                    .Select(s => new { s.Number, s.Status, s.CashSessionId, s.ReturnedTotal })
                    .FirstOrDefaultAsync();

                if (sale is null)
                    return Response<SaleDetailDTO>.Failure("La venta no existe");

                if (sale.Status != SaleStatus.Completed)
                    return Response<SaleDetailDTO>.Failure(alreadyCancelled);

                // Con devoluciones, anular devolvería el stock dos veces.
                const string hasReturns = "Esta venta tiene devoluciones: no se puede anular (haz otra devolución por lo que falte)";
                if (sale.ReturnedTotal > 0)
                    return Response<SaleDetailDTO>.Failure(hasReturns);

                // Ventas anteriores a la caja no tienen turno: solo admiten devolución.
                if (sale.CashSessionId is null)
                    return Response<SaleDetailDTO>.Failure(sessionClosed);

                // 1. Bloquea el turno (mismo orden que una venta: turno → productos).
                //    Si se cerró, no se actualiza nada y se rechaza: un arqueo cerrado no se altera.
                int sessionLocked = await _context.CashSessions
                    .Where(c => c.Id == sale.CashSessionId && c.Status == CashSessionStatus.Open)
                    .ExecuteUpdateAsync(s => s.SetProperty(c => c.Status, c => c.Status));

                if (sessionLocked == 0)
                    return Response<SaleDetailDTO>.Failure(sessionClosed);

                // 2. Cambio de estado atómico: solo si sigue "Completada" y sin devoluciones.
                //    Si dos supervisores anulan a la vez (o alguien hace una devolución
                //    en ese instante), solo una operación actualiza la fila.
                DateTime now = DateTime.UtcNow;
                int cancelled = await _context.Sales
                    .Where(s => s.Id == saleId && s.Status == SaleStatus.Completed && s.ReturnedTotal == 0)
                    .ExecuteUpdateAsync(s => s
                        .SetProperty(x => x.Status, SaleStatus.Cancelled)
                        .SetProperty(x => x.CancelledAtUtc, now)
                        .SetProperty(x => x.CancelReason, reason)
                        .SetProperty(x => x.CancelledByUserId, userId));

                if (cancelled == 0)
                {
                    // Algo cambió justo antes: se averigua qué para dar el mensaje correcto.
                    bool returnedMeanwhile = await _context.Sales.AnyAsync(s => s.Id == saleId && s.ReturnedTotal > 0);
                    return Response<SaleDetailDTO>.Failure(returnedMeanwhile ? hasReturns : alreadyCancelled);
                }

                // 3. Devuelve el stock de forma atómica, ordenado por ProductId (evita bloqueos mutuos).
                var lines = await _context.SaleItems
                    .Where(i => i.SaleId == saleId)
                    .GroupBy(i => i.ProductId)
                    .Select(g => new { ProductId = g.Key, Quantity = g.Sum(i => i.Quantity) })
                    .ToListAsync();

                foreach (var line in lines.OrderBy(l => l.ProductId))
                {
                    await _context.Products
                        .Where(p => p.Id == line.ProductId)
                        .ExecuteUpdateAsync(s => s.SetProperty(p => p.Stock, p => p.Stock + line.Quantity));

                    await InventoryRecorder.RecordAsync(_context, line.ProductId, InventoryMovementType.SaleCancellation,
                        line.Quantity, userId, referenceId: saleId, referenceNumber: sale.Number);
                }

                await _context.SaveChangesAsync();
                await transaction.CommitAsync();
            }
            catch (Exception ex)
            {
                return Response<SaleDetailDTO>.Failure(ex);
            }

            Response<SaleDetailDTO> result = await GetOneAsync(saleId);
            if (result.IsSuccess)
                result.Message = "Venta anulada con éxito";
            return result;
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
                string? code = BarcodeHelper.Normalize(term);

                List<PosProductDTO> products = await _context.Products
                    .AsNoTracking()
                    .Where(p => p.Stock > 0
                             && p.Category != null && p.Category.IsActive
                             && (p.Name.ToLower().Contains(filter) || (code != null && p.Barcode == code)))
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

                products.ForEach(CompletePosProduct);

                return Response<List<PosProductDTO>>.Success(products);
            }
            catch (Exception ex)
            {
                return Response<List<PosProductDTO>>.Failure(ex);
            }
        }

        public async Task<Response<BarcodeLookupDTO>> FindByBarcodeAsync(string? code)
        {
            string? normalized = BarcodeHelper.Normalize(code);
            if (normalized is null || BarcodeHelper.Validate(normalized) is not null)
                return Response<BarcodeLookupDTO>.Success(new BarcodeLookupDTO { Status = BarcodeLookupStatus.NotFound },
                                                          $"No hay ningún producto con el código {code?.Trim()}.");

            try
            {
                var product = await _context.Products
                    .AsNoTracking()
                    .Where(p => p.Barcode == normalized)
                    .Select(p => new
                    {
                        Pos = new PosProductDTO { Id = p.Id, Name = p.Name, Price = p.Price, Stock = p.Stock, Unit = p.Unit },
                        CategoryActive = p.Category != null && p.Category.IsActive
                    })
                    .SingleOrDefaultAsync();

                if (product is null)
                    return Response<BarcodeLookupDTO>.Success(new BarcodeLookupDTO { Status = BarcodeLookupStatus.NotFound },
                                                              $"No hay ningún producto con el código {normalized}.");

                // Mismas reglas que el buscador por nombre y que la venta: sin stock o con la categoría desactivada no se vende.
                if (!product.CategoryActive)
                    return Response<BarcodeLookupDTO>.Success(new BarcodeLookupDTO { Status = BarcodeLookupStatus.InactiveCategory },
                                                              $"\"{product.Pos.Name}\" es de una categoría desactivada: no se puede vender.");

                if (product.Pos.Stock <= 0)
                    return Response<BarcodeLookupDTO>.Success(new BarcodeLookupDTO { Status = BarcodeLookupStatus.OutOfStock },
                                                              $"\"{product.Pos.Name}\" no tiene stock.");

                CompletePosProduct(product.Pos);
                return Response<BarcodeLookupDTO>.Success(new BarcodeLookupDTO { Status = BarcodeLookupStatus.Found, Product = product.Pos });
            }
            catch (Exception ex)
            {
                return Response<BarcodeLookupDTO>.Failure(ex);
            }
        }

        private static void CompletePosProduct(PosProductDTO product)
        {
            product.UnitAbbreviation = product.Unit.Abbreviation();
            product.AllowsDecimals = product.Unit != UnitOfMeasure.Unit;
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
                        Total = s.Total,
                        ReturnedTotal = s.ReturnedTotal
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
                        CancelledAtUtc = s.CancelledAtUtc,
                        CancelReason = s.CancelReason,
                        CancelledByName = s.CancelledByUser != null ? s.CancelledByUser.FirstName + " " + s.CancelledByUser.LastName : null,
                        CanBeCancelled = s.Status == SaleStatus.Completed
                                      && s.ReturnedTotal == 0
                                      && s.CashSession != null
                                      && s.CashSession.Status == CashSessionStatus.Open,
                        ReturnedTotal = s.ReturnedTotal,
                        CanBeReturned = s.Status == SaleStatus.Completed && s.ReturnedTotal < s.Total,
                        Returns = s.Returns
                            .OrderBy(r => r.Number)
                            .Select(r => new SaleReturnSummaryDTO
                            {
                                Id = r.Id,
                                Number = r.Number,
                                CreatedAtUtc = r.CreatedAtUtc,
                                Total = r.Total
                            })
                            .ToList(),
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