using System.ComponentModel.DataAnnotations;

namespace SistemadeVentasSupermercado.Web.Data.Entities
{
    /// <summary>
    /// Foto de perfil de un usuario, siempre en JPEG. Va en una tabla aparte de User para no cargar
    /// la imagen cada vez que se consulta el usuario.
    /// </summary>
    public class UserPhoto
    {
        /// <summary>Tamaño máximo de la imagen guardada (200 KB).</summary>
        public const int MaxBytes = 200 * 1024;

        /// <summary>Una foto por usuario: es la clave de la tabla.</summary>
        [Key]
        public string UserId { get; set; } = string.Empty;
        public User? User { get; set; }

        [Required]
        public byte[] Content { get; set; } = [];

        public DateTime UpdatedAtUtc { get; set; }
    }
}
