using System.Security.Cryptography;

namespace SistemadeVentasSupermercado.Web.Core
{
    /// <summary>
    /// Genera contraseñas temporales que cumplen la política de Identity (ver CustomConfiguration.ConfigureIdentityOptions):
    /// mínimo 8 caracteres, mayúscula, minúscula, dígito y 4 caracteres distintos.
    /// </summary>
    public static class PasswordGenerator
    {
        // Sin caracteres que se confunden al leerlos en voz alta o en papel: I, O, l, 0 y 1.
        private const string Upper = "ABCDEFGHJKLMNPQRSTUVWXYZ";
        private const string Lower = "abcdefghijkmnopqrstuvwxyz";
        private const string Digits = "23456789";
        private const string All = Upper + Lower + Digits;

        public const int DefaultLength = 10;

        /// <summary>
        /// Usa RandomNumberGenerator (criptográficamente seguro), no Random: una contraseña
        /// hecha con Random se podría adivinar.
        /// </summary>
        public static string Generate(int length = DefaultLength)
        {
            if (length < 8)
                throw new ArgumentOutOfRangeException(nameof(length), "La contraseña debe tener al menos 8 caracteres.");

            while (true)
            {
                char[] chars = new char[length];

                // Garantiza al menos una mayúscula, una minúscula y un dígito...
                chars[0] = Pick(Upper);
                chars[1] = Pick(Lower);
                chars[2] = Pick(Digits);
                for (int i = 3; i < length; i++)
                    chars[i] = Pick(All);

                // ...y los revuelve para que no queden siempre en las mismas posiciones (Fisher–Yates).
                for (int i = length - 1; i > 0; i--)
                {
                    int j = RandomNumberGenerator.GetInt32(i + 1);
                    (chars[i], chars[j]) = (chars[j], chars[i]);
                }

                // Casi imposible con 10 caracteres, pero la política exige 4 distintos.
                if (chars.Distinct().Count() >= 4)
                    return new string(chars);
            }
        }

        private static char Pick(string alphabet) => alphabet[RandomNumberGenerator.GetInt32(alphabet.Length)];
    }
}
