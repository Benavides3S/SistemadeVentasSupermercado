namespace SistemadeVentasSupermercado.Web.Data.Entities
{
    public class RolePermission
    {
        public required Guid SistemaVentasRoleId { get; set; }
        public required Guid PermissionId { get; set; }
        public SistemaVentasRole SistemaVentasRole { get; set; }
        public Permission Permission { get; set; }


    }
}
