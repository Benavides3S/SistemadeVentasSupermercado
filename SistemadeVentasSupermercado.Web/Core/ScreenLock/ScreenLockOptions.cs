namespace SistemadeVentasSupermercado.Web.Core.ScreenLock
{
    /// <summary>Sección "Seguridad" de appsettings.json.</summary>
    public class ScreenLockOptions
    {
        public const string SectionName = "Seguridad";

        /// <summary>Minutos sin usar la pantalla para bloquearla. 0 = sin bloqueo automático.</summary>
        public int MinutosDeInactividad { get; set; } = 5;

        /// <summary>
        /// Margen que da el servidor sobre ese tiempo: la pantalla avisa que hay uso como máximo cada 30
        /// segundos, así que el servidor se entera con un poco de retraso.
        /// </summary>
        public static readonly TimeSpan ServerGrace = TimeSpan.FromSeconds(60);

        /// <summary>Cuánto puede pasar sin noticias antes de que el servidor bloquee; null = nunca.</summary>
        public TimeSpan? ServerIdleLimit => MinutosDeInactividad > 0
            ? TimeSpan.FromMinutes(MinutosDeInactividad) + ServerGrace
            : null;
    }
}
