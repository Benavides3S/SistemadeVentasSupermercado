using SistemadeVentasSupermercado.Web.Core.Pagination;
using SistemadeVentasSupermercado.Web.Data.Entities;
using System.ComponentModel.DataAnnotations;

namespace SistemadeVentasSupermercado.Web.DTOs
{
    /// <summary>Filtros del historial de inventario. Las fechas son días de Colombia, ambos incluidos.</summary>
    public class InventoryQueryDTO : PaginationRequest
    {
        public Guid? ProductId { get; set; }
        public InventoryMovementType? Type { get; set; }
        public DateOnly? From { get; set; }
        public DateOnly? To { get; set; }
    }

    public class InventoryMovementDTO
    {
        public Guid Id { get; set; }
        public int Number { get; set; }
        public DateTime CreatedAtUtc { get; set; }
        public Guid ProductId { get; set; }
        public string ProductName { get; set; } = string.Empty;
        public UnitOfMeasure Unit { get; set; }

        /// <summary>Vacío en el saldo inicial que crea el sistema al arrancar.</summary>
        public string? UserName { get; set; }

        public InventoryMovementType Type { get; set; }
        public decimal Quantity { get; set; }
        public decimal StockAfter { get; set; }

        /// <summary>Id y número de la venta, compra o devolución que originó el movimiento.</summary>
        public Guid? ReferenceId { get; set; }
        public int? ReferenceNumber { get; set; }

        public AdjustmentReason? Reason { get; set; }
        public string? Notes { get; set; }
    }

    /// <summary>Datos del producto que muestran el historial y el formulario de ajuste.</summary>
    public class InventoryProductDTO
    {
        public Guid Id { get; set; }
        public string Name { get; set; } = string.Empty;
        public string? Barcode { get; set; }
        public UnitOfMeasure Unit { get; set; }
        public decimal Stock { get; set; }
    }

    public enum AdjustmentDirection
    {
        [Display(Name = "Entrada (suma al stock)")] Increase = 1,
        [Display(Name = "Salida (resta del stock)")] Decrease = 2
    }

    public class AdjustInventoryDTO
    {
        public Guid ProductId { get; set; }

        public AdjustmentDirection Direction { get; set; } = AdjustmentDirection.Decrease;

        /// <summary>Cantidad positiva: la dirección dice si suma o resta.</summary>
        public decimal Quantity { get; set; }

        public AdjustmentReason Reason { get; set; } = AdjustmentReason.Waste;

        /// <summary>Obligatorio cuando el motivo es "Otro". Máximo 200 caracteres.</summary>
        public string? Notes { get; set; }

        /// <summary>Lo genera el formulario: si se envía dos veces (doble clic o reintento), el ajuste se aplica una sola vez.</summary>
        public Guid IdempotencyKey { get; set; }
    }

    public class AdjustInventoryResultDTO
    {
        public Guid MovementId { get; set; }
        public int Number { get; set; }
        public decimal StockAfter { get; set; }

        /// <summary>True si era un reintento de un ajuste que ya estaba aplicado (no se aplicó de nuevo).</summary>
        public bool AlreadyRegistered { get; set; }
    }
}
