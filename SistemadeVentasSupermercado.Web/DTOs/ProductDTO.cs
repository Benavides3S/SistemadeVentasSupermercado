using System.ComponentModel.DataAnnotations;

namespace SistemadeVentasSupermercado.Web.DTOs
{
    public class ProductDTO
    {

       
        public Guid Id { get; set; }

        [Display(Name = "Nombre")]
        public string Name { get; set; } = String.Empty;

        [Display(Name = "Descripcion")]
        public string? Description { get; set; }

        [Display(Name = "precio")]
        public decimal Price { get; set; }   // ✅ Ahora decimal

        [Display(Name = "Stock")]
        public int Stock { get; set; }

        [Display(Name = "Categoria")]
        public string Category { get; set; } = String.Empty; // ✅ Ahora string
    }
}
