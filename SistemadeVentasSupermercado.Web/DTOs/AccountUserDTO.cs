using System.ComponentModel.DataAnnotations;

namespace SistemadeVentasSupermercado.Web.DTOs
{
    public class AccountUserDTO
    {
        public string? Id { get; set; }

        [Display(Name = "Documento")]
        [MaxLength(32)]
        [Required(ErrorMessage = "El campo {0} es requerido.")]
        public string Document { get; set; }

        [Display(Name = "Nombres")]
        [MaxLength(64)]
        [Required(ErrorMessage = "El campo {0} es requerido.")]
        public string FirstName { get; set; }

        [Display(Name = "Apellidos")]
        [MaxLength(64)]
        [Required(ErrorMessage = "El campo {0} es requerido.")]
        public string LastName { get; set; }

        [Display(Name = "Teléfono")]
        [MaxLength(32)]
        [Required(ErrorMessage = "El campo {0} es requerido.")]
        public string PhoneNumber { get; set; }

        [Display(Name = "Correo")]
        public string? Email { get; set; }

        public string FullName => $"{FirstName} {LastName}";
    }
}
