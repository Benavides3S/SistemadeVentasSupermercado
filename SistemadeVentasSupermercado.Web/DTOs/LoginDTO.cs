using System.ComponentModel.DataAnnotations;

namespace SistemadeVentasSupermercado.Web.DTOs
{
    public class LoginDTO
    {
        [Required(ErrorMessage = "El campo {0} es requerido.")]
        [EmailAddress(ErrorMessage = "El campo {0} debe ser un Email válido")]
        
        [Display(Name = "Correo")]
        public required string Email { get; set; }

        [Required(ErrorMessage = "El campo {0} es requerido.")]
        [MaxLength(4, ErrorMessage = "El campo {0} debe tener por lo menos {1} caractéres")]
        [Display(Name = "Contraseña")]
        public required string Password { get; set; }
    }
}
