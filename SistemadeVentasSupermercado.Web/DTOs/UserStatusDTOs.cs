namespace SistemadeVentasSupermercado.Web.DTOs
{
    /// <summary>Estado de un usuario en la lista. "Desactivado" manda sobre "Bloqueado".</summary>
    public enum UserStatus
    {
        Active,
        /// <summary>Bloqueado un rato por varios intentos fallidos.</summary>
        Locked,
        Deactivated
    }

    /// <summary>Resultado de desactivar: si dejó la caja abierta, hay que avisarlo.</summary>
    public class UserDeactivationDTO
    {
        public string FullName { get; set; } = string.Empty;
        public int? OpenCashSessionNumber { get; set; }
        public DateTime? OpenCashSessionOpenedAtUtc { get; set; }
    }
}
