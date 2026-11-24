using System.ComponentModel.DataAnnotations;
using SistemadeVentasSupermercado.Web.Data.Entities;

namespace SistemadeVentasSupermercado.Web.DTOs
{
    public class ProductDTO
    {
        public Guid Id { get; set; }

        [Display(Name = "Nombre")]
        [Required(ErrorMessage = "El campo {0} es obligatorio")]
        [StringLength(100, ErrorMessage = "El campo {0} admite máximo {1} caracteres")]
        public string Name { get; set; } = String.Empty;

        [Display(Name = "Código de barras")]
        [StringLength(50, ErrorMessage = "El campo {0} admite máximo {1} caracteres")]
        public string? Barcode { get; set; }

        [Display(Name = "Descripción")]
        public string? Description { get; set; }

        [Display(Name = "Precio")]
        [Range(0.01, 1000000000, ErrorMessage = "El campo {0} debe ser mayor que cero")]
        public decimal Price { get; set; }

        [Display(Name = "Stock")]
        [Range(0, 1000000000, ErrorMessage = "El campo {0} no puede ser negativo")]
        public decimal Stock { get; set; }

        /// <summary>
        /// Último costo de compra. Solo se muestra: lo cambia el módulo de compras, así que
        /// ni ToEntity ni ApplyTo lo copian (un formulario manipulado no puede fijarlo).
        /// </summary>
        [Display(Name = "Último costo")]
        public decimal? Cost { get; set; }

        [Display(Name = "Unidad de medida")]
        public UnitOfMeasure Unit { get; set; }
        [Display(Name = "Categoría")]
        [Required(ErrorMessage = "Seleccione una categoría")]
        public Guid? CategoryId { get; set; }

        public string? CategoryName { get; set; }
    }
}
