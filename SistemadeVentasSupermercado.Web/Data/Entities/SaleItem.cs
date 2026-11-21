using SistemadeVentasSupermercado.Web.Data.Abstractions;
using System.ComponentModel.DataAnnotations;

namespace SistemadeVentasSupermercado.Web.Data.Entities
{
    public class SaleItem : IId
    {
        [Key]
        public Guid Id { get; set; }

        public Guid SaleId { get; set; }
        public Sale? Sale { get; set; }

        public Guid ProductId { get; set; }
        public Product? Product { get; set; }

        // Copia del nombre y precio al momento de la venta:
        // si luego cambias el producto, la venta histórica no se altera.
        [Required]
        [StringLength(100)]
        public string ProductName { get; set; } = string.Empty;

        public decimal UnitPrice { get; set; }

        public decimal Quantity { get; set; }

        public UnitOfMeasure Unit { get; set; }

        public decimal Total { get; set; }
    }
}
