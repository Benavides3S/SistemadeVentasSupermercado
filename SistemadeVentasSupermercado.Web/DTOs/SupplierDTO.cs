using System.ComponentModel.DataAnnotations;

namespace SistemadeVentasSupermercado.Web.DTOs
{
    public class SupplierDTO
    {
        public Guid Id { get; set; }

        [Display(Name = "Nombre")]
        [Required(ErrorMessage = "El campo {0} es obligatorio")]
        [StringLength(100, ErrorMessage = "El campo {0} admite máximo {1} caracteres")]
        public string Name { get; set; } = string.Empty;

        [Display(Name = "NIT / Documento")]
        [StringLength(30, ErrorMessage = "El campo {0} admite máximo {1} caracteres")]
        public string? TaxId { get; set; }

        [Display(Name = "Teléfono")]
        [StringLength(20, ErrorMessage = "El campo {0} admite máximo {1} caracteres")]
        public string? Phone { get; set; }

        [Display(Name = "Correo")]
        [StringLength(100, ErrorMessage = "El campo {0} admite máximo {1} caracteres")]
        [EmailAddress(ErrorMessage = "El campo {0} no es un correo válido")]
        public string? Email { get; set; }

        [Display(Name = "Dirección")]
        [StringLength(200, ErrorMessage = "El campo {0} admite máximo {1} caracteres")]
        public string? Address { get; set; }

        [Display(Name = "Notas")]
        [StringLength(500, ErrorMessage = "El campo {0} admite máximo {1} caracteres")]
        public string? Notes { get; set; }

        [Display(Name = "Activo")]
        public bool IsActive { get; set; } = true;
    }
}
