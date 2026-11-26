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

        /// <summary>
        /// Verdadero si el usuario tiene una contraseña temporal (recién creado o restablecido por un
        /// administrador): mientras lo sea, toda página lo lleva a "Cambiar contraseña".
        /// </summary>
        public bool MustChangePassword { get; set; }

        /// <summary>
        /// Fecha (UTC) en que se desactivó el usuario; null = activo. Un usuario desactivado no puede
        /// entrar, pero no se borra: se conservan sus ventas, compras y turnos de caja.
        /// </summary>
        public DateTime? DeactivatedAt { get; set; }

        public string FullName => $"{FirstName} {LastName}";
    }
}

