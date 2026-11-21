using SistemadeVentasSupermercado.Web.Data.Entities;
using System.ComponentModel.DataAnnotations;

namespace SistemadeVentasSupermercado.Web.DTOs
{
    /// <summary>Formulario para abrir caja.</summary>
    public class OpenCashSessionDTO
    {
        [Display(Name = "Monto inicial en efectivo")]
        [Range(0, 999_999_999, ErrorMessage = "El {0} no puede ser negativo")]
        public decimal OpeningAmount { get; set; }
    }

    /// <summary>Formulario de cierre (arqueo ciego: el cajero no ve el esperado).</summary>
    public class CloseCashSessionDTO
    {
        [Display(Name = "Efectivo contado")]
        [Required(ErrorMessage = "El campo {0} es requerido")]
        [Range(0, 999_999_999, ErrorMessage = "El {0} no puede ser negativo")]
        public decimal? CountedAmount { get; set; }

        [Display(Name = "Observaciones")]
        [StringLength(500, ErrorMessage = "Las {0} pueden tener máximo {1} caracteres")]
        public string? Notes { get; set; }
    }

    /// <summary>Datos de un turno de caja.</summary>
    public class CashSessionDTO
    {
        public Guid Id { get; set; }
        public int Number { get; set; }
        public string UserId { get; set; } = string.Empty;
        public string CashierName { get; set; } = string.Empty;
        public DateTime OpenedAtUtc { get; set; }
        public decimal OpeningAmount { get; set; }
        public CashSessionStatus Status { get; set; }
        public DateTime? ClosedAtUtc { get; set; }
        public string? ClosedByName { get; set; }
        public decimal? CountedAmount { get; set; }
        public decimal? ExpectedAmount { get; set; }
        public decimal? Difference { get; set; }
        public string? Notes { get; set; }
    }

    /// <summary>Totales de un método de pago dentro de un turno.</summary>
    public class PaymentTotalDTO
    {
        public PaymentMethod PaymentMethod { get; set; }
        public int Count { get; set; }
        public decimal Total { get; set; }
    }

    /// <summary>Reporte de caja: el turno más sus totales.</summary>
    public class CashSessionSummaryDTO
    {
        public CashSessionDTO Session { get; set; } = new();

        /// <summary>Ventas completadas por método de pago (siempre trae los tres métodos).</summary>
        public List<PaymentTotalDTO> Payments { get; set; } = new();

        public int SalesCount { get; set; }
        public decimal SalesTotal { get; set; }

        public int CancelledCount { get; set; }
        public decimal CancelledTotal { get; set; }

        /// <summary>Devoluciones reembolsadas en efectivo desde este turno.</summary>
        public int CashRefundsCount { get; set; }
        public decimal CashRefundsTotal { get; set; }

        /// <summary>
        /// Efectivo que debería haber en el cajón: monto inicial + ventas completadas en efectivo
        /// − reembolsos en efectivo. Si el turno está cerrado, es el valor guardado al cerrar.
        /// </summary>
        public decimal ExpectedCash { get; set; }
    }
}
