namespace SistemadeVentasSupermercado.Web.Core
{
    /// <summary>
    /// Días del calendario colombiano para consultas. Las fechas se guardan en UTC;
    /// Colombia es UTC−5 todo el año (no tiene horario de verano), así que basta con
    /// sumar o restar 5 horas, y esa resta se puede hacer dentro de la consulta SQL.
    /// </summary>
    public static class ColombiaCalendar
    {
        public const int UtcOffsetHours = -5;

        /// <summary>Instante UTC en que empieza (00:00 en Colombia) el día indicado.</summary>
        public static DateTime StartOfDayUtc(DateOnly day)
            => DateTime.SpecifyKind(day.ToDateTime(TimeOnly.MinValue).AddHours(-UtcOffsetHours), DateTimeKind.Utc);

        /// <summary>Fecha de hoy en Colombia.</summary>
        public static DateOnly Today()
            => DateOnly.FromDateTime(DateTime.UtcNow.AddHours(UtcOffsetHours));
    }
}
