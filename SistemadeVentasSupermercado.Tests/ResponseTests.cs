using SistemadeVentasSupermercado.Web.Core;

namespace SistemadeVentasSupermercado.Tests
{
    public class ResponseTests
    {
        [Fact]
        public void Failure_SinErrores_DejaListaVaciaYNoNula()
        {
            Response<string> r = Response<string>.Failure("No se pudo");

            Assert.False(r.IsSuccess);
            Assert.NotNull(r.Errors);
            Assert.Empty(r.Errors);
        }

        [Fact]
        public void Failure_ConErrores_ConservaLosRecibidos()
        {
            Response<string> r = Response<string>.Failure("No se pudo", ["uno", "dos"]);

            Assert.Equal(["uno", "dos"], r.Errors);
        }
    }
}
