using SistemadeVentasSupermercado.Tests.Infraestructura;
using SistemadeVentasSupermercado.Web.Core;

namespace SistemadeVentasSupermercado.Tests
{
    public class ProfilePhotoRulesTests
    {
        private static readonly byte[] Valida = FotosDePrueba.Jpg();

        [Theory]
        [InlineData(1, 1)]
        [InlineData(256, 256)]
        [InlineData(512, 512)]
        public void UnJpgDentroDeLosLimites_SeAcepta(int ancho, int alto)
            => Assert.Null(ProfilePhotoRules.Validate(FotosDePrueba.Jpg(ancho, alto)));

        [Theory]
        [InlineData(513, 256)]
        [InlineData(256, 513)]
        [InlineData(65535, 65535)] // pocos bytes que, al abrirlos, ocuparían gigas de memoria
        public void DemasiadoAnchoOAlto_SeRechaza(int ancho, int alto)
            => Assert.Equal(ProfilePhotoRules.DimensionsMessage, ProfilePhotoRules.Validate(FotosDePrueba.Jpg(ancho, alto)));

        [Fact]
        public void DemasiadoPesado_SeRechaza_AunqueSeaJpg()
        {
            byte[] pesada = FotosDePrueba.Jpg(bytesDeDatos: ProfilePhotoRules.MaxBytes);

            Assert.Equal(ProfilePhotoRules.TooLargeMessage, ProfilePhotoRules.Validate(pesada));
        }

        [Fact]
        public void LoQueNoEsUnJpg_SeRechaza()
        {
            byte[][] archivos =
            [
                [],
                FotosDePrueba.Svg,
                "Esto es texto, aunque se llame foto.jpg"u8.ToArray(),
                [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 0, 0, 0, 0],  // PNG
                [0xFF, 0xD8, 0xFF, 0xD9],                                       // JPG vacío
                [0xFF, 0xD8, .. FotosDePrueba.Svg],                              // solo la firma
                [0xFF, 0xD8, .. FotosDePrueba.Svg, 0xFF, 0xD9],                  // firma y final
            ];

            Assert.All(archivos, a => Assert.Equal(ProfilePhotoRules.NotJpegMessage, ProfilePhotoRules.Validate(a)));
        }

        [Fact]
        public void ConCualquierCorte_SeRechaza_SinFallar()
        {
            for (int largo = 0; largo < Valida.Length; largo++)
                Assert.NotNull(ProfilePhotoRules.Validate(Valida.AsSpan(0, largo)));
        }

        [Fact]
        public void ConAlgoPegadoDespuesDelFinal_SeRechaza()
            => Assert.NotNull(ProfilePhotoRules.Validate([.. Valida, .. FotosDePrueba.Svg]));

        [Fact]
        public void ConDosBloquesDeMedidas_SeRechaza()
        {
            // El primero dice 256 × 256; el segundo, enorme.
            byte[] doble = [.. Valida[..2], .. FotosDePrueba.Bloque(0xDB, new byte[65]),
                .. FotosDePrueba.BloqueDeMedidas(256, 256), .. FotosDePrueba.BloqueDeMedidas(9000, 9000),
                .. Valida[2..]];

            Assert.NotNull(ProfilePhotoRules.Validate(doble));
        }
    }
}
