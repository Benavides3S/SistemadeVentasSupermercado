using SistemadeVentasSupermercado.Web.Data.Entities;

namespace SistemadeVentasSupermercado.Web.DTOs
{
    /// <summary>Qué datos quiere ver quien abre el Inicio (según sus permisos).</summary>
    public class HomeRequestDTO
    {
        public string UserId { get; set; } = string.Empty;
        public bool WantsSalesToday { get; set; }
        public bool WantsOwnCashSession { get; set; }
        public bool WantsLowStock { get; set; }
    }

    public class HomeSummaryDTO
    {
        /// <summary>Ventas completadas hoy (día de Colombia). Null si no se pidió.</summary>
        public int? SalesCountToday { get; set; }
        public decimal SalesTotalToday { get; set; }

        /// <summary>Null = no se pidió; HasOpenCashSession dice si el usuario tiene turno abierto.</summary>
        public bool? HasOpenCashSession { get; set; }
        public DateTime? CashSessionOpenedAtUtc { get; set; }

        public List<LowStockProductDTO>? LowStock { get; set; }
    }

    public class LowStockProductDTO
    {
        public Guid Id { get; set; }
        public string Name { get; set; } = string.Empty;
        public decimal Stock { get; set; }
        public UnitOfMeasure Unit { get; set; }
    }
}
