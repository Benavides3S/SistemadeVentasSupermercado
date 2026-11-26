using SistemadeVentasSupermercado.Web.Data.Abstractions;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace SistemadeVentasSupermercado.Web.Data.Entities
{
    /// <summary>
    /// Devolución (parcial o total) de una venta. Se reembolsa al precio original
    /// y con el mismo método de pago de la venta.
    /// </summary>
    public class SaleReturn : IId
    {
        [Key]
        public Guid Id { get; set; }

        [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
        [Display(Name = "Número")]
        public int Number { get; set; }

        public Guid SaleId { get; set; }
        public Sale? Sale { get; set; }

        /// <summary>Quién hizo la devolución.</summary>
        [Required]
        public string UserId { get; set; } = string.Empty;
        public User? User { get; set; }

        /// <summary>
        /// Supervisor que autorizó la devolución con su PIN. Vacío si quien la hizo ya tenía el permiso.
        /// </summary>
        public string? AuthorizedByUserId { get; set; }
        public User? AuthorizedByUser { get; set; }

        /// <summary>
        /// Turno de caja de quien devuelve. Obligatorio si el reembolso es en efectivo
        /// (el dinero sale de ese cajón); vacío para tarjeta o transferencia.
        /// </summary>
        public Guid? CashSessionId { get; set; }
        public CashSession? CashSession { get; set; }

        [Display(Name = "Fecha")]
        public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;

        [Required]
        [StringLength(200)]
        [Display(Name = "Motivo")]
        public string Reason { get; set; } = string.Empty;

        [Display(Name = "Método de reembolso")]
        public PaymentMethod RefundMethod { get; set; }

        [Display(Name = "Total devuelto")]
        public decimal Total { get; set; }

        public ICollection<SaleReturnItem> Items { get; set; } = new List<SaleReturnItem>();
    }

    /// <summary>Una línea devuelta: copia nombre, precio y unidad como SaleItem.</summary>
    public class SaleReturnItem : IId
    {
        [Key]
        public Guid Id { get; set; }

        public Guid SaleReturnId { get; set; }
        public SaleReturn? SaleReturn { get; set; }

        /// <summary>Línea de la venta original que se devuelve.</summary>
        public Guid SaleItemId { get; set; }
        public SaleItem? SaleItem { get; set; }

        public Guid ProductId { get; set; }
        public Product? Product { get; set; }

        [Required]
        [StringLength(100)]
        public string ProductName { get; set; } = string.Empty;

        public decimal Quantity { get; set; }

        /// <summary>Precio de la venta original (no el precio actual del producto).</summary>
        public decimal UnitPrice { get; set; }

        public UnitOfMeasure Unit { get; set; }

        public decimal Total { get; set; }

        /// <summary>true si el producto volvió al inventario (false si estaba dañado o vencido).</summary>
        public bool Restocked { get; set; }
    }
}
