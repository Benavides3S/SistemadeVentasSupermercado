using SistemadeVentasSupermercado.Web.Data.Entities;

namespace SistemadeVentasSupermercado.Web.DTOs
{
    /// <summary>Producto tal como lo ve el buscador del punto de venta.</summary>
    public class PosProductDTO
    {
        public Guid Id { get; set; }
        public string Name { get; set; } = string.Empty;
        public decimal Price { get; set; }
        public decimal Stock { get; set; }
        public UnitOfMeasure Unit { get; set; }
        public string UnitAbbreviation { get; set; } = string.Empty;

        /// <summary>true si se vende por peso/volumen (acepta decimales).</summary>
        public bool AllowsDecimals { get; set; }
    }

    public enum BarcodeLookupStatus
    {
        /// <summary>Existe y se puede vender: se agrega al carrito.</summary>
        Found = 1,
        /// <summary>Ningún producto tiene ese código (puede que sea un nombre: se busca por nombre).</summary>
        NotFound = 2,
        OutOfStock = 3,
        InactiveCategory = 4
    }

    /// <summary>Resultado de escanear un código en el punto de venta.</summary>
    public class BarcodeLookupDTO
    {
        public BarcodeLookupStatus Status { get; set; }

        /// <summary>Solo cuando Status = Found.</summary>
        public PosProductDTO? Product { get; set; }
    }

    /// <summary>Cliente tal como lo ve el buscador del punto de venta.</summary>
    public class PosClientDTO
    {
        public Guid Id { get; set; }
        public string Name { get; set; } = string.Empty;
        public string? Email { get; set; }
    }

    /// <summary>Una fila del historial de ventas.</summary>
    public class SaleListItemDTO
    {
        public Guid Id { get; set; }
        public int Number { get; set; }
        public DateTime CreatedAtUtc { get; set; }
        public string CashierName { get; set; } = string.Empty;
        public string? ClientName { get; set; }
        public PaymentMethod PaymentMethod { get; set; }
        public SaleStatus Status { get; set; }
        public decimal Total { get; set; }
        public decimal ReturnedTotal { get; set; }
    }

    /// <summary>Una venta completa, para el comprobante.</summary>
    public class SaleDetailDTO
    {
        public Guid Id { get; set; }
        public int Number { get; set; }
        public DateTime CreatedAtUtc { get; set; }
        public string CashierName { get; set; } = string.Empty;
        public string? ClientName { get; set; }
        public PaymentMethod PaymentMethod { get; set; }
        public SaleStatus Status { get; set; }
        public decimal Total { get; set; }
        public List<SaleDetailLineDTO> Items { get; set; } = new();

        // ---------- Anulación ----------
        public DateTime? CancelledAtUtc { get; set; }
        public string? CancelReason { get; set; }
        public string? CancelledByName { get; set; }

        /// <summary>Se puede anular si sigue completada, sin devoluciones y con su turno de caja abierto.</summary>
        public bool CanBeCancelled { get; set; }

        // ---------- Devoluciones ----------
        public decimal ReturnedTotal { get; set; }

        /// <summary>Se puede devolver si sigue completada y no se ha devuelto todo.</summary>
        public bool CanBeReturned { get; set; }

        public List<SaleReturnSummaryDTO> Returns { get; set; } = new();
    }

    /// <summary>Formulario de anulación.</summary>
    public class CancelSaleDTO
    {
        [System.ComponentModel.DataAnnotations.Required(ErrorMessage = "El motivo es obligatorio")]
        [System.ComponentModel.DataAnnotations.StringLength(200, ErrorMessage = "El motivo puede tener máximo {1} caracteres")]
        public string Reason { get; set; } = string.Empty;
    }

    public class SaleDetailLineDTO
    {
        public string ProductName { get; set; } = string.Empty;
        public decimal UnitPrice { get; set; }
        public decimal Quantity { get; set; }
        public UnitOfMeasure Unit { get; set; }
        public decimal Total { get; set; }
    }
}
