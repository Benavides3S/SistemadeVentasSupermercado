using SistemadeVentasSupermercado.Web.Data.Abstractions;
using System.ComponentModel.DataAnnotations;

namespace SistemadeVentasSupermercado.Web.Data.Entities
{
    public class Category : IId
    {
        [Key]
        public Guid Id { get; set; }

        [Required(ErrorMessage = "El campo {0} es requerido")]
        [StringLength(60)]
        [Display(Name = "Nombre")]
        public string Name { get; set; } = string.Empty;

        [Display(Name = "Activa")]
        public bool IsActive { get; set; } = true;

        public ICollection<Product> Products { get; set; } = new List<Product>();
    }
}
