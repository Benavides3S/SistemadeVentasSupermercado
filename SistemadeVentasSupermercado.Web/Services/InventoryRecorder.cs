using Microsoft.EntityFrameworkCore;
using SistemadeVentasSupermercado.Web.Data;
using SistemadeVentasSupermercado.Web.Data.Entities;

namespace SistemadeVentasSupermercado.Web.Services
{
    /// <summary>
    /// Arma los movimientos del historial de inventario. Se usa SIEMPRE dentro de la misma transacción
    /// que cambia el stock y justo DESPUÉS de la actualización atómica del producto:
    ///  - La actualización deja la fila del producto bloqueada hasta el final de la transacción, así que
    ///    el stock que se lee aquí es exacto (nadie más lo puede cambiar) y es el "stock después" de este
    ///    movimiento. Por eso, para un mismo producto, el orden por Number es el orden real de los cambios.
    ///  - Si la operación falla, la transacción se deshace y no queda ningún movimiento.
    /// </summary>
    public static class InventoryRecorder
    {
        /// <summary>
        /// Crea el movimiento (sin agregarlo al contexto). Útil cuando el número de la venta, compra o
        /// devolución todavía no se conoce porque la base lo asigna al guardar: se llama justo después de
        /// actualizar cada producto y se agrega al contexto cuando ya existe el número (ver <see cref="Add"/>).
        /// </summary>
        public static async Task<InventoryMovement> BuildAsync(
            DataContext context,
            Guid productId,
            InventoryMovementType type,
            decimal quantity,
            string? userId,
            Guid? referenceId = null,
            int? referenceNumber = null,
            AdjustmentReason? reason = null,
            string? notes = null,
            Guid? idempotencyKey = null)
        {
            decimal stockAfter = await context.Products
                .AsNoTracking()
                .Where(p => p.Id == productId)
                .Select(p => p.Stock)
                .SingleAsync();

            return new InventoryMovement
            {
                Id = Guid.NewGuid(),
                CreatedAtUtc = DateTime.UtcNow,
                ProductId = productId,
                UserId = userId,
                Type = type,
                Quantity = quantity,
                StockAfter = stockAfter,
                ReferenceId = referenceId,
                ReferenceNumber = referenceNumber,
                Reason = reason,
                Notes = notes,
                IdempotencyKey = idempotencyKey
            };
        }

        /// <summary>Crea el movimiento y lo agrega al contexto: se guarda con el próximo SaveChanges de la transacción.</summary>
        public static async Task RecordAsync(
            DataContext context,
            Guid productId,
            InventoryMovementType type,
            decimal quantity,
            string? userId,
            Guid? referenceId = null,
            int? referenceNumber = null,
            AdjustmentReason? reason = null,
            string? notes = null,
            Guid? idempotencyKey = null)
        {
            context.InventoryMovements.Add(await BuildAsync(
                context, productId, type, quantity, userId, referenceId, referenceNumber, reason, notes, idempotencyKey));
        }

        /// <summary>
        /// Guarda movimientos armados antes de conocer el número de la operación, UNO POR UNO y en el
        /// orden recibido. No se guardan en lote porque EF ordena los lotes por la clave (un Guid al azar):
        /// el Number saldría en otro orden y, si un producto tiene dos movimientos en la misma operación,
        /// el historial ya no coincidiría con el orden real de los cambios.
        /// </summary>
        public static async Task SaveAsync(DataContext context, IEnumerable<InventoryMovement> movements, int referenceNumber)
        {
            foreach (InventoryMovement movement in movements)
            {
                movement.ReferenceNumber = referenceNumber;
                context.InventoryMovements.Add(movement);
                await context.SaveChangesAsync();
            }
        }
    }
}
