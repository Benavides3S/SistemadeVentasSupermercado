using SistemadeVentasSupermercado.Web.Data.Entities;
using System.ComponentModel.DataAnnotations;

namespace SistemadeVentasSupermercado.Web.DTOs
{
    public class DiscountDTO
    {
        public Guid Id { get; set; }

        [Display(Name = "Nombre del Descuento")]
        public string Name { get; set; } = String.Empty;

        [Display(Name = "Descripción")]
        public string? Description { get; set; }

        [Display(Name = "Tipo de Descuento")]
        public DiscountType Type { get; set; }

        [Display(Name = "Valor")]
        public decimal Value { get; set; }

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
}
