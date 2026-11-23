using Microsoft.EntityFrameworkCore;
using SistemadeVentasSupermercado.Tests.Infraestructura;
using SistemadeVentasSupermercado.Web.Core;
using SistemadeVentasSupermercado.Web.Data;
using SistemadeVentasSupermercado.Web.Data.Entities;
using SistemadeVentasSupermercado.Web.DTOs;
using SistemadeVentasSupermercado.Web.Services.Implementations;

namespace SistemadeVentasSupermercado.Tests
{
    /// <summary>
    /// Edición de categorías, clientes y productos: se carga el registro de la base y solo se cambian
    /// los campos del formulario. Lo demás (relaciones, unidad de medida, etc.) se conserva.
    /// </summary>
    [Collection(ColeccionBaseDeDatos.Nombre)]
    public class CatalogEditTests
    {
        private readonly BaseDeDatosFixture _fixture;

        public CatalogEditTests(BaseDeDatosFixture fixture)
        {
            _fixture = fixture;
        }

        private static string Unico(string nombre) => $"{nombre} {Guid.NewGuid().ToString("N")[..8]}";

        private async Task<Category> CrearCategoriaAsync(bool activa = true)
        {
            await using DataContext context = _fixture.CrearContexto();
            Category categoria = new Category { Id = Guid.NewGuid(), Name = Unico("Categoría"), IsActive = activa };
            context.Categories.Add(categoria);
            await context.SaveChangesAsync();
            return categoria;
        }

        private async Task<Client> CrearClienteAsync()
        {
            await using DataContext context = _fixture.CrearContexto();
            // Name y Email son únicos en la base.
            Client cliente = new Client
            {
                Id = Guid.NewGuid(), Name = Unico("Cliente"), Address = "Calle 1", Phone = "300",
                Email = $"cliente-{Guid.NewGuid().ToString("N")[..8]}@correo.co"
            };
            context.Clients.Add(cliente);
            await context.SaveChangesAsync();
            return cliente;
        }

        // ---------- Categorías ----------

        [Fact]
        public async Task Editar_categoria_cambia_nombre_y_estado_y_conserva_sus_productos()
        {
            Category categoria = await CrearCategoriaAsync();
            Product producto = await _fixture.CrearProductoAsync("Yogur", 3000m, 5m, categoriaId: categoria.Id);
            string nuevoNombre = Unico("Lácteos");

            Response<CategoryDTO> respuesta;
            await using (DataContext context = _fixture.CrearContexto())
            {
                respuesta = await new CategoriesService(context)
                    .EditAsync(new CategoryDTO { Id = categoria.Id, Name = nuevoNombre, IsActive = false });
            }

            Assert.True(respuesta.IsSuccess, respuesta.Message);

            await using DataContext verificacion = _fixture.CrearContexto();
            Category guardada = await verificacion.Categories.Include(c => c.Products).SingleAsync(c => c.Id == categoria.Id);
            Assert.Equal(nuevoNombre, guardada.Name);
            Assert.False(guardada.IsActive);
            Assert.Contains(guardada.Products, p => p.Id == producto.Id);
        }

        [Fact]
        public async Task Editar_categoria_inexistente_no_crea_nada()
        {
            Guid id = Guid.NewGuid();

            Response<CategoryDTO> respuesta;
            await using (DataContext context = _fixture.CrearContexto())
            {
                respuesta = await new CategoriesService(context)
                    .EditAsync(new CategoryDTO { Id = id, Name = Unico("Fantasma") });
            }

            Assert.False(respuesta.IsSuccess);
            await using DataContext verificacion = _fixture.CrearContexto();
            Assert.False(await verificacion.Categories.AnyAsync(c => c.Id == id));
        }

        // ---------- Clientes ----------

        [Fact]
        public async Task Editar_cliente_cambia_solo_ese_cliente()
        {
            Client cliente = await CrearClienteAsync();
            Client otro = await CrearClienteAsync();
            string nuevoNombre = Unico("Ana María");
            string correoNuevo = $"am-{Guid.NewGuid().ToString("N")[..8]}@correo.co";

            Response<ClientDTO> respuesta;
            await using (DataContext context = _fixture.CrearContexto())
            {
                respuesta = await new ClientsService(context).EditAsync(new ClientDTO
                {
                    Id = cliente.Id, Name = nuevoNombre, Address = null, Phone = "301", Email = correoNuevo
                });
            }

            Assert.True(respuesta.IsSuccess, respuesta.Message);

            await using DataContext verificacion = _fixture.CrearContexto();
            Client guardado = await verificacion.Clients.SingleAsync(c => c.Id == cliente.Id);
            Assert.Equal(nuevoNombre, guardado.Name);
            Assert.Null(guardado.Address);
            Assert.Equal("301", guardado.Phone);
            Assert.Equal(correoNuevo, guardado.Email);

            Client otroGuardado = await verificacion.Clients.SingleAsync(c => c.Id == otro.Id);
            Assert.Equal(otro.Name, otroGuardado.Name);
            Assert.Equal("Calle 1", otroGuardado.Address);
        }

        [Fact]
        public async Task Editar_cliente_con_ventas_conserva_sus_ventas()
        {
            Client cliente = await CrearClienteAsync();
            Product producto = await _fixture.CrearProductoAsync("Galletas", 2000m, 5m);

            await using (DataContext context = _fixture.CrearContexto())
            {
                Response<SaleResultDTO> venta = await new SalesService(context).CreateAsync(new CreateSaleDTO
                {
                    ClientId = cliente.Id,
                    PaymentMethod = PaymentMethod.Cash,
                    Items = { new SaleLineDTO { ProductId = producto.Id, Quantity = 1m } },
                    IdempotencyKey = Guid.NewGuid()
                }, _fixture.CajeroId);
                Assert.True(venta.IsSuccess, venta.Message);
            }

            await using (DataContext context = _fixture.CrearContexto())
            {
                Response<ClientDTO> respuesta = await new ClientsService(context).EditAsync(new ClientDTO
                {
                    Id = cliente.Id, Name = Unico("Renombrado"), Phone = "302"
                });
                Assert.True(respuesta.IsSuccess, respuesta.Message);
            }

            await using DataContext verificacion = _fixture.CrearContexto();
            Assert.Equal(1, await verificacion.Sales.CountAsync(s => s.ClientId == cliente.Id));
        }

        // ---------- Productos ----------

        [Fact]
        public async Task Editar_producto_por_peso_conserva_su_unidad()
        {
            // El formulario de edición no tiene el campo "Unidad de medida": antes, al guardar,
            // el producto quedaba como "Unidad" y ya no se podía vender por peso.
            Product queso = await _fixture.CrearProductoAsync("Queso", 12000m, 5m, UnitOfMeasure.Kilogram);

            Response<ProductDTO> respuesta;
            await using (DataContext context = _fixture.CrearContexto())
            {
                respuesta = await new ProductsService(context).EditAsync(new ProductDTO
                {
                    Id = queso.Id, Name = queso.Name, Price = 13000m, Stock = 5m, CategoryId = queso.CategoryId
                    // Unit no viene del formulario: queda en su valor por defecto (Unidad)
                });
            }

            Assert.True(respuesta.IsSuccess, respuesta.Message);

            await using DataContext verificacion = _fixture.CrearContexto();
            Product guardado = await verificacion.Products.SingleAsync(p => p.Id == queso.Id);
            Assert.Equal(13000m, guardado.Price);
            Assert.Equal(UnitOfMeasure.Kilogram, guardado.Unit);
        }
    }
}
