using Microsoft.AspNetCore.Identity;
using System.ComponentModel.DataAnnotations;

namespace SistemadeVentasSupermercado.Web.Data.Entities
{
    public class User : IdentityUser
    {
        [MaxLength(32)]
        [Required]
        public required string Document { get; set; }

        [MaxLength(64)]
        [Required]
        public required string FirstName { get; set; }

        [MaxLength(64)]
        [Required]
        public required string LastName { get; set; }

        public required Guid SistemaVentasRoleId { get; set; }

        public SistemaVentasRole? SistemaVentasRole { get; set; }

        public string? Photo { get; set; }

        public string FullName => $"{FirstName} {LastName}";
    }
}

