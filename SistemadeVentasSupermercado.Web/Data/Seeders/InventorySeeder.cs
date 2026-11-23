using Microsoft.EntityFrameworkCore;
using SistemadeVentasSupermercado.Web.Data.Entities;

namespace SistemadeVentasSupermercado.Web.Data.Seeders
{
    /// <summary>
    /// Da un punto de partida al historial de inventario: cada producto que ya tiene stock pero ningún
    /// movimiento recibe uno de "Saldo inicial del historial" por su stock actual. No se reconstruye el
    /// pasado: el historial empieza el día en que se activa.
    /// Es seguro repetirlo en cada arranque (solo toca productos sin movimientos) y es una sola
    /// instrucción SQL, así que no hay riesgo de leer un stock y que cambie antes de guardarlo.
    /// </summary>
    public class InventorySeeder
    {
        private readonly DataContext _context;

        public InventorySeeder(DataContext context)
        {
            _context = context;
        }

        /// <returns>Cuántos productos recibieron su saldo inicial.</returns>
        public async Task<int> SeedOpeningBalanceAsync()
        {
            int type = (int)InventoryMovementType.OpeningBalance;

            int created = await _context.Database.ExecuteSqlInterpolatedAsync($@"
                INSERT INTO InventoryMovements (Id, CreatedAtUtc, ProductId, UserId, Type, Quantity, StockAfter)
                SELECT NEWID(), SYSUTCDATETIME(), p.Id, NULL, {type}, p.Stock, p.Stock
                FROM Products p
                WHERE p.Stock > 0
                  AND NOT EXISTS (SELECT 1 FROM InventoryMovements m WHERE m.ProductId = p.Id)");

            if (created > 0)
                Console.WriteLine($"Historial de inventario: saldo inicial creado para {created} producto(s).");

            return created;
        }
    }
}
