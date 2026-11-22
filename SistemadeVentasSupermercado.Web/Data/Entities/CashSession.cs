using SistemadeVentasSupermercado.Web.Data.Abstractions;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace SistemadeVentasSupermercado.Web.Data.Entities
{
    public enum CashSessionStatus
    {
        [Display(Name = "Abierta")] Open = 1,
        [Display(Name = "Cerrada")] Closed = 2
    }

    /// <summary>
    /// Turno de caja de un cajero: se abre con un monto inicial en efectivo y se cierra con el arqueo.
    /// Un cajero solo puede tener un turno abierto (índice único filtrado en DataContext).
    /// </summary>
    public class CashSession : IId
    {
        [Key]
        public Guid Id { get; set; }

        [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
        [Display(Name = "Número")]
        public int Number { get; set; }

        // ---------- Apertura ----------

        [Required]
        public string UserId { get; set; } = string.Empty;
        public User? User { get; set; }

        [Display(Name = "Apertura")]
        public DateTime OpenedAtUtc { get; set; } = DateTime.UtcNow;

        [Display(Name = "Monto inicial")]
        public decimal OpeningAmount { get; set; }

        [Display(Name = "Estado")]
        public CashSessionStatus Status { get; set; } = CashSessionStatus.Open;

        // ---------- Cierre (arqueo) ----------

        [Display(Name = "Cierre")]
        public DateTime? ClosedAtUtc { get; set; }

        /// <summary>Quién cerró: el mismo cajero o un supervisor si el cajero lo olvidó.</summary>
        public string? ClosedByUserId { get; set; }
        public User? ClosedByUser { get; set; }

        /// <summary>Efectivo que se contó físicamente en el cajón.</summary>
        [Display(Name = "Efectivo contado")]
        public decimal? CountedAmount { get; set; }

        /// <summary>Efectivo que debería haber según el sistema (se guarda al cerrar).</summary>
        [Display(Name = "Efectivo esperado")]
        public decimal? ExpectedAmount { get; set; }

        /// <summary>Contado − esperado: positivo si sobra, negativo si falta.</summary>
        [Display(Name = "Diferencia")]
        public decimal? Difference { get; set; }

        [StringLength(500)]
        [Display(Name = "Observaciones")]
        public string? Notes { get; set; }

        public ICollection<Sale> Sales { get; set; } = new List<Sale>();
    }
}
