using System.ComponentModel.DataAnnotations;

namespace SistemadeVentasSupermercado.Web.DTOs
{
    public class CategoryDTO
    {
        public Guid Id { get; set; }

        [Display(Name = "Nombre")]
        [Required(ErrorMessage = "El campo {0} es obligatorio")]
        [StringLength(60, ErrorMessage = "El campo {0} admite máximo {1} caracteres")]
        public string Name { get; set; } = string.Empty;

        [Display(Name = "Activa")]
        public bool IsActive { get; set; } = true;
    }
}