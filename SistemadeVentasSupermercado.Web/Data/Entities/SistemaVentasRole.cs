using SistemadeVentasSupermercado.Web.Data.Abstractions;
using System.ComponentModel.DataAnnotations;

namespace SistemadeVentasSupermercado.Web.Data.Entities
{
    public class SistemaVentasRole : IId
    {
        [Key]
        public Guid Id { get; set; }

        [MaxLength(32)]
        [Required]
        public required string Name { get; set; }

        public ICollection<RolePermission>? RolePermissions { get; set; }
    }
}
