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
    public class InventoryService : IInventoryService
    {
        private readonly DataContext _context;

        private const decimal MaxQuantity = 1_000_000m;
        private const string IdempotencyIndexName = "IX_InventoryMovements_IdempotencyKey_UserId";

        public InventoryService(DataContext context)
        {
            _context = context;
        }

        private static Response<AdjustInventoryResultDTO> Fail(string message)
            => Response<AdjustInventoryResultDTO>.Failure(message);

        // ---------- Consultas ----------

        public async Task<Response<PaginationResponse<InventoryMovementDTO>>> GetMovementsAsync(InventoryQueryDTO query)
        {
            try
            {
                if (query.From.HasValue && query.To.HasValue && query.From > query.To)
                    return Response<PaginationResponse<InventoryMovementDTO>>.Failure("La fecha inicial no puede ser posterior a la final");

                IQueryable<InventoryMovement> movements = _context.InventoryMovements.AsNoTracking();

                if (query.ProductId.HasValue)
                    movements = movements.Where(m => m.ProductId == query.ProductId);

                if (query.Type.HasValue)
                    movements = movements.Where(m => m.Type == query.Type);

                // Días de Colombia, ambos incluidos: desde el inicio del primero hasta el inicio del siguiente al último.
                if (query.From.HasValue)
                {
                    DateTime fromUtc = ColombiaCalendar.StartOfDayUtc(query.From.Value);
                    movements = movements.Where(m => m.CreatedAtUtc >= fromUtc);
                }
                if (query.To.HasValue)
                {
                    DateTime toUtcExclusive = ColombiaCalendar.StartOfDayUtc(query.To.Value.AddDays(1));
                    movements = movements.Where(m => m.CreatedAtUtc < toUtcExclusive);
                }

                if (!string.IsNullOrWhiteSpace(query.Filter))
                {
                    string filter = query.Filter.Trim().ToLower();
                    string code = filter.Replace(" ", "");
                    movements = movements.Where(m => m.Product!.Name.ToLower().Contains(filter)
                                                  || (m.Product.Barcode != null && m.Product.Barcode.Contains(code)));
                }

                IQueryable<InventoryMovementDTO> projected = movements
                    .OrderByDescending(m => m.Number)
                    .Select(m => new InventoryMovementDTO
                    {
                        Id = m.Id,
                        Number = m.Number,
                        CreatedAtUtc = m.CreatedAtUtc,
                        ProductId = m.ProductId,
                        ProductName = m.Product!.Name,
                        Unit = m.Product.Unit,
                        UserName = m.User == null ? null : m.User.FirstName + " " + m.User.LastName,
                        Type = m.Type,
                        Quantity = m.Quantity,
                        StockAfter = m.StockAfter,
                        ReferenceId = m.ReferenceId,
                        ReferenceNumber = m.ReferenceNumber,
                        Reason = m.Reason,
                        Notes = m.Notes
                    });

                PagedList<InventoryMovementDTO> list = await PagedList<InventoryMovementDTO>.ToPagedListAsync(projected, query);

                return Response<PaginationResponse<InventoryMovementDTO>>.Success(new PaginationResponse<InventoryMovementDTO>
                {
                    List = list,
                    TotalCount = list.TotalCount,
                    RecordsPerPage = list.RecordsPerPage,
                    CurrentPage = list.CurrentPage,
                    TotalPages = list.TotalPages,
                    Filter = query.Filter
                });
            }
            catch (Exception ex)
            {
                return Response<PaginationResponse<InventoryMovementDTO>>.Failure(ex);
            }
        }

        public async Task<Response<InventoryProductDTO>> GetProductAsync(Guid productId)
        {
            try
            {
                InventoryProductDTO? product = await _context.Products
                    .AsNoTracking()
                    .Where(p => p.Id == productId)
                    .Select(p => new InventoryProductDTO { Id = p.Id, Name = p.Name, Barcode = p.Barcode, Unit = p.Unit, Stock = p.Stock })
                    .FirstOrDefaultAsync();

                return product is null
                    ? Response<InventoryProductDTO>.Failure("El producto no existe")
                    : Response<InventoryProductDTO>.Success(product);
            }
            catch (Exception ex)
            {
                return Response<InventoryProductDTO>.Failure(ex);
            }
        }

        // ---------- Ajuste ----------

        /// <summary>Busca un ajuste del mismo usuario con el mismo código (un reintento).</summary>
        private async Task<Response<AdjustInventoryResultDTO>?> FindAlreadyRegisteredAsync(Guid idempotencyKey, string userId)
        {
            AdjustInventoryResultDTO? existing = await _context.InventoryMovements
                .AsNoTracking()
                .Where(m => m.IdempotencyKey == idempotencyKey && m.UserId == userId)
                .Select(m => new AdjustInventoryResultDTO { MovementId = m.Id, Number = m.Number, StockAfter = m.StockAfter, AlreadyRegistered = true })
                .FirstOrDefaultAsync();

            return existing is null
                ? null
                : Response<AdjustInventoryResultDTO>.Success(existing, "Este ajuste ya estaba registrado");
        }

        private static bool IsDuplicateIdempotencyKey(DbUpdateException ex)
            => ex.GetBaseException() is SqlException sql
               && (sql.Number == 2601 || sql.Number == 2627)
               && sql.Message.Contains(IdempotencyIndexName, StringComparison.OrdinalIgnoreCase);

        public async Task<Response<AdjustInventoryResultDTO>> AdjustAsync(AdjustInventoryDTO dto, string userId)
        {
            // ---------- Validaciones que no necesitan la base de datos ----------

            if (dto.IdempotencyKey == Guid.Empty)
                return Fail("Falta el código del ajuste. Recarga la página (F5) e inténtalo de nuevo.");

            if (!Enum.IsDefined(dto.Direction))
                return Fail("Indica si es una entrada o una salida");

            if (!Enum.IsDefined(dto.Reason))
                return Fail("Selecciona el motivo del ajuste");

            decimal quantity = Math.Round(dto.Quantity, 3);
            if (quantity <= 0)
                return Fail("La cantidad debe ser mayor que cero");
            if (quantity > MaxQuantity)
                return Fail($"La cantidad no puede ser mayor que {DisplayFormat.Quantity(MaxQuantity)}");

            string? notes = string.IsNullOrWhiteSpace(dto.Notes) ? null : dto.Notes.Trim();
            if (notes is not null && notes.Length > 200)
                return Fail("Las notas pueden tener máximo 200 caracteres");
            if (dto.Reason == AdjustmentReason.Other && notes is null)
                return Fail("Cuando el motivo es \"Otro\", explica qué pasó en las notas");

            try
            {
                // Un reintento (doble clic, corte de red) no se aplica de nuevo.
                Response<AdjustInventoryResultDTO>? previous = await FindAlreadyRegisteredAsync(dto.IdempotencyKey, userId);
                if (previous is not null)
                    return previous;

                var product = await _context.Products
                    .AsNoTracking()
                    .Where(p => p.Id == dto.ProductId)
                    .Select(p => new { p.Name, p.Unit })
                    .FirstOrDefaultAsync();

                if (product is null)
                    return Fail("El producto no existe");

                if (product.Unit == UnitOfMeasure.Unit && quantity != Math.Floor(quantity))
                    return Fail($"\"{product.Name}\" se maneja por unidad: la cantidad debe ser un número entero");

                bool increase = dto.Direction == AdjustmentDirection.Increase;
                decimal signed = increase ? quantity : -quantity;

                // Si salimos sin llegar a CommitAsync, la transacción se deshace sola.
                await using IDbContextTransaction transaction = await _context.Database.BeginTransactionAsync();

                // Una sola operación atómica en la base, igual que las ventas: una salida solo se aplica
                // si todavía alcanza (otro cajero pudo vender mientras tanto).
                int updated = await _context.Products
                    .Where(p => p.Id == dto.ProductId && (increase || p.Stock >= quantity))
                    .ExecuteUpdateAsync(s => s.SetProperty(p => p.Stock, p => p.Stock + signed));

                if (updated == 0)
                {
                    decimal current = await _context.Products.Where(p => p.Id == dto.ProductId).Select(p => p.Stock).SingleAsync();
                    return Fail($"Stock insuficiente de \"{product.Name}\": hay {DisplayFormat.Quantity(current)} y se intentó sacar {DisplayFormat.Quantity(quantity)}");
                }

                InventoryMovement movement = await InventoryRecorder.BuildAsync(
                    _context, dto.ProductId, InventoryMovementType.Adjustment, signed, userId,
                    reason: dto.Reason, notes: notes, idempotencyKey: dto.IdempotencyKey);

                _context.InventoryMovements.Add(movement);
                await _context.SaveChangesAsync();
                await transaction.CommitAsync();

                return Response<AdjustInventoryResultDTO>.Success(
                    new AdjustInventoryResultDTO { MovementId = movement.Id, Number = movement.Number, StockAfter = movement.StockAfter },
                    "Ajuste registrado con éxito");
            }
            catch (DbUpdateException ex) when (IsDuplicateIdempotencyKey(ex))
            {
                // Respaldo: dos envíos simultáneos con el mismo código. El segundo se deshizo y se devuelve el que quedó.
                return await FindAlreadyRegisteredAsync(dto.IdempotencyKey, userId)
                       ?? Fail("No se pudo registrar el ajuste. Revisa el historial antes de volver a intentarlo.");
            }
            catch (Exception ex)
            {
                return Response<AdjustInventoryResultDTO>.Failure(ex);
            }
        }
    }
}
