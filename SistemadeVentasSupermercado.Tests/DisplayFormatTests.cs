using SistemadeVentasSupermercado.Web.Core;

namespace SistemadeVentasSupermercado.Tests
{
    /// <summary>Formatos colombianos que se muestran en el historial y el comprobante (sin base de datos).</summary>
    public class DisplayFormatTests
    {
        [Fact]
        public void Dinero_usa_punto_de_miles_y_omite_centavos_si_no_hay()
        {
            string texto = DisplayFormat.Money(12500m);

            Assert.Contains("$", texto);
            Assert.Contains("12.500", texto);
            Assert.DoesNotContain(",", texto);
        }

        [Fact]
        public void Dinero_con_centavos_usa_coma_decimal()
        {
            Assert.Contains("15.432,09", DisplayFormat.Money(15432.09m));
        }

        [Theory]
        [InlineData("1", "1")]
        [InlineData("1.25", "1,25")]
        [InlineData("0.375", "0,375")]
        public void Cantidad_muestra_hasta_tres_decimales_con_coma(string valor, string esperado)
        {
            decimal cantidad = decimal.Parse(valor, System.Globalization.CultureInfo.InvariantCulture);
            Assert.Equal(esperado, DisplayFormat.Quantity(cantidad));
        }

        [Fact]
        public void La_hora_se_convierte_a_hora_de_Colombia()
        {
            DateTime utc = new DateTime(2026, 11, 21, 20, 30, 0, DateTimeKind.Utc);

            DateTime colombia = DisplayFormat.ToColombiaTime(utc);

            Assert.Equal(new DateTime(2026, 11, 21, 15, 30, 0), colombia); // UTC-5
        }
    }
}
