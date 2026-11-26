namespace SistemadeVentasSupermercado.Tests.Infraestructura
{
    /// <summary>
    /// Archivos con la estructura de un JPG, armados a mano (no son fotos que se vean). La prueba con un
    /// JPG real de un navegador se hace aparte, en Chrome.
    /// </summary>
    public static class FotosDePrueba
    {
        public static byte[] Jpg(int ancho = 256, int alto = 256, byte pixel = 0x11, int bytesDeDatos = 8)
            =>
            [
                0xFF, 0xD8,
                .. Bloque(0xE0, [.. "JFIF\0"u8, 1, 1, 0, 0, 1, 0, 1, 0, 0]),
                .. Bloque(0xDB, new byte[65]),
                .. BloqueDeMedidas(ancho, alto),
                .. Bloque(0xC4, new byte[20]),
                .. Bloque(0xDA, [1, 1, 0, 0, 63, 0]),
                // Datos comprimidos, con un FF 00 y una marca de reinicio, que son válidos ahí dentro.
                .. Enumerable.Repeat(pixel, bytesDeDatos), 0xFF, 0x00, 0xFF, 0xD0, pixel,
                0xFF, 0xD9
            ];

        public static byte[] BloqueDeMedidas(int ancho, int alto)
            => Bloque(0xC0, [8, (byte)(alto >> 8), (byte)alto, (byte)(ancho >> 8), (byte)ancho, 1, 1, 0x11, 0]);

        public static byte[] Bloque(byte tipo, byte[] contenido)
        {
            int largo = contenido.Length + 2;
            return [0xFF, tipo, (byte)(largo >> 8), (byte)largo, .. contenido];
        }

        public static readonly byte[] Svg =
            "<svg xmlns=\"http://www.w3.org/2000/svg\" onload=\"alert(1)\"><rect width=\"10\" height=\"10\"/></svg>"u8.ToArray();
    }
}
