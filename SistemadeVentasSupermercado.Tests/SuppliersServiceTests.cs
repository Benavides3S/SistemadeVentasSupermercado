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
    /// Proveedores: nombre y NIT únicos, textos opcionales vacíos guardados como null y edición
    /// que solo cambia los campos del formulario.
    /// </summary>
    [Collection(ColeccionBaseDeDatos.Nombre)]
    public class SuppliersServiceTests
    {
        private readonly BaseDeDatosFixture _fixture;

        public SuppliersServiceTests(BaseDeDatosFixture fixture)
        {
            _fixture = fixture;
        }

        private static string Unico(string nombre) => $"{nombre} {Guid.NewGuid().ToString("N")[..8]}";

        private async Task<Response<SupplierDTO>> CrearAsync(SupplierDTO dto)
        {
            await using DataContext context = _fixture.CrearContexto();
            return await new SuppliersService(context).CreateAsync(dto);
        }

        [Fact]
        public async Task Crear_proveedor_normaliza_nombre_y_guarda_vacios_como_null()
        {
            string nombre = Unico("Distribuidora");

            Response<SupplierDTO> respuesta = await CrearAsync(new SupplierDTO
            {
                Name = $"  {nombre.Replace(" ", "   ")}  ", TaxId = "   ", Phone = "", Email = null
            });

            Assert.True(respuesta.IsSuccess, respuesta.Message);

            await using DataContext verificacion = _fixture.CrearContexto();
            Supplier guardado = await verificacion.Suppliers.SingleAsync(s => s.Name == nombre);
            Assert.Null(guardado.TaxId);
            Assert.Null(guardado.Phone);
            Assert.True(guardado.IsActive);
        }

        [Fact]
        public async Task Dos_proveedores_sin_NIT_son_validos()
        {
            Response<SupplierDTO> primero = await CrearAsync(new SupplierDTO { Name = Unico("Sin NIT") });
            Response<SupplierDTO> segundo = await CrearAsync(new SupplierDTO { Name = Unico("Sin NIT") });

            Assert.True(primero.IsSuccess, primero.Message);
            Assert.True(segundo.IsSuccess, segundo.Message);
        }

        [Fact]
        public async Task No_permite_nombre_repetido_sin_importar_mayusculas()
        {
            string nombre = Unico("Lácteos del Sur");
            Assert.True((await CrearAsync(new SupplierDTO { Name = nombre })).IsSuccess);

            Response<SupplierDTO> repetido = await CrearAsync(new SupplierDTO { Name = nombre.ToUpperInvariant() });

            Assert.False(repetido.IsSuccess);
        }

        [Fact]
        public async Task No_permite_NIT_repetido()
        {
            string nit = Guid.NewGuid().ToString("N")[..12];
            Assert.True((await CrearAsync(new SupplierDTO { Name = Unico("Proveedor"), TaxId = nit })).IsSuccess);

            Response<SupplierDTO> repetido = await CrearAsync(new SupplierDTO { Name = Unico("Proveedor"), TaxId = nit });

            Assert.False(repetido.IsSuccess);
            Assert.Contains(nit, repetido.Message);
        }

        [Fact]
        public async Task Editar_proveedor_cambia_sus_datos_y_puede_desactivarlo()
        {
            string nombre = Unico("Original");
            Assert.True((await CrearAsync(new SupplierDTO { Name = nombre, Phone = "300" })).IsSuccess);

            Guid id;
            await using (DataContext lectura = _fixture.CrearContexto())
                id = (await lectura.Suppliers.SingleAsync(s => s.Name == nombre)).Id;

            string nuevoNombre = Unico("Renombrado");
            Response<SupplierDTO> respuesta;
            await using (DataContext context = _fixture.CrearContexto())
            {
                respuesta = await new SuppliersService(context).EditAsync(new SupplierDTO
                {
                    Id = id, Name = nuevoNombre, Phone = null, Notes = "Nota", IsActive = false
                });
            }

            Assert.True(respuesta.IsSuccess, respuesta.Message);

            await using DataContext verificacion = _fixture.CrearContexto();
            Supplier guardado = await verificacion.Suppliers.SingleAsync(s => s.Id == id);
            Assert.Equal(nuevoNombre, guardado.Name);
            Assert.Null(guardado.Phone);
            Assert.Equal("Nota", guardado.Notes);
            Assert.False(guardado.IsActive);
        }

        [Fact]
        public async Task Editar_no_permite_el_nombre_de_otro_proveedor()
        {
            string nombreAjeno = Unico("Ajeno");
            Assert.True((await CrearAsync(new SupplierDTO { Name = nombreAjeno })).IsSuccess);
            string nombrePropio = Unico("Propio");
            Assert.True((await CrearAsync(new SupplierDTO { Name = nombrePropio })).IsSuccess);

            Guid id;
            await using (DataContext lectura = _fixture.CrearContexto())
                id = (await lectura.Suppliers.SingleAsync(s => s.Name == nombrePropio)).Id;

            Response<SupplierDTO> respuesta;
            await using (DataContext context = _fixture.CrearContexto())
                respuesta = await new SuppliersService(context).EditAsync(new SupplierDTO { Id = id, Name = nombreAjeno });

            Assert.False(respuesta.IsSuccess);
        }

        [Fact]
        public async Task Editar_conservando_su_propio_nombre_y_NIT_es_valido()
        {
            string nombre = Unico("Mismo");
            string nit = Guid.NewGuid().ToString("N")[..12];
            Assert.True((await CrearAsync(new SupplierDTO { Name = nombre, TaxId = nit })).IsSuccess);

            Guid id;
            await using (DataContext lectura = _fixture.CrearContexto())
                id = (await lectura.Suppliers.SingleAsync(s => s.Name == nombre)).Id;

            Response<SupplierDTO> respuesta;
            await using (DataContext context = _fixture.CrearContexto())
                respuesta = await new SuppliersService(context).EditAsync(new SupplierDTO { Id = id, Name = nombre, TaxId = nit, Phone = "311" });

            Assert.True(respuesta.IsSuccess, respuesta.Message);
        }

        [Fact]
        public async Task Editar_proveedor_inexistente_no_crea_nada()
        {
            Guid id = Guid.NewGuid();

            Response<SupplierDTO> respuesta;
            await using (DataContext context = _fixture.CrearContexto())
                respuesta = await new SuppliersService(context).EditAsync(new SupplierDTO { Id = id, Name = Unico("Fantasma") });

            Assert.False(respuesta.IsSuccess);
            await using DataContext verificacion = _fixture.CrearContexto();
            Assert.False(await verificacion.Suppliers.AnyAsync(s => s.Id == id));
        }
    }
}
