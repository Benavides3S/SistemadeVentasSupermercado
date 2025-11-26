using System.ComponentModel.DataAnnotations;

namespace SistemadeVentasSupermercado.Web.DTOs
{
    public class PaymentMethodDTO
    {
        public Guid Id { get; set; }

        [Display(Name = "Nombre del Método de Pago")]
        public string Name { get; set; } = String.Empty;

        [Display(Name = "Descripción")]
        public string? Description { get; set; }

        [Display(Name = "Activo")]
        public bool IsActive { get; set; } = true;

        [Display(Name = "Comisión (%)")]
        public decimal CommissionPercentage { get; set; }

        [Display(Name = "Requiere terminal")]
        public bool RequiresTerminal { get; set; }
    }

}
