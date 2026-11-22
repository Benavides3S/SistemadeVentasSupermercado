using SistemadeVentasSupermercado.Web.Data.Entities;

namespace SistemadeVentasSupermercado.Web.DTOs
{
    /// <summary>Rango de fechas del reporte (días de Colombia, ambos incluidos).</summary>
    public class ReportRequest
    {
        public DateOnly? From { get; set; }
        public DateOnly? To { get; set; }
    }

    // ---------- Ventas por día ----------

    public class DailySalesReportDTO
    {
        public DateOnly From { get; set; }
        public DateOnly To { get; set; }
        public List<DailySalesRowDTO> Days { get; set; } = new();

        /// <summary>Suma de todos los días del rango.</summary>
        public DailySalesRowDTO Totals { get; set; } = new();
    }

    /// <summary>
    /// Un día. Ventas anuladas no cuentan. Las devoluciones cuentan el día en que se hicieron
    /// (así el reporte del día cuadra con la caja de ese día).
    /// </summary>
    public class DailySalesRowDTO
    {
        public DateOnly Date { get; set; }
        public int SalesCount { get; set; }
        public decimal SalesTotal { get; set; }
        public decimal CashTotal { get; set; }
        public decimal CardTotal { get; set; }
        public decimal TransferTotal { get; set; }
        public int ReturnsCount { get; set; }
        public decimal ReturnsTotal { get; set; }
        public decimal NetTotal => SalesTotal - ReturnsTotal;
    }

    // ---------- Más vendidos ----------

    /// <summary>
    /// Un producto en el ranking. Se ordena por dinero neto (vendido − devuelto en el rango),
    /// porque las cantidades mezclan unidades y kilos.
    /// </summary>
    public class TopProductDTO
    {
        public Guid ProductId { get; set; }
        public string ProductName { get; set; } = string.Empty;
        public UnitOfMeasure Unit { get; set; }
        public decimal QuantitySold { get; set; }
        public decimal QuantityReturned { get; set; }
        public decimal NetQuantity => QuantitySold - QuantityReturned;
        public decimal SalesTotal { get; set; }
        public decimal ReturnedTotal { get; set; }
        public decimal NetTotal => SalesTotal - ReturnedTotal;
    }
}
