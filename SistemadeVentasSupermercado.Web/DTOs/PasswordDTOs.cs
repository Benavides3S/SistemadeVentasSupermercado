using System.ComponentModel.DataAnnotations;

namespace SistemadeVentasSupermercado.Web.DTOs
{
    /// <summary>Formulario "Cambiar contraseña" del propio usuario.</summary>
    public class ChangePasswordDTO
    {
        [Display(Name = "Contraseña actual")]
        [Required(ErrorMessage = "El campo {0} es requerido.")]
        [DataType(DataType.Password)]
        public string CurrentPassword { get; set; } = string.Empty;

        [Display(Name = "Contraseña nueva")]
        [Required(ErrorMessage = "El campo {0} es requerido.")]
        [DataType(DataType.Password)]
        public string NewPassword { get; set; } = string.Empty;

        [Display(Name = "Confirmar contraseña nueva")]
        [Required(ErrorMessage = "El campo {0} es requerido.")]
        [DataType(DataType.Password)]
        [Compare(nameof(NewPassword), ErrorMessage = "Las contraseñas nuevas no coinciden.")]
        public string ConfirmPassword { get; set; } = string.Empty;
    }

    /// <summary>
    /// Contraseña temporal de un usuario recién creado o restablecido. Se muestra UNA sola vez
    /// al administrador y no se guarda en ningún lado (en la base solo queda su hash).
    /// </summary>
    public class TemporaryPasswordDTO
    {
        public string UserId { get; set; } = string.Empty;
        public string FullName { get; set; } = string.Empty;
        public string Email { get; set; } = string.Empty;
        public string TemporaryPassword { get; set; } = string.Empty;

        /// <summary>Verdadero si se creó el usuario; falso si se restableció la contraseña.</summary>
        public bool IsNewUser { get; set; }
    }
}
