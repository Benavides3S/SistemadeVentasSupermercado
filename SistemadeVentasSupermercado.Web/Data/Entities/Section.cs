using CsvHelper.Configuration.Attributes;
using System.ComponentModel.DataAnnotations;

namespace SistemadeVentasSupermercado.Web.NewFolder.Entities
{
    public class Section
    {
        [Key]
        public Guid Id { get; set; }

        [Display(Name = "Seccion")]
        [Required(ErrorMessage = "El campo {0} es requerido")]
        public string Name { get; set; }

        [Display(Name = "Descripcion")]
        public string? Description { get; set; }
        public bool IsHidden { get; set; } = false;
    }
}
