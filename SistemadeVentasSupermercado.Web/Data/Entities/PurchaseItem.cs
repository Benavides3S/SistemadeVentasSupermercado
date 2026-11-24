using SistemadeVentasSupermercado.Web.Data.Abstractions;
using System.ComponentModel.DataAnnotations;

namespace SistemadeVentasSupermercado.Web.Data.Entities
{
    public class PurchaseItem : IId
    {
        [Key]
        public Guid Id { get; set; }

        public Guid PurchaseId { get; set; }
        public Purchase? Purchase { get; set; }

        public Guid ProductId { get; set; }
        public Product? Product { get; set; }

        // Copia del nombre y la unidad al momento de la compra:
        // si luego cambias el producto, la compra histórica no se altera.
        [Required]
        [StringLength(100)]
        public string ProductName { get; set; } = string.Empty;

        public UnitOfMeasure Unit { get; set; }

        /// <summary>Costo por unidad (o por kilo, libra, litro) que pagó el negocio en esta compra.</summary>
        public decimal UnitCost { get; set; }

        public decimal Quantity { get; set; }

        public decimal Total { get; set; }
    }
}
