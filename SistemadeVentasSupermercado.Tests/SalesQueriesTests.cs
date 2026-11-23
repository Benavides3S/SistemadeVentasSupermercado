using SistemadeVentasSupermercado.Tests.Infraestructura;
using SistemadeVentasSupermercado.Web.Core;
using SistemadeVentasSupermercado.Web.Core.Pagination;
using SistemadeVentasSupermercado.Web.Data;
using SistemadeVentasSupermercado.Web.Data.Entities;
using SistemadeVentasSupermercado.Web.DTOs;
using SistemadeVentasSupermercado.Web.Services.Implementations;

namespace SistemadeVentasSupermercado.Tests
{
    /// <summary>
    /// Pruebas de las consultas que usa la pantalla de venta:
    /// buscadores, historial y comprobante.
    /// </summary>
    [Collection(ColeccionBaseDeDatos.Nombre)]
    public class SalesQueriesTests
    {
        private readonly BaseDeDatosFixture _fixture;

        public SalesQueriesTests(BaseDeDatosFixture fixture)
        {
            _fixture = fixture;
        }

        // ---------- Ayudantes ----------

        /// <summary>Palabra única para que las búsquedas solo encuentren lo que crea cada prueba.</summary>
        private static string Marca() => "zz" + Guid.NewGuid().ToString("N")[..10];

        private async Task<T> ConServicioAsync<T>(Func<SalesService, Task<T>> accion)
        {
            await using DataContext context = _fixture.CrearContexto();
            return await accion(new SalesService(context));
        }

        private async Task<Client> CrearClienteAsync(string nombre)
        {
            await using DataContext context = _fixture.CrearContexto();
            Client cliente = new Client
            {
                Id = Guid.NewGuid(),
                Name = nombre,
                Email = $"{Guid.NewGuid():N}@pruebas.local"
            };
            context.Clients.Add(cliente);
            await context.SaveChangesAsync();
            return cliente;
        }

        private Task<Response<SaleResultDTO>> VenderAsync(Guid productoId, decimal cantidad, Guid? clienteId = null)
            => ConServicioAsync(s => s.CreateAsync(new CreateSaleDTO
            {
                ClientId = clienteId,
                PaymentMethod = PaymentMethod.Card,
                Items = { new SaleLineDTO { ProductId = productoId, Quantity = cantidad } },
                IdempotencyKey = Guid.NewGuid()
            }, _fixture.CajeroId));

        // ---------- Buscador de productos ----------

        [Fact]
        public async Task Buscar_productos_solo_trae_los_que_se_pueden_vender()
        {
            string marca = Marca();
            Product conStock = await _fixture.CrearProductoAsync($"Mango {marca}", 4000m, 3m, UnitOfMeasure.Kilogram);
            Product sinStock = await _fixture.CrearProductoAsync($"Mango agotado {marca}", 4000m, 0m);
            Product inactivo = await _fixture.CrearProductoAsync($"Mango viejo {marca}", 4000m, 3m,
                categoriaId: _fixture.CategoriaInactivaId);

            Response<List<PosProductDTO>> respuesta = await ConServicioAsync(s => s.SearchProductsAsync(marca.ToUpper()));

            Assert.True(respuesta.IsSuccess, respuesta.Message);
            PosProductDTO encontrado = Assert.Single(respuesta.Result!);
            Assert.Equal(conStock.Id, encontrado.Id);
            Assert.Equal(4000m, encontrado.Price);
            Assert.Equal("kg", encontrado.UnitAbbreviation);
            Assert.True(encontrado.AllowsDecimals);
        }

        [Theory]
        [InlineData(null)]
        [InlineData("")]
        [InlineData("   ")]
        public async Task Buscar_productos_sin_texto_devuelve_lista_vacia(string? texto)
        {
            Response<List<PosProductDTO>> respuesta = await ConServicioAsync(s => s.SearchProductsAsync(texto));

            Assert.True(respuesta.IsSuccess);
            Assert.Empty(respuesta.Result!);
        }

        [Fact]
        public async Task Buscar_productos_devuelve_maximo_veinte()
        {
            string marca = Marca();
            for (int i = 0; i < 25; i++)
                await _fixture.CrearProductoAsync($"Galleta {marca}", 1000m, 5m);

            Response<List<PosProductDTO>> respuesta = await ConServicioAsync(s => s.SearchProductsAsync(marca));

            Assert.Equal(20, respuesta.Result!.Count);
            Assert.All(respuesta.Result, p => Assert.False(p.AllowsDecimals));
        }

        // ---------- Buscador de clientes ----------

        [Fact]
        public async Task Buscar_clientes_por_nombre_o_por_correo()
        {
            string marca = Marca();
            Client cliente = await CrearClienteAsync($"Rosa {marca}");

            Response<List<PosClientDTO>> porNombre = await ConServicioAsync(s => s.SearchClientsAsync(marca));
            Response<List<PosClientDTO>> porCorreo = await ConServicioAsync(s => s.SearchClientsAsync(cliente.Email![..12]));

            Assert.Equal(cliente.Id, Assert.Single(porNombre.Result!).Id);
            Assert.Equal(cliente.Id, Assert.Single(porCorreo.Result!).Id);
        }

        // ---------- Comprobante ----------

        [Fact]
        public async Task Obtener_una_venta_trae_lineas_cajero_y_cliente()
        {
            Client cliente = await CrearClienteAsync($"Luis {Marca()}");
            Product cafe = await _fixture.CrearProductoAsync("Café", 15000m, 5m);
            Response<SaleResultDTO> venta = await VenderAsync(cafe.Id, 2m, cliente.Id);

            Response<SaleDetailDTO> respuesta = await ConServicioAsync(s => s.GetOneAsync(venta.Result!.Id));

            Assert.True(respuesta.IsSuccess, respuesta.Message);
            SaleDetailDTO detalle = respuesta.Result!;
            Assert.Equal(venta.Result!.Number, detalle.Number);
            Assert.Equal("Cajero Pruebas", detalle.CashierName);
            Assert.Equal(cliente.Name, detalle.ClientName);
            Assert.Equal(PaymentMethod.Card, detalle.PaymentMethod);
            Assert.Equal(30000m, detalle.Total);

            SaleDetailLineDTO linea = Assert.Single(detalle.Items);
            Assert.Equal(cafe.Name, linea.ProductName);
            Assert.Equal(2m, linea.Quantity);
            Assert.Equal(15000m, linea.UnitPrice);
        }

        [Fact]
        public async Task Obtener_una_venta_inexistente_falla()
        {
            Response<SaleDetailDTO> respuesta = await ConServicioAsync(s => s.GetOneAsync(Guid.NewGuid()));

            Assert.False(respuesta.IsSuccess);
            Assert.Contains("no existe", respuesta.Message);
        }

        // ---------- Historial ----------

        [Fact]
        public async Task Historial_muestra_primero_la_venta_mas_reciente()
        {
            Product pan = await _fixture.CrearProductoAsync("Pan", 300m, 10m);
            Response<SaleResultDTO> primera = await VenderAsync(pan.Id, 1m);
            Response<SaleResultDTO> segunda = await VenderAsync(pan.Id, 1m);
            Response<SaleResultDTO> tercera = await VenderAsync(pan.Id, 1m);

            Response<PaginationResponse<SaleListItemDTO>> respuesta = await ConServicioAsync(
                s => s.GetPaginatedListAsync(new PaginationRequest { Page = 1, RecordsPerPage = 2 }));

            Assert.True(respuesta.IsSuccess, respuesta.Message);
            PaginationResponse<SaleListItemDTO> pagina = respuesta.Result!;
            Assert.Equal(2, pagina.List.Count);
            Assert.Equal(tercera.Result!.Number, pagina.List[0].Number);
            Assert.Equal(segunda.Result!.Number, pagina.List[1].Number);
            Assert.True(pagina.TotalCount >= 3);
            Assert.Equal("Cajero Pruebas", pagina.List[0].CashierName);
        }

        [Fact]
        public async Task Historial_filtra_por_numero_de_venta()
        {
            Product leche = await _fixture.CrearProductoAsync("Leche", 1200m, 10m);
            await VenderAsync(leche.Id, 1m);
            Response<SaleResultDTO> buscada = await VenderAsync(leche.Id, 2m);

            Response<PaginationResponse<SaleListItemDTO>> respuesta = await ConServicioAsync(
                s => s.GetPaginatedListAsync(new PaginationRequest { Filter = buscada.Result!.Number.ToString() }));

            SaleListItemDTO fila = Assert.Single(respuesta.Result!.List);
            Assert.Equal(buscada.Result!.Id, fila.Id);
            Assert.Equal(2400m, fila.Total);
        }

        [Fact]
        public async Task Historial_filtra_por_nombre_de_cliente()
        {
            string marca = Marca();
            Client cliente = await CrearClienteAsync($"Marta {marca}");
            Product arroz = await _fixture.CrearProductoAsync("Arroz", 2500m, 10m);
            await VenderAsync(arroz.Id, 1m);                     // sin cliente
            Response<SaleResultDTO> conCliente = await VenderAsync(arroz.Id, 1m, cliente.Id);

            Response<PaginationResponse<SaleListItemDTO>> respuesta = await ConServicioAsync(
                s => s.GetPaginatedListAsync(new PaginationRequest { Filter = marca }));

            SaleListItemDTO fila = Assert.Single(respuesta.Result!.List);
            Assert.Equal(conCliente.Result!.Id, fila.Id);
            Assert.Equal(cliente.Name, fila.ClientName);
        }
    }
}
