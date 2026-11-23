using System.Text.RegularExpressions;

namespace SistemadeVentasSupermercado.Web.Core
{
    /// <summary>
    /// Normaliza y valida códigos de barras (EAN/UPC) o códigos internos del negocio.
    /// No se revisa el dígito verificador: el lector ya lo valida al escanear, y así sirven también los códigos internos.
    /// </summary>
    public static partial class BarcodeHelper
    {
        public const int MaxLength = 50;

        /// <summary>Quita todos los espacios. Vacío o solo espacios = sin código (null).</summary>
        public static string? Normalize(string? code)
        {
            if (string.IsNullOrWhiteSpace(code)) return null;
            string normalized = WhitespaceRegex().Replace(code, "");
            return normalized.Length == 0 ? null : normalized;
        }

        /// <summary>Devuelve un mensaje de error, o null si el código (ya normalizado) es válido.</summary>
        public static string? Validate(string? normalizedCode)
        {
            if (normalizedCode is null) return null;
            if (normalizedCode.Length > MaxLength)
                return $"El código de barras admite máximo {MaxLength} caracteres.";
            if (!AllowedRegex().IsMatch(normalizedCode))
                return "El código de barras solo puede tener letras, números y guiones (-).";
            return null;
        }

        [GeneratedRegex(@"\s+")]
        private static partial Regex WhitespaceRegex();

        [GeneratedRegex(@"^[A-Za-z0-9-]+$")]
        private static partial Regex AllowedRegex();
    }
}
