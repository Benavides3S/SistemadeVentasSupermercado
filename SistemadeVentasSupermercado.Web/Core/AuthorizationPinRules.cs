namespace SistemadeVentasSupermercado.Web.Core
{
    /// <summary>Cómo debe ser el PIN de autorización y cuántos errores se permiten.</summary>
    public static class AuthorizationPinRules
    {
        public const int Length = 6;
        public const int MaxFailedAttempts = 5;
        public static readonly TimeSpan LockoutTime = TimeSpan.FromMinutes(15);

        /// <summary>Exactamente 6 dígitos del 0 al 9.</summary>
        public static bool HasValidFormat(string? pin)
            => pin is { Length: Length } && pin.All(char.IsAsciiDigit);

        /// <summary>
        /// Claves que se adivinan de primeras: grupos repetidos (000000, 121212, 123123) y seguidos
        /// hacia arriba o abajo (123456, 654321, 890123).
        /// </summary>
        public static bool IsTooEasy(string pin)
            => Repeats(pin, 1) || Repeats(pin, 2) || Repeats(pin, 3)
               || IsSequence(pin, 1) || IsSequence(pin, 9);

        private static bool Repeats(string pin, int size)
            => Enumerable.Range(size, pin.Length - size).All(i => pin[i] == pin[i - size]);

        // step 1 = sube de uno en uno; step 9 = baja de uno en uno (cuentas módulo 10).
        private static bool IsSequence(string pin, int step)
            => Enumerable.Range(1, pin.Length - 1).All(i => (pin[i] - pin[i - 1] + 10) % 10 == step);
    }
}
