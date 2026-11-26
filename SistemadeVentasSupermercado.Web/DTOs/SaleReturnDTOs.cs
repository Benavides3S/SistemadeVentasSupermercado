using SistemadeVentasSupermercado.Web.Data.Entities;

namespace SistemadeVentasSupermercado.Web.DTOs
{
    // ---------- Lo que envía el formulario ----------

    /// <summary>
    /// Devolución pedida desde el formulario. Solo identificadores y cantidades:
    /// los precios salen de la venta original guardada en la base de datos.
    /// </summary>
    public class CreateSaleReturnDTO
    {
        public string Reason { get; set; } = string.Empty;
        public List<SaleReturnLineDTO> Lines { get; set; } = new();
    }

    public class SaleReturnLineDTO
    {
        public Guid SaleItemId { get; set; }
        public decimal Quantity { get; set; }

        /// <summary>true si el producto vuelve al inventario (false si está dañado o vencido).</summary>
        public bool Restock { get; set; } = true;
    }

    // ---------- Para armar el formulario ----------

    /// <summary>Venta con lo que todavía se puede devolver de cada línea.</summary>
    public class ReturnableSaleDTO
    {
        public Guid SaleId { get; set; }
        public int SaleNumber { get; set; }
        public DateTime CreatedAtUtc { get; set; }
        public PaymentMethod PaymentMethod { get; set; }
        public decimal Total { get; set; }
        public decimal ReturnedTotal { get; set; }
        public List<ReturnableLineDTO> Lines { get; set; } = new();
    }

    public class ReturnableLineDTO
    {
        public Guid SaleItemId { get; set; }
        public string ProductName { get; set; } = string.Empty;
        public decimal UnitPrice { get; set; }
        public UnitOfMeasure Unit { get; set; }
        public decimal SoldQuantity { get; set; }
        public decimal ReturnedQuantity { get; set; }
        public decimal RemainingQuantity => SoldQuantity - ReturnedQuantity;
        public bool AllowsDecimals => Unit != UnitOfMeasure.Unit;
    }

    // ---------- Comprobante de devolución ----------

    public class SaleReturnDetailDTO
    {
        public Guid Id { get; set; }
        public int Number { get; set; }
        public Guid SaleId { get; set; }
        public int SaleNumber { get; set; }
        public DateTime CreatedAtUtc { get; set; }
        /// <summary>Quién hizo la devolución (para saber si es de quien la consulta).</summary>
        public string UserId { get; set; } = string.Empty;
        public string UserName { get; set; } = string.Empty;
        /// <summary>Supervisor que la autorizó (null si quien la hizo tenía el permiso).</summary>
        public string? AuthorizedByName { get; set; }
        public string Reason { get; set; } = string.Empty;
        public PaymentMethod RefundMethod { get; set; }
        public decimal Total { get; set; }
        public List<SaleReturnDetailLineDTO> Items { get; set; } = new();
    }

    public class SaleReturnDetailLineDTO
    {
        public string ProductName { get; set; } = string.Empty;
        public decimal Quantity { get; set; }
        public UnitOfMeasure Unit { get; set; }
        public decimal UnitPrice { get; set; }
        public decimal Total { get; set; }
        public bool Restocked { get; set; }
    }

    /// <summary>Resumen de una devolución para mostrarla en el comprobante de la venta.</summary>
    public class SaleReturnSummaryDTO
    {
        public Guid Id { get; set; }
        public int Number { get; set; }
        public DateTime CreatedAtUtc { get; set; }
        public decimal Total { get; set; }
    }
}
