using SistemadeVentasSupermercado.Web.Data.Abstractions;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace SistemadeVentasSupermercado.Web.Data.Entities
{
    public enum PaymentMethod
    {
        [Display(Name = "Efectivo")] Cash = 1,
        [Display(Name = "Tarjeta")] Card = 2,
        [Display(Name = "Transferencia")] Transfer = 3
    }

    public enum SaleStatus
    {
        [Display(Name = "Completada")] Completed = 1,
        [Display(Name = "Anulada")] Cancelled = 2
    }

    public static class SaleEnumExtensions
    {
        public static string Label(this PaymentMethod method) => method switch
        {
            PaymentMethod.Cash => "Efectivo",
            PaymentMethod.Card => "Tarjeta",
            PaymentMethod.Transfer => "Transferencia",
            _ => method.ToString()
        };

        public static string Label(this SaleStatus status) => status switch
        {
            SaleStatus.Completed => "Completada",
            SaleStatus.Cancelled => "Anulada",
            _ => status.ToString()
        };
    }

    public class Sale : IId
    {
        [Key]
        public Guid Id { get; set; }

        [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
        [Display(Name = "Número")]
        public int Number { get; set; }

        [Display(Name = "Fecha")]
        public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;

        [Required]
        public string UserId { get; set; } = string.Empty;
        public User? User { get; set; }

        public Guid? ClientId { get; set; }
        public Client? Client { get; set; }

        /// <summary>Turno de caja en que se hizo la venta (vacío en ventas anteriores a la caja).</summary>
        public Guid? CashSessionId { get; set; }
        public CashSession? CashSession { get; set; }

        [Display(Name = "Método de pago")]
        public PaymentMethod PaymentMethod { get; set; }

        [Display(Name = "Estado")]
        public SaleStatus Status { get; set; } = SaleStatus.Completed;

        [Display(Name = "Total")]
        public decimal Total { get; set; }

        public DateTime? CancelledAtUtc { get; set; }

        [StringLength(200)]
        public string? CancelReason { get; set; }

        /// <summary>Quién anuló la venta (vacío si no está anulada).</summary>
        public string? CancelledByUserId { get; set; }
        public User? CancelledByUser { get; set; }

        /// <summary>Suma de todo lo devuelto de esta venta (0 si no hay devoluciones).</summary>
        [Display(Name = "Devuelto")]
        public decimal ReturnedTotal { get; set; }

        public ICollection<SaleItem> Items { get; set; } = new List<SaleItem>();

        public ICollection<SaleReturn> Returns { get; set; } = new List<SaleReturn>();
    }
}