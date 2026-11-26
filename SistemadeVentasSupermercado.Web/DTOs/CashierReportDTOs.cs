using SistemadeVentasSupermercado.Web.Data.Entities;

namespace SistemadeVentasSupermercado.Web.DTOs
{
    /// <summary>
    /// Lo que hizo una persona en el rango. "Cobró" y "ejecutó/registró" se cuentan aparte porque pueden
    /// ser personas distintas: no se suman entre sí.
    /// </summary>
    public class CashierRowDTO
    {
        public string UserId { get; set; } = string.Empty;
        public string Name { get; set; } = string.Empty;
        public bool IsDeactivated { get; set; }

        // Ventas completadas que cobró (por la fecha del cobro).
        public int SalesCount { get; set; }
        public decimal SalesTotal { get; set; }
        public decimal CashTotal { get; set; }
        public decimal CardTotal { get; set; }
        public decimal TransferTotal { get; set; }

        // Anulaciones (por la fecha de la anulación).
        public int OwnSalesCancelledCount { get; set; }
        public decimal OwnSalesCancelledTotal { get; set; }
        public int CancellationsDoneCount { get; set; }
        public decimal CancellationsDoneTotal { get; set; }

        // Devoluciones (por la fecha en que se registraron).
        public int ReturnsRegisteredCount { get; set; }
        public decimal ReturnsRegisteredTotal { get; set; }
        public int ReturnsOnOwnSalesCount { get; set; }
        public decimal ReturnsOnOwnSalesTotal { get; set; }

        // Turnos propios cerrados en el rango. Faltantes y sobrantes NO se compensan entre sí.
        public int ClosedSessionsCount { get; set; }
        public int UnbalancedSessionsCount { get; set; }
        public decimal ShortageTotal { get; set; }
        public decimal SurplusTotal { get; set; }

        /// <summary>Turnos abiertos AHORA, sin importar el rango.</summary>
        public int OpenSessionsCount { get; set; }
    }

    public class CashierReportDTO
    {
        public DateOnly From { get; set; }
        public DateOnly To { get; set; }
        public List<CashierRowDTO> Rows { get; set; } = new();
        /// <summary>Suma de las filas que se muestran.</summary>
        public CashierRowDTO Totals { get; set; } = new();
        /// <summary>Quien consulta tiene la caja abierta: sus cifras no se incluyen (arqueo ciego).</summary>
        public bool OwnFiguresHidden { get; set; }
    }

    public class CashierCancellationDTO
    {
        public Guid SaleId { get; set; }
        public int SaleNumber { get; set; }
        public DateTime SoldAtUtc { get; set; }
        public DateTime CancelledAtUtc { get; set; }
        public decimal Total { get; set; }
        public string Reason { get; set; } = string.Empty;
        public string ChargedByName { get; set; } = string.Empty;
        public string CancelledByName { get; set; } = string.Empty;
        /// <summary>Supervisor que la autorizó con su PIN (null si no hizo falta).</summary>
        public string? AuthorizedByName { get; set; }
    }

    public class CashierReturnDTO
    {
        public Guid ReturnId { get; set; }
        public int ReturnNumber { get; set; }
        public Guid SaleId { get; set; }
        public int SaleNumber { get; set; }
        public DateTime CreatedAtUtc { get; set; }
        public decimal Total { get; set; }
        public PaymentMethod RefundMethod { get; set; }
        public string Reason { get; set; } = string.Empty;
        public string ChargedByName { get; set; } = string.Empty;
        public string RegisteredByName { get; set; } = string.Empty;
        /// <summary>Supervisor que la autorizó con su PIN (null si no hizo falta).</summary>
        public string? AuthorizedByName { get; set; }
    }

    public class CashierSessionDTO
    {
        public Guid SessionId { get; set; }
        public int Number { get; set; }
        public string CashierName { get; set; } = string.Empty;
        public bool IsOpen { get; set; }
        public DateTime OpenedAtUtc { get; set; }
        public DateTime? ClosedAtUtc { get; set; }
        public decimal? ExpectedAmount { get; set; }
        public decimal? CountedAmount { get; set; }
        public decimal? Difference { get; set; }
        public string? Notes { get; set; }
        public string? ClosedByName { get; set; }
        /// <summary>Lo cerró otra persona: puede que quien contó el dinero no sea el cajero.</summary>
        public bool ClosedByOther { get; set; }
    }

    public class CashierDetailsDTO
    {
        public DateOnly From { get; set; }
        public DateOnly To { get; set; }
        /// <summary>La persona consultada; null si el detalle es de todas.</summary>
        public CashierRowDTO? Cashier { get; set; }
        public List<CashierCancellationDTO> Cancellations { get; set; } = new();
        public List<CashierReturnDTO> Returns { get; set; } = new();
        public List<CashierSessionDTO> Sessions { get; set; } = new();
        public bool OwnFiguresHidden { get; set; }
    }
}
