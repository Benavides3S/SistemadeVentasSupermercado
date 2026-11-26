using SistemadeVentasSupermercado.Web.Data.Abstractions;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace SistemadeVentasSupermercado.Web.Data.Entities
{
    /// <summary>
    /// Qué pasó. Los números se guardan en la base: no cambiarlos ni reutilizarlos.
    /// Van por grupos de diez para poder agregar eventos sin desordenarlos.
    /// </summary>
    public enum UserActivityType
    {
        // Accesos
        LoginSucceeded = 10,
        Logout = 11,
        /// <summary>Correo o contraseña incorrectos.</summary>
        LoginFailed = 12,
        /// <summary>Intento con la cuenta bloqueada (incluye el intento que la bloquea).</summary>
        AccountLocked = 13,
        /// <summary>Intento de entrar con un usuario desactivado.</summary>
        DeactivatedLoginRejected = 14,
        /// <summary>Contraseña equivocada al desbloquear la pantalla.</summary>
        UnlockFailed = 15,

        // Contraseñas
        PasswordChanged = 20,
        PasswordReset = 21,

        // Usuarios
        UserCreated = 30,
        UserEdited = 31,
        UserRoleChanged = 32,
        UserDeactivated = 33,
        UserReactivated = 34,
        UserUnlocked = 35,
        ProfileUpdated = 36,

        // Roles
        RoleCreated = 40,
        RoleEdited = 41,
        RoleDeleted = 42,

        // Autorización de supervisor
        /// <summary>Un supervisor autorizó con su PIN una operación en la caja de otro.</summary>
        SupervisorAuthorizationGranted = 50,
        /// <summary>PIN incorrecto o bloqueado, o de alguien que no puede autorizar.</summary>
        SupervisorAuthorizationFailed = 51,
        AuthorizationPinChanged = 52
    }

    /// <summary>
    /// Una línea del historial de actividad de usuarios. La aplicación solo agrega filas: no las edita ni
    /// las borra (la única excepción es sumar intentos a un registro de fallos agrupados).
    /// NUNCA se guarda una contraseña, ni las temporales ni las mal escritas.
    /// </summary>
    public class UserActivity : IId
    {
        public const int NameMaxLength = 400;
        public const int DetailsMaxLength = 4000;
        /// <summary>Alcanza para una dirección IPv6.</summary>
        public const int IpAddressMaxLength = 45;
        public const int GroupKeyMaxLength = 350;

        [Key]
        public Guid Id { get; set; }

        /// <summary>Orden en que se registraron.</summary>
        [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
        [Display(Name = "Número")]
        public int Number { get; set; }

        /// <summary>Cuándo pasó. En fallos agrupados es la hora del PRIMER intento.</summary>
        [Display(Name = "Fecha")]
        public DateTime OccurredAtUtc { get; set; } = DateTime.UtcNow;

        [Display(Name = "Evento")]
        public UserActivityType Type { get; set; }

        /// <summary>Quién lo hizo. Vacío si no había sesión (intentos de entrar fallidos).</summary>
        public string? ActorUserId { get; set; }
        public User? ActorUser { get; set; }

        /// <summary>Copia del nombre y correo en ese momento.</summary>
        [StringLength(NameMaxLength)]
        [Display(Name = "Quién")]
        public string? ActorName { get; set; }

        /// <summary>Usuario afectado, si el evento es sobre un usuario que existe.</summary>
        public string? TargetUserId { get; set; }
        public User? TargetUser { get; set; }

        /// <summary>
        /// Copia de sobre quién o qué fue: un usuario, un rol (sin FK: puede borrarse) o, en un intento con
        /// un correo que no existe, lo escrito (solo si tiene forma de correo; si no, "usuario no válido").
        /// </summary>
        [StringLength(NameMaxLength)]
        [Display(Name = "Sobre")]
        public string? TargetName { get; set; }

        [StringLength(DetailsMaxLength)]
        [Display(Name = "Detalle")]
        public string? Details { get; set; }

        /// <summary>Solo es una pista: detrás de Docker o de un intermediario puede no ser la del equipo.</summary>
        [StringLength(IpAddressMaxLength)]
        [Display(Name = "Dirección IP")]
        public string? IpAddress { get; set; }

        /// <summary>Cuántos intentos resume esta fila (1 salvo en fallos agrupados).</summary>
        [Display(Name = "Intentos")]
        public int AttemptCount { get; set; } = 1;

        /// <summary>Hora del último intento. Solo en fallos agrupados.</summary>
        [Display(Name = "Último intento")]
        public DateTime? LastAttemptAtUtc { get; set; }

        /// <summary>
        /// Solo en fallos agrupados: tipo + correo (o "usuario no válido") + IP + minuto. Es única en la base,
        /// así dos intentos simultáneos no pueden crear dos filas. Los accesos exitosos no se agrupan (null).
        /// </summary>
        [StringLength(GroupKeyMaxLength)]
        public string? GroupKey { get; set; }
    }
}
