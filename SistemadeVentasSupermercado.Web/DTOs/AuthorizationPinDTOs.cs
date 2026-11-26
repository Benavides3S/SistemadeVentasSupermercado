using System.ComponentModel.DataAnnotations;

namespace SistemadeVentasSupermercado.Web.DTOs
{
    /// <summary>Operaciones que un cajero puede hacer con la autorización de un supervisor.</summary>
    public enum SupervisorOperation
    {
        CancelSale,
        CreateReturn
    }

    /// <summary>Formulario "Mi PIN": crear o cambiar el PIN de autorización propio.</summary>
    public class SetAuthorizationPinDTO
    {
        [Display(Name = "Contraseña actual")]
        [Required(ErrorMessage = "El campo {0} es requerido.")]
        [DataType(DataType.Password)]
        public string CurrentPassword { get; set; } = string.Empty;

        [Display(Name = "PIN nuevo (6 dígitos)")]
        [Required(ErrorMessage = "El campo {0} es requerido.")]
        [DataType(DataType.Password)]
        public string Pin { get; set; } = string.Empty;

        [Display(Name = "Confirmar PIN")]
        [Required(ErrorMessage = "El campo {0} es requerido.")]
        [DataType(DataType.Password)]
        [Compare(nameof(Pin), ErrorMessage = "Los PIN no coinciden.")]
        public string ConfirmPin { get; set; } = string.Empty;
    }

    /// <summary>Una persona que puede autorizar (para la lista del formulario).</summary>
    public class AuthorizerDTO
    {
        public string UserId { get; set; } = string.Empty;
        public string FullName { get; set; } = string.Empty;
    }
}
