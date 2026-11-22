namespace SistemadeVentasSupermercado.Web.Core
{
    /// <summary>
    /// Datos del negocio que salen en el comprobante. Se leen de la sección "Negocio"
    /// de appsettings.json, así cada supermercado los cambia sin tocar el código.
    /// </summary>
    public class BusinessOptions
    {
        public const string SectionName = "Negocio";

        public string Nombre { get; set; } = "Supermercado";
        public string? Nit { get; set; }
        public string? Direccion { get; set; }
        public string? Telefono { get; set; }
    }
}
