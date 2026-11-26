using SistemadeVentasSupermercado.Web.Data.Entities;

namespace SistemadeVentasSupermercado.Web.Core
{
    /// <summary>
    /// Qué foto de perfil acepta el servidor. No confía en el nombre del archivo ni en el tipo que diga
    /// el navegador: solo en el contenido.
    /// Se recorre la estructura del JPEG de principio a fin y se leen sus dimensiones. NO se decodifica
    /// la imagen (haría falta un paquete): un archivo bien armado con píxeles dañados se aceptaría y
    /// se vería mal, solo a su dueño.
    /// </summary>
    public static class ProfilePhotoRules
    {
        public const int MaxBytes = UserPhoto.MaxBytes;

        /// <summary>Petición completa: la imagen más lo que agrega el formulario.</summary>
        public const int MaxRequestBytes = MaxBytes + 16 * 1024;

        /// <summary>Ancho y alto máximos. La pantalla envía 256 × 256.</summary>
        public const int MaxDimension = 512;

        public const string TooLargeMessage = "La foto es demasiado grande (máximo 200 KB).";
        public const string NotJpegMessage = "El archivo no es una foto JPG válida.";
        public static readonly string DimensionsMessage =
            $"La foto no puede medir más de {MaxDimension} × {MaxDimension} píxeles.";

        /// <summary>Devuelve null si la foto se acepta; si no, el motivo.</summary>
        public static string? Validate(ReadOnlySpan<byte> data)
        {
            if (data.Length > MaxBytes)
                return TooLargeMessage;

            // Empieza con "inicio de imagen" (FF D8).
            if (data.Length < 4 || data[0] != 0xFF || data[1] != 0xD8)
                return NotJpegMessage;

            bool hasSize = false;
            bool hasQuantization = false;
            bool hasScan = false;
            int position = 2;

            while (position + 1 < data.Length)
            {
                if (data[position] != 0xFF)
                    return NotJpegMessage;

                byte marker = data[position + 1];

                if (marker == 0xFF) // relleno permitido entre bloques
                {
                    position++;
                    continue;
                }

                position += 2;

                // "Fin de imagen": debe ser lo último del archivo (nada pegado después).
                if (marker == 0xD9)
                    return hasScan && position == data.Length ? null : NotJpegMessage;

                // Los demás bloques traen su longitud (que se cuenta a sí misma).
                if (position + 2 > data.Length)
                    return NotJpegMessage;
                int length = (data[position] << 8) | data[position + 1];
                if (length < 2 || position + length > data.Length)
                    return NotJpegMessage;

                ReadOnlySpan<byte> body = data.Slice(position + 2, length - 2);
                position += length;

                switch (marker)
                {
                    case 0xC0 or 0xC1 or 0xC2: // medidas (los tipos de JPG que muestran los navegadores)
                        // Solo un bloque de medidas: con dos, el navegador podría usar el otro.
                        if (hasSize || body.Length < 6 || body[0] != 8)
                            return NotJpegMessage;
                        int height = (body[1] << 8) | body[2];
                        int width = (body[3] << 8) | body[4];
                        int components = body[5];
                        if (width == 0 || height == 0 || components is not (1 or 3) || body.Length != 6 + 3 * components)
                            return NotJpegMessage;
                        if (width > MaxDimension || height > MaxDimension)
                            return DimensionsMessage;
                        hasSize = true;
                        break;

                    case 0xDB:
                        hasQuantization = true;
                        break;

                    case 0xC4 or 0xDD or 0xFE or (>= 0xE0 and <= 0xEF): // tablas, comentarios y metadatos
                        break;

                    case 0xDA: // datos de la imagen
                        if (!hasSize || !hasQuantization)
                            return NotJpegMessage;
                        int start = position;
                        position = EndOfScan(data, position);
                        if (position == start)
                            return NotJpegMessage;
                        hasScan = true;
                        break;

                    default:
                        return NotJpegMessage;
                }
            }

            return NotJpegMessage; // se acabó el archivo sin "fin de imagen"
        }

        /// <summary>Posición del bloque que sigue a los datos comprimidos.</summary>
        private static int EndOfScan(ReadOnlySpan<byte> data, int position)
        {
            while (position + 1 < data.Length)
            {
                // Dentro de los datos, FF 00 es un byte normal y FF D0..D7 son marcas de reinicio.
                if (data[position] == 0xFF && data[position + 1] != 0x00 && data[position + 1] is not (>= 0xD0 and <= 0xD7))
                    return position;
                position++;
            }

            return data.Length;
        }
    }
}
