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
