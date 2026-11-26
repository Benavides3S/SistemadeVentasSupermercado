using SistemadeVentasSupermercado.Web.Data.Entities;

namespace SistemadeVentasSupermercado.Web.Services.Abstractions
{
    /// <summary>Escribe en el historial de actividad de usuarios. NUNCA recibe ni guarda contraseñas.</summary>
    public interface IUserActivityRecorder
    {
        /// <summary>
        /// Para cambios de usuarios, contraseñas y roles: deja el registro pendiente en el contexto, SIN guardar.
        /// Quien llama guarda en la misma transacción que el cambio, así quedan los dos o ninguno.
        /// </summary>
        Task StageAsync(UserActivityType type, string? actorUserName, User? target = null,
            string? targetName = null, string? details = null);

        /// <summary>Ingreso o salida. Si falla el registro no se interrumpe al usuario (queda en el log técnico).</summary>
        Task RecordAccessAsync(UserActivityType type, string userName);

        /// <summary>
        /// Intento de entrar que no tuvo éxito. Se agrupa: una fila por tipo, correo, IP y minuto, con un
        /// contador. target es el usuario si el correo existe; si no, typedEmail es lo que se escribió y solo
        /// se guarda cuando tiene forma de correo. No lanza excepciones.
        /// </summary>
        Task RecordFailedLoginAsync(UserActivityType type, User? target, string? typedEmail);

        /// <summary>
        /// PIN de autorización rechazado. Guarda quién estaba en la sesión y de quién era el PIN (si puede
        /// autorizar). Se agrupa: una fila por esas dos personas, IP y minuto. No lanza excepciones.
        /// NUNCA recibe el PIN.
        /// </summary>
        Task RecordFailedAuthorizationAsync(User? authorizer, string requesterUserName);
    }
}
