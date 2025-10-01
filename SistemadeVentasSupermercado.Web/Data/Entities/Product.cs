using CsvHelper.Configuration.Attributes;
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

        [Display(Name = "Descripcion")]
        public string? Description { get; set; }

        [Required(ErrorMessage = "El campo {0} es requerido")]
        [Display(Name = "Precio")]
        public decimal Price { get; set; }   // ✅ Ahora decimal

        [Required(ErrorMessage = "El campo {0} es requerido")]
        [Display(Name = "Stock")]
        public int Stock { get; set; }

        [Required(ErrorMessage = "El campo {0} es requerido")]
        [StringLength(50)]
        [Display(Name = "Categoria")]
        public string Category { get; set; } = String.Empty; // ✅ Ahora string
    }
}

