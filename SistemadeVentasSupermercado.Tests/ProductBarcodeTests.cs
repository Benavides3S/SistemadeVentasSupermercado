using Microsoft.EntityFrameworkCore;
using SistemadeVentasSupermercado.Tests.Infraestructura;
using SistemadeVentasSupermercado.Web.Core;
using SistemadeVentasSupermercado.Web.Core.Pagination;
using SistemadeVentasSupermercado.Web.Data;
using SistemadeVentasSupermercado.Web.Data.Entities;
using SistemadeVentasSupermercado.Web.DTOs;
using SistemadeVentasSupermercado.Web.Services.Implementations;

namespace SistemadeVentasSupermercado.Tests
{
    /// <summary>Código de barras en la administración de productos: normalización, validación y unicidad.</summary>
    [Collection(ColeccionBaseDeDatos.Nombre)]
    public class ProductBarcodeTests
    {
        private readonly BaseDeDatosFixture _fixture;

        public ProductBarcodeTests(BaseDeDatosFixture fixture)
        {
            _fixture = fixture;
        }

        /// <summary>Código único por prueba (la base exige que no se repita).</summary>
        private static string NuevoCodigo() => "77" + Random.Shared.NextInt64(10_000_000_000, 99_999_999_999);

        private ProductDTO NuevoProducto(string? codigo) => new ProductDTO
        {
            Name = $"Producto código {Guid.NewGuid().ToString("N")[..8]}",
            Barcode = codigo,
            Price = 1000m,
            Stock = 5m,
            Unit = UnitOfMeasure.Unit,
            CategoryId = _fixture.CategoriaId
        };

        private async Task<Response<ProductDTO>> CrearAsync(ProductDTO dto)
        {
            await using DataContext context = _fixture.CrearContexto();
            return await new ProductsService(context).CreateAsync(dto);
        }

        private async Task<ProductDTO> AbrirFormularioAsync(Guid id)
        {
            await using DataContext context = _fixture.CrearContexto();
            return (await new ProductsService(context).GetOneAsync(id)).Result!;
        }

        private async Task<Response<ProductDTO>> GuardarAsync(ProductDTO dto)
        {
            await using DataContext context = _fixture.CrearContexto();
            return await new ProductsService(context).EditAsync(dto);
        }

        private async Task<string?> CodigoGuardadoAsync(Guid id)
        {
            await using DataContext context = _fixture.CrearContexto();
            return await context.Products.Where(p => p.Id == id).Select(p => p.Barcode).SingleAsync();
        }

        // ---------- Normalización y validación (sin base de datos) ----------

        [Theory]
        [InlineData(null, null)]
        [InlineData("", null)]
        [InlineData("   ", null)]
        [InlineData(" 7702001 ", "7702001")]
        [InlineData("770 2001\t03", "770200103")]
        [InlineData("ABC-12", "ABC-12")]
        public void Normaliza_quitando_espacios_y_vacio_es_sin_codigo(string? entrada, string? esperado)
        {
            Assert.Equal(esperado, BarcodeHelper.Normalize(entrada));
        }

        [Theory]
        [InlineData("7702/001")]
        [InlineData("código")]
        [InlineData("77'; DROP")]
        public void Rechaza_caracteres_no_permitidos(string codigo)
        {
            Assert.NotNull(BarcodeHelper.Validate(BarcodeHelper.Normalize(codigo)));
        }

        [Fact]
        public void Rechaza_codigos_de_mas_de_50_caracteres()
        {
            Assert.NotNull(BarcodeHelper.Validate(new string('1', 51)));
            Assert.Null(BarcodeHelper.Validate(new string('1', 50)));
        }

        // ---------- Crear y editar ----------

        [Fact]
        public async Task Crear_guarda_el_codigo_normalizado()
        {
            string codigo = NuevoCodigo();
            Response<ProductDTO> respuesta = await CrearAsync(NuevoProducto($"  {codigo[..6]} {codigo[6..]} "));

            Assert.True(respuesta.IsSuccess, respuesta.Message);
            await using DataContext context = _fixture.CrearContexto();
            Assert.True(await context.Products.AnyAsync(p => p.Barcode == codigo));
        }

        [Fact]
        public async Task Crear_sin_codigo_lo_deja_vacio_y_varios_productos_pueden_no_tenerlo()
        {
            Response<ProductDTO> uno = await CrearAsync(NuevoProducto(""));
            Response<ProductDTO> otro = await CrearAsync(NuevoProducto("   "));

            Assert.True(uno.IsSuccess, uno.Message);
            Assert.True(otro.IsSuccess, otro.Message);
            await using DataContext context = _fixture.CrearContexto();
            Assert.Null(await context.Products.Where(p => p.Name == uno.Result!.Name).Select(p => p.Barcode).SingleAsync());
        }

        [Fact]
        public async Task Crear_con_un_codigo_repetido_dice_que_producto_lo_tiene()
        {
            string codigo = NuevoCodigo();
            Product existente = await _fixture.CrearProductoAsync("Con código", 2000m, 3m, codigo: codigo);

            Response<ProductDTO> respuesta = await CrearAsync(NuevoProducto(codigo));

            Assert.False(respuesta.IsSuccess);
            Assert.Contains(existente.Name, respuesta.Message);
        }

        [Fact]
        public async Task Crear_con_un_codigo_invalido_falla()
        {
            Response<ProductDTO> respuesta = await CrearAsync(NuevoProducto("77/01"));

            Assert.False(respuesta.IsSuccess);
            Assert.Contains("letras, números y guiones", respuesta.Message);
        }

        [Fact]
        public async Task Editar_sin_tocar_el_codigo_lo_conserva()
        {
            string codigo = NuevoCodigo();
            Product producto = await _fixture.CrearProductoAsync("Editar código", 2000m, 3m, codigo: codigo);

            ProductDTO formulario = await AbrirFormularioAsync(producto.Id);
            formulario.Price = 2500m;
            Response<ProductDTO> respuesta = await GuardarAsync(formulario);

            Assert.True(respuesta.IsSuccess, respuesta.Message);
            Assert.Equal(codigo, await CodigoGuardadoAsync(producto.Id));
        }

        [Fact]
        public async Task Editar_puede_cambiar_o_quitar_el_codigo()
        {
            Product producto = await _fixture.CrearProductoAsync("Cambiar código", 2000m, 3m, codigo: NuevoCodigo());
            string nuevo = NuevoCodigo();

            ProductDTO formulario = await AbrirFormularioAsync(producto.Id);
            formulario.Barcode = nuevo;
            Assert.True((await GuardarAsync(formulario)).IsSuccess);
            Assert.Equal(nuevo, await CodigoGuardadoAsync(producto.Id));

            formulario = await AbrirFormularioAsync(producto.Id);
            formulario.Barcode = "";
            Assert.True((await GuardarAsync(formulario)).IsSuccess);
            Assert.Null(await CodigoGuardadoAsync(producto.Id));
        }

        [Fact]
        public async Task Editar_con_el_codigo_de_otro_producto_falla_y_no_cambia_nada()
        {
            string codigoOtro = NuevoCodigo();
            Product otro = await _fixture.CrearProductoAsync("Dueño del código", 2000m, 3m, codigo: codigoOtro);
            string propio = NuevoCodigo();
            Product producto = await _fixture.CrearProductoAsync("Intenta copiar", 2000m, 3m, codigo: propio);

            ProductDTO formulario = await AbrirFormularioAsync(producto.Id);
            formulario.Barcode = codigoOtro;
            formulario.Price = 9999m;
            Response<ProductDTO> respuesta = await GuardarAsync(formulario);

            Assert.False(respuesta.IsSuccess);
            Assert.Contains(otro.Name, respuesta.Message);
            Assert.Equal(propio, await CodigoGuardadoAsync(producto.Id));
        }

        [Fact]
        public async Task Editar_conservando_su_propio_codigo_no_choca_consigo_mismo()
        {
            string codigo = NuevoCodigo();
            Product producto = await _fixture.CrearProductoAsync("Mismo código", 2000m, 3m, codigo: codigo);

            ProductDTO formulario = await AbrirFormularioAsync(producto.Id);
            formulario.Barcode = $" {codigo} ";
            Response<ProductDTO> respuesta = await GuardarAsync(formulario);

            Assert.True(respuesta.IsSuccess, respuesta.Message);
        }

        // ---------- Lista ----------

        [Fact]
        public async Task La_lista_de_productos_busca_por_codigo()
        {
            string codigo = NuevoCodigo();
            Product producto = await _fixture.CrearProductoAsync("Buscar por código", 2000m, 3m, codigo: codigo);

            await using DataContext context = _fixture.CrearContexto();
            Response<PaginationResponse<ProductDTO>> respuesta = await new ProductsService(context)
                .GetPaginatedListAsync(new PaginationRequest { Filter = codigo, RecordsPerPage = 50 });

            Assert.True(respuesta.IsSuccess, respuesta.Message);
            Assert.Contains(respuesta.Result!.List, p => p.Id == producto.Id);
        }
    }
}
