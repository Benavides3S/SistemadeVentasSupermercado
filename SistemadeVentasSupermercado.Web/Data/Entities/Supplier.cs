using SistemadeVentasSupermercado.Web.Data.Abstractions;
using System.ComponentModel.DataAnnotations;

namespace SistemadeVentasSupermercado.Web.Data.Entities
{
    /// <summary>
    /// Proveedor al que se le compra mercancía. El nombre es único; el NIT/documento es opcional
    /// y también único cuando existe. Un proveedor con compras no se borra: se desactiva.
    /// </summary>
    public class Supplier : IId
    {
        [Key]
        public Guid Id { get; set; }

        [Required(ErrorMessage = "El campo {0} es requerido")]
        [StringLength(100)]
        [Display(Name = "Nombre")]
        public string Name { get; set; } = string.Empty;

        [StringLength(30)]
        [Display(Name = "NIT / Documento")]
        public string? TaxId { get; set; }

        [StringLength(20)]
        [Display(Name = "Teléfono")]
        public string? Phone { get; set; }

        [StringLength(100)]
        [Display(Name = "Correo")]
        public string? Email { get; set; }

        [StringLength(200)]
        [Display(Name = "Dirección")]
        public string? Address { get; set; }

        [StringLength(500)]
        [Display(Name = "Notas")]
        public string? Notes { get; set; }

        [Display(Name = "Activo")]
        public bool IsActive { get; set; } = true;
    }
}
