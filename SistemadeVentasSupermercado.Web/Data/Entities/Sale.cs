using SistemadeVentasSupermercado.Web.Data.Abstractions;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace SistemadeVentasSupermercado.Web.Data.Entities
{
    public enum PaymentMethod
    {
        Cash = 1,
        Card = 2,
        Transfer = 3
    }

    public enum SaleStatus
    {
        Completed = 1,
        Cancelled = 2
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

        [Display(Name = "Método de pago")]
        public PaymentMethod PaymentMethod { get; set; }

        [Display(Name = "Estado")]
        public SaleStatus Status { get; set; } = SaleStatus.Completed;

        [Display(Name = "Total")]
        public decimal Total { get; set; }

        public DateTime? CancelledAtUtc { get; set; }

        [StringLength(200)]
        public string? CancelReason { get; set; }

        public ICollection<SaleItem> Items { get; set; } = new List<SaleItem>();
    }
}