using SistemadeVentasSupermercado.Web.Data.Abstractions;
using System.ComponentModel.DataAnnotations;

namespace SistemadeVentasSupermercado.Web.Data.Entities
{
    public class Product : IId
    {
        [Key]
        public Guid Id { get; set; }

        [Required(ErrorMessage = "El campo {0} es requerido")]
        [StringLength(100)]
        [Display(Name = "Nombre")]
        public string Name { get; set; } = String.Empty;

        /// <summary>
        /// Código de barras (EAN/UPC) o código interno del negocio. Opcional (frutas y verduras por peso
        /// pueden no tenerlo) y único cuando existe. Se guarda sin espacios; vacío = sin código.
        /// </summary>
        [StringLength(50)]
        [Display(Name = "Código de barras")]
        public string? Barcode { get; set; }

        [Display(Name = "Descripcion")]
        public string? Description { get; set; }

        [Required(ErrorMessage = "El campo {0} es requerido")]
        [Display(Name = "Precio")]
        public decimal Price { get; set; }   // ✅ Ahora decimal

        [Required(ErrorMessage = "El campo {0} es requerido")]
        [Display(Name = "Stock")]
        public decimal Stock { get; set; }

        /// <summary>
        /// Último costo de compra (por unidad, kilo, libra o litro). Lo actualiza solo el módulo de
        /// compras; vacío si el producto nunca se ha comprado. El formulario de producto no lo edita.
        /// </summary>
        [Display(Name = "Último costo")]
        public decimal? Cost { get; set; }

        [Display(Name = "Unidad de medida")]
        public UnitOfMeasure Unit { get; set; } = UnitOfMeasure.Unit;

        [Required(ErrorMessage = "El campo {0} es requerido")]
        [Display(Name = "Categoría")]
        public Guid CategoryId { get; set; }

        public Category? Category { get; set; }
    }
}

