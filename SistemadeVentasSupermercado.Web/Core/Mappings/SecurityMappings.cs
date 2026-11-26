using SistemadeVentasSupermercado.Web.Data.Entities;
using SistemadeVentasSupermercado.Web.DTOs;

namespace SistemadeVentasSupermercado.Web.Core.Mappings
{
    /// <summary>
    /// Copia de datos entre entidades y DTOs de usuarios, roles y permisos, escrita a mano.
    /// Ver la explicación de ToDTO / ToEntity en <see cref="CatalogMappings"/>.
    /// </summary>
    public static class SecurityMappings
    {
        // ---------- Usuarios ----------

        public static UserDTO ToUserDTO(this User user) => new UserDTO
        {
            Id = Guid.TryParse(user.Id, out Guid id) ? id : null,
            Document = user.Document,
            FirstName = user.FirstName,
            LastName = user.LastName,
            PhoneNumber = user.PhoneNumber ?? string.Empty,
            Email = user.Email ?? string.Empty,
            SistemaVentasRoleId = user.SistemaVentasRoleId.ToString(),
            SistemaVentasRole = user.SistemaVentasRole,
            RoleName = user.SistemaVentasRole?.Name,
            Status = user.DeactivatedAt is not null ? UserStatus.Deactivated
                   : user.LockoutEnd > DateTimeOffset.UtcNow ? UserStatus.Locked
                   : UserStatus.Active
        };

        /// <summary>Usuario nuevo. El Id, la confirmación del correo y la contraseña los pone UsersService.</summary>
        public static User ToEntity(this UserDTO dto) => new User
        {
            Document = dto.Document,
            FirstName = dto.FirstName,
            LastName = dto.LastName,
            PhoneNumber = dto.PhoneNumber,
            Email = dto.Email,
            UserName = dto.Email,
            SistemaVentasRoleId = Guid.TryParse(dto.SistemaVentasRoleId, out Guid roleId) ? roleId : Guid.Empty
        };

        /// <summary>Datos de "Mi cuenta".</summary>
        public static AccountUserDTO ToAccountUserDTO(this User user) => new AccountUserDTO
        {
            Document = user.Document,
            FirstName = user.FirstName,
            LastName = user.LastName,
            PhoneNumber = user.PhoneNumber ?? string.Empty,
            Email = user.Email
        };

        // ---------- Roles y permisos ----------

        /// <summary>Los permisos del rol los arma RolesService aparte.</summary>
        public static SistemaVentaRoleDTO ToDTO(this SistemaVentasRole role) => new SistemaVentaRoleDTO
        {
            Id = role.Id,
            Name = role.Name
        };

        public static SistemaVentasRole ToEntity(this SistemaVentaRoleDTO dto) => new SistemaVentasRole
        {
            Id = dto.Id,
            Name = dto.Name
        };

        public static PermissionDTO ToDTO(this Permission permission) => new PermissionDTO
        {
            Id = permission.Id,
            Name = permission.Name,
            Description = permission.Description,
            Module = permission.Module
        };
    }
}
