using SistemadeVentasSupermercado.Web.Data.Abstractions;
using System.ComponentModel.DataAnnotations;

namespace SistemadeVentasSupermercado.Web.Data.Entities
{
    public class PaymentMethod : IId
    {
        [Key]
        public Guid Id { get; set; }

        [Required(ErrorMessage = "El campo {0} es requerido")]
        [StringLength(100)]
        [Display(Name = "Nombre del Método de Pago")]
        public string Name { get; set; } = String.Empty;

        [Display(Name = "Descripción")]
        public string? Description { get; set; }

        [Required(ErrorMessage = "El campo {0} es requerido")]
        [Display(Name = "Activo")]
        public bool IsActive { get; set; } = true;

        [Display(Name = "Comisión (%)")]
        public decimal CommissionPercentage { get; set; }

        [Display(Name = "Requiere terminal")]
        public bool RequiresTerminal { get; set; }
    }
}
