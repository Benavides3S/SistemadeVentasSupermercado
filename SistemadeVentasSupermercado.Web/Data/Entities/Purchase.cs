using SistemadeVentasSupermercado.Web.Data.Abstractions;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace SistemadeVentasSupermercado.Web.Data.Entities
{
    public enum PurchaseStatus
    {
        [Display(Name = "Completada")] Completed = 1,
        [Display(Name = "Anulada")] Cancelled = 2
    }

    public static class PurchaseEnumExtensions
    {
        public static string Label(this PurchaseStatus status) => status switch
        {
            PurchaseStatus.Completed => "Completada",
            PurchaseStatus.Cancelled => "Anulada",
            _ => status.ToString()
        };
    }

    /// <summary>
    /// Compra de mercancía a un proveedor. Al registrarla, el stock de cada producto sube y su
    /// último costo (<see cref="Product.Cost"/>) se actualiza. Una compra anulada devuelve el stock
    /// (solo si todavía alcanza) pero no restaura el costo anterior.
    /// </summary>
    public class Purchase : IId
    {
        [Key]
        public Guid Id { get; set; }

        [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
        [Display(Name = "Número")]
        public int Number { get; set; }

        [Display(Name = "Fecha")]
        public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;

        /// <summary>Quién registró la compra.</summary>
        [Required]
        public string UserId { get; set; } = string.Empty;
        public User? User { get; set; }

        [Display(Name = "Proveedor")]
        public Guid SupplierId { get; set; }
        public Supplier? Supplier { get; set; }

        /// <summary>Número de la factura o remisión del proveedor (opcional).</summary>
        [StringLength(50)]
        [Display(Name = "Factura del proveedor")]
        public string? SupplierInvoiceNumber { get; set; }

        [StringLength(200)]
        [Display(Name = "Notas")]
        public string? Notes { get; set; }

        [Display(Name = "Estado")]
        public PurchaseStatus Status { get; set; } = PurchaseStatus.Completed;

        [Display(Name = "Total")]
        public decimal Total { get; set; }

        public DateTime? CancelledAtUtc { get; set; }

        [StringLength(200)]
        public string? CancelReason { get; set; }

        /// <summary>Quién anuló la compra (vacío si no está anulada).</summary>
        public string? CancelledByUserId { get; set; }
        public User? CancelledByUser { get; set; }

        /// <summary>
        /// Código único que la pantalla de compras genera para cada registro. Si la red se corta y se
        /// vuelve a enviar con el mismo código, se devuelve la compra ya registrada en vez de sumar
        /// el stock otra vez.
        /// </summary>
        public Guid? IdempotencyKey { get; set; }

        public ICollection<PurchaseItem> Items { get; set; } = new List<PurchaseItem>();
    }
}
