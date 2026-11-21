using System.ComponentModel.DataAnnotations;

namespace SistemadeVentasSupermercado.Web.Data.Entities
{
    public enum UnitOfMeasure
    {
        [Display(Name = "Unidad")] Unit = 0,
        [Display(Name = "Kilogramo")] Kilogram = 1,
        [Display(Name = "Libra")] Pound = 2,
        [Display(Name = "Litro")] Liter = 3
    }

    public static class UnitOfMeasureExtensions
    {
        public static string Abbreviation(this UnitOfMeasure unit) => unit switch
        {
            UnitOfMeasure.Kilogram => "kg",
            UnitOfMeasure.Pound => "lb",
            UnitOfMeasure.Liter => "L",
            _ => "und"
        };
    }
}