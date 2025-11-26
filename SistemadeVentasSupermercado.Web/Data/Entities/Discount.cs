using SistemadeVentasSupermercado.Web.Data.Abstractions;
using System.ComponentModel.DataAnnotations;

namespace SistemadeVentasSupermercado.Web.Data.Entities
{
    public class Discount : IId
    {
        [Key]
        public Guid Id { get; set; }

        [Required(ErrorMessage = "El campo {0} es requerido")]
        [StringLength(100)]
        [Display(Name = "Nombre del Descuento")]
        public string Name { get; set; } = String.Empty;

        [Display(Name = "Descripción")]
        public string? Description { get; set; }

        [Required(ErrorMessage = "El campo {0} es requerido")]
        [Display(Name = "Tipo de Descuento")]
        public DiscountType Type { get; set; }

        [Required(ErrorMessage = "El campo {0} es requerido")]
        [Display(Name = "Valor")]
        public decimal Value { get; set; }

        [Required(ErrorMessage = "El campo {0} es requerido")]
        [Display(Name = "Activo")]
        public bool IsActive { get; set; } = true;

        [Display(Name = "Fecha de Inicio")]
        public DateTime? StartDate { get; set; }

        [Display(Name = "Fecha de Fin")]
        public DateTime? EndDate { get; set; }

        [Display(Name = "Mínimo de Compra")]
        public decimal? MinimumPurchaseAmount { get; set; }

        [Display(Name = "Aplicable a todos los productos")]
        public bool ApplyToAllProducts { get; set; } = true;
    }

    public enum DiscountType
    {
        Percentage = 1,  // Porcentaje
        FixedAmount = 2  // Monto fijo
    }
}
