using Microsoft.Extensions.Options;
using SistemadeVentasSupermercado.Web.Core.ScreenLock;

namespace SistemadeVentasSupermercado.Tests
{
    /// <summary>Las reglas de tiempo del bloqueo de pantalla, con un reloj que se mueve a mano.</summary>
    public class ScreenLockStoreTests
    {
        private sealed class Reloj : TimeProvider
        {
            private DateTimeOffset _ahora = new(2026, 1, 1, 8, 0, 0, TimeSpan.Zero);
            public override DateTimeOffset GetUtcNow() => _ahora;
            public void Avanzar(double segundos) => _ahora = _ahora.AddSeconds(segundos);
        }

        private readonly Reloj _reloj = new();

        // 5 minutos + 60 segundos de margen = 360 segundos.
        private ScreenLockStore Crear(int minutos = 5)
        {
            ScreenLockStore store = new(Options.Create(new ScreenLockOptions { MinutosDeInactividad = minutos }), _reloj);
            store.Start("a");
            return store;
        }

        [Fact]
        public void SinUsoMasDelLimite_SeBloquea_YNoReviveConOtraPeticion()
        {
            ScreenLockStore store = Crear();
            _reloj.Avanzar(361);

            Assert.False(store.TryUse("a"));
            Assert.False(store.TryUse("a"));
            Assert.True(store.IsLocked("a"));
        }

        [Fact]
        public void ConUso_NoSeBloquea()
        {
            ScreenLockStore store = Crear();

            for (int i = 0; i < 10; i++)
            {
                _reloj.Avanzar(359);
                Assert.True(store.TryUse("a"));
            }
        }

        [Fact]
        public void PreguntarNoCuentaComoActividad()
        {
            ScreenLockStore store = Crear();
            _reloj.Avanzar(200);
            Assert.False(store.IsLocked("a"));
            _reloj.Avanzar(200);

            Assert.True(store.IsLocked("a"));
        }

        [Fact]
        public void ConCeroMinutos_NoHayBloqueoAutomatico_PeroElBotonSirve()
        {
            ScreenLockStore store = Crear(minutos: 0);
            _reloj.Avanzar(7 * 3600);
            Assert.True(store.TryUse("a"));

            store.Lock("a");

            Assert.False(store.TryUse("a"));
        }

        [Fact]
        public void Desbloquear_VuelveAContarElTiempoDesdeCero()
        {
            ScreenLockStore store = Crear();
            _reloj.Avanzar(1000);
            Assert.True(store.IsLocked("a"));

            store.Unlock("a");
            _reloj.Avanzar(359);

            Assert.True(store.TryUse("a"));
        }

        [Fact]
        public void UnaSesionDesconocida_SeConsideraBloqueada()
        {
            // Por ejemplo, después de reiniciar la aplicación.
            Assert.False(Crear().TryUse("otra"));
        }

        [Fact]
        public void CadaSesionVaPorSeparado()
        {
            ScreenLockStore store = Crear();
            store.Start("b");

            store.Lock("a");

            Assert.True(store.TryUse("b"));
        }
    }
}
