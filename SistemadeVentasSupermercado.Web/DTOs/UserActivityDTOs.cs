using SistemadeVentasSupermercado.Web.Core.Pagination;
using SistemadeVentasSupermercado.Web.Data.Entities;

namespace SistemadeVentasSupermercado.Web.DTOs
{
    /// <summary>Filtros de "Actividad de usuarios". Filter busca en el nombre o correo de quién y sobre quién.</summary>
    public class UserActivityQueryDTO : PaginationRequest
    {
        public UserActivityType? Type { get; set; }
        /// <summary>Días de Colombia, ambos incluidos.</summary>
        public DateOnly? From { get; set; }
        public DateOnly? To { get; set; }
    }

    public class UserActivityDTO
    {
        public int Number { get; set; }
        public DateTime OccurredAtUtc { get; set; }
        public UserActivityType Type { get; set; }
        public string? ActorName { get; set; }
        public string? TargetName { get; set; }
        public string? Details { get; set; }
        public string? IpAddress { get; set; }
        public int AttemptCount { get; set; }
        public DateTime? LastAttemptAtUtc { get; set; }
    }

    public static class UserActivityLabels
    {
        public static string Label(this UserActivityType type) => type switch
        {
            UserActivityType.LoginSucceeded => "Ingreso",
            UserActivityType.Logout => "Cierre de sesión",
            UserActivityType.LoginFailed => "Intento fallido de ingreso",
            UserActivityType.AccountLocked => "Intento con la cuenta bloqueada",
            UserActivityType.DeactivatedLoginRejected => "Intento de un usuario desactivado",
            UserActivityType.PasswordChanged => "Cambió su contraseña",
            UserActivityType.PasswordReset => "Restableció una contraseña",
            UserActivityType.UserCreated => "Creó un usuario",
            UserActivityType.UserEdited => "Editó un usuario",
            UserActivityType.UserRoleChanged => "Cambió el rol de un usuario",
            UserActivityType.UserDeactivated => "Desactivó un usuario",
            UserActivityType.UserReactivated => "Reactivó un usuario",
            UserActivityType.UserUnlocked => "Desbloqueó un usuario",
            UserActivityType.ProfileUpdated => "Actualizó su perfil",
            UserActivityType.RoleCreated => "Creó un rol",
            UserActivityType.RoleEdited => "Editó un rol",
            UserActivityType.RoleDeleted => "Eliminó un rol",
            _ => type.ToString()
        };
    }
}
