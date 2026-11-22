using System.Globalization;

namespace SistemadeVentasSupermercado.Web.Core
{
    /// <summary>
    /// Formatos para MOSTRAR datos al usuario en Colombia (dinero, cantidades, hora local).
    /// No usar para formularios: allí la cultura es InvariantCulture (decimales con punto).
    /// </summary>
    public static class DisplayFormat
    {
        private static readonly CultureInfo Colombia = CultureInfo.GetCultureInfo("es-CO");

        // Colombia no tiene horario de verano: si el sistema no conoce la zona, UTC-5 fijo es correcto.
        private static readonly TimeZoneInfo ColombiaTimeZone = FindColombiaTimeZone();

        private static TimeZoneInfo FindColombiaTimeZone()
        {
            foreach (string id in new[] { "America/Bogota", "SA Pacific Standard Time" })
            {
                try { return TimeZoneInfo.FindSystemTimeZoneById(id); }
                catch (TimeZoneNotFoundException) { }
                catch (InvalidTimeZoneException) { }
            }
            return TimeZoneInfo.CreateCustomTimeZone("Colombia", TimeSpan.FromHours(-5), "Colombia", "Colombia");
        }

        /// <summary>$ 12.500 o $ 12.500,50 si tiene centavos.</summary>
        public static string Money(decimal value)
            => value.ToString(value % 1 == 0 ? "C0" : "C2", Colombia);

        /// <summary>1, 1,25 o 0,375 (hasta 3 decimales, sin ceros de sobra).</summary>
        public static string Quantity(decimal value)
            => value.ToString("0.###", Colombia);

        public static DateTime ToColombiaTime(DateTime utc)
            => TimeZoneInfo.ConvertTimeFromUtc(DateTime.SpecifyKind(utc, DateTimeKind.Utc), ColombiaTimeZone);

        /// <summary>21/11/2026 3:45 p. m. (hora de Colombia).</summary>
        public static string DateTimeColombia(DateTime utc)
            => ToColombiaTime(utc).ToString("dd/MM/yyyy h:mm tt", Colombia);
    }
}
