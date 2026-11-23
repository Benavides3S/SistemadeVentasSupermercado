using SistemadeVentasSupermercado.Web.Data.Abstractions;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace SistemadeVentasSupermercado.Web.Data.Entities
{
    /// <summary>Por qué cambió el stock de un producto.</summary>
    public enum InventoryMovementType
    {
        /// <summary>Stock con el que se creó el producto.</summary>
        [Display(Name = "Stock inicial")] InitialStock = 1,

        /// <summary>
        /// Saldo de los productos que ya existían cuando se activó el historial: el historial empieza
        /// aquí, no se reconstruye lo anterior.
        /// </summary>
        [Display(Name = "Saldo inicial del historial")] OpeningBalance = 2,

        [Display(Name = "Venta")] Sale = 3,
        [Display(Name = "Anulación de venta")] SaleCancellation = 4,
        [Display(Name = "Devolución de venta")] SaleReturn = 5,
        [Display(Name = "Compra")] Purchase = 6,
        [Display(Name = "Anulación de compra")] PurchaseCancellation = 7,
        [Display(Name = "Ajuste")] Adjustment = 8
    }

    /// <summary>Motivo de un ajuste manual de inventario.</summary>
    public enum AdjustmentReason
    {
        [Display(Name = "Merma, vencido o dañado")] Waste = 1,
        [Display(Name = "Pérdida o robo")] Loss = 2,
        [Display(Name = "Corrección por conteo físico")] PhysicalCount = 3,
        [Display(Name = "Consumo interno o muestra")] InternalUse = 4,
        [Display(Name = "Otro (explicar en las notas)")] Other = 5
    }

    public static class InventoryEnumExtensions
    {
        public static string Label(this InventoryMovementType type) => type switch
        {
            InventoryMovementType.InitialStock => "Stock inicial",
            InventoryMovementType.OpeningBalance => "Saldo inicial del historial",
            InventoryMovementType.Sale => "Venta",
            InventoryMovementType.SaleCancellation => "Anulación de venta",
            InventoryMovementType.SaleReturn => "Devolución de venta",
            InventoryMovementType.Purchase => "Compra",
            InventoryMovementType.PurchaseCancellation => "Anulación de compra",
            InventoryMovementType.Adjustment => "Ajuste",
            _ => type.ToString()
        };

        public static string Label(this AdjustmentReason reason) => reason switch
        {
            AdjustmentReason.Waste => "Merma, vencido o dañado",
            AdjustmentReason.Loss => "Pérdida o robo",
            AdjustmentReason.PhysicalCount => "Corrección por conteo físico",
            AdjustmentReason.InternalUse => "Consumo interno o muestra",
            AdjustmentReason.Other => "Otro",
            _ => reason.ToString()
        };
    }

    /// <summary>
    /// Una línea del historial de inventario. Solo se agregan filas: nunca se editan ni se borran.
    /// Cada cambio de stock deja una, dentro de la misma transacción que lo hizo, así que si la
    /// operación falla no queda ningún movimiento. <see cref="Product.Stock"/> sigue siendo el saldo
    /// real; esta tabla explica cómo se llegó a él.
    /// </summary>
    public class InventoryMovement : IId
    {
        [Key]
        public Guid Id { get; set; }

        /// <summary>Orden de los movimientos. Para un mismo producto coincide con el orden real de los cambios.</summary>
        [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
        [Display(Name = "Número")]
        public int Number { get; set; }

        [Display(Name = "Fecha")]
        public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;

        public Guid ProductId { get; set; }
        public Product? Product { get; set; }

        /// <summary>Quién hizo la operación. Vacío en el saldo inicial que crea el sistema al arrancar.</summary>
        public string? UserId { get; set; }
        public User? User { get; set; }

        [Display(Name = "Tipo")]
        public InventoryMovementType Type { get; set; }

        /// <summary>Cantidad que entró (positiva) o salió (negativa). Nunca es cero.</summary>
        [Display(Name = "Cantidad")]
        public decimal Quantity { get; set; }

        /// <summary>Stock del producto justo después de este movimiento.</summary>
        [Display(Name = "Stock después")]
        public decimal StockAfter { get; set; }

        /// <summary>
        /// Id de la venta, compra o devolución que originó el movimiento (sin FK a propósito: el
        /// historial no depende de esas tablas). Vacío en ajustes, stock inicial y saldo inicial.
        /// </summary>
        public Guid? ReferenceId { get; set; }

        /// <summary>Copia del número de esa venta, compra o devolución, para mostrarlo sin consultar otra tabla.</summary>
        public int? ReferenceNumber { get; set; }

        /// <summary>Solo en ajustes.</summary>
        [Display(Name = "Motivo")]
        public AdjustmentReason? Reason { get; set; }

        /// <summary>Solo en ajustes: obligatorio cuando el motivo es "Otro".</summary>
        [StringLength(200)]
        [Display(Name = "Notas")]
        public string? Notes { get; set; }

        /// <summary>
        /// Solo en ajustes: código único que genera el formulario. Si se envía dos veces (doble clic o
        /// reintento), el ajuste no se aplica de nuevo.
        /// </summary>
        public Guid? IdempotencyKey { get; set; }
    }
}
