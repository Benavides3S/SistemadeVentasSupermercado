using System.ComponentModel.DataAnnotations;

namespace SistemadeVentasSupermercado.Web.Data.Entities
{
    /// <summary>
    /// PIN con el que un supervisor autoriza una operación en la caja de otro. Va en una tabla aparte
    /// de User para que un guardado de Identity con datos viejos no pise el contador de intentos.
    /// El PIN nunca se guarda: solo su hash.
    /// </summary>
    public class UserAuthorizationPin
    {
        public const int PinHashMaxLength = 200;

        /// <summary>Un PIN por usuario: es la clave de la tabla.</summary>
        [Key]
        public string UserId { get; set; } = string.Empty;
        public User? User { get; set; }

        [Required]
        [StringLength(PinHashMaxLength)]
        public string PinHash { get; set; } = string.Empty;

        /// <summary>Errores seguidos. No bloquea el inicio de sesión, solo el PIN.</summary>
        public int FailedAttempts { get; set; }

        /// <summary>Hasta cuándo (UTC) está bloqueado el PIN; null = no está bloqueado.</summary>
        public DateTime? LockedUntilUtc { get; set; }
    }
}
