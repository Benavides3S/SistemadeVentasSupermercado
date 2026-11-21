// Las pruebas corren una por una (no en paralelo) para que las de concurrencia
// midan solo a los "cajeros" que ellas mismas lanzan.
[assembly: CollectionBehavior(DisableTestParallelization = true)]

namespace SistemadeVentasSupermercado.Tests.Infraestructura
{
    /// <summary>
    /// Todas las clases marcadas con [Collection(ColeccionBaseDeDatos.Nombre)]
    /// comparten un único BaseDeDatosFixture (la base se crea una sola vez).
    /// </summary>
    [CollectionDefinition(Nombre)]
    public class ColeccionBaseDeDatos : ICollectionFixture<BaseDeDatosFixture>
    {
        public const string Nombre = "Base de datos de pruebas";
    }
}
