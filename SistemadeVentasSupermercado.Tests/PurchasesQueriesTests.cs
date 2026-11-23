using Microsoft.EntityFrameworkCore;
using SistemadeVentasSupermercado.Tests.Infraestructura;
using SistemadeVentasSupermercado.Web.Core;
using SistemadeVentasSupermercado.Web.Core.Pagination;
using SistemadeVentasSupermercado.Web.Data;
using SistemadeVentasSupermercado.Web.Data.Entities;
using SistemadeVentasSupermercado.Web.Data.Seeders;
using SistemadeVentasSupermercado.Web.DTOs;
using SistemadeVentasSupermercado.Web.Services.Implementations;

namespace SistemadeVentasSupermercado.Tests
{
    /// <summary>
    /// Consultas de compras: historial, detalle, buscadores de producto (sin filtrar por stock ni categoría)
    /// y permisos del módulo.
    /// </summary>
    [Collection(ColeccionBaseDeDatos.Nombre)]
    public class PurchasesQueriesTests
    {
        private readonly BaseDeDatosFixture _fixture;

        public PurchasesQueriesTests(BaseDeDatosFixture fixture)
        {
            _fixture = fixture;
        }

        // ---------- Ayudantes ----------

        private async Task<(Guid id, string nombre)> CrearProveedorAsync()
        {
            await using DataContext context = _fixture.CrearContexto();
            Supplier proveedor = new Supplier { Id = Guid.NewGuid(), Name = $"Proveedor {Guid.NewGuid().ToString("N")[..8]}" };
            context.Suppliers.Add(proveedor);
            await context.SaveChangesAsync();
            return (proveedor.Id, proveedor.Name);
        }

        private async Task<T> ConServicioAsync<T>(Func<PurchasesService, Task<T>> accion)
        {
            await using DataContext context = _fixture.CrearContexto();
            return await accion(new PurchasesService(context));
        }

        private async Task<Response<PurchaseResultDTO>> ComprarAsync(Guid proveedorId, string? factura, params PurchaseLineDTO[] lineas)
        {
            Response<PurchaseResultDTO> respuesta = await ConServicioAsync(s => s.CreateAsync(new CreatePurchaseDTO
            {
                SupplierId = proveedorId,
                SupplierInvoiceNumber = factura,
                Notes = "Notas de prueba",
                Items = lineas.ToList(),
                IdempotencyKey = Guid.NewGuid()
            }, _fixture.CajeroId));
            Assert.True(respuesta.IsSuccess, respuesta.Message);
            return respuesta;
        }

        private static PurchaseLineDTO Linea(Product producto, decimal cantidad, decimal costo)
            => new PurchaseLineDTO { ProductId = producto.Id, Quantity = cantidad, UnitCost = costo };

        // ---------- Historial ----------

        [Fact]
        public async Task El_historial_muestra_la_compra_mas_reciente_primero_y_pagina()
        {
            (Guid proveedor, string nombre) = await CrearProveedorAsync();
            Product producto = await _fixture.CrearProductoAsync("Historial", 1000m, 0m);
            Response<PurchaseResultDTO> primera = await ComprarAsync(proveedor, "H-1", Linea(producto, 1m, 500m));
            Response<PurchaseResultDTO> segunda = await ComprarAsync(proveedor, "H-2", Linea(producto, 1m, 500m));
            Response<PurchaseResultDTO> tercera = await ComprarAsync(proveedor, "H-3", Linea(producto, 1m, 500m));

            var pagina1 = await ConServicioAsync(s => s.GetPaginatedListAsync(
                new PaginationRequest { Filter = nombre, Page = 1, RecordsPerPage = 2 }));
            var pagina2 = await ConServicioAsync(s => s.GetPaginatedListAsync(
                new PaginationRequest { Filter = nombre, Page = 2, RecordsPerPage = 2 }));

            Assert.True(pagina1.IsSuccess, pagina1.Message);
            Assert.Equal(3, pagina1.Result!.TotalCount);
            Assert.Equal(2, pagina1.Result.TotalPages);
            Assert.Equal(new[] { tercera.Result!.Number, segunda.Result!.Number }, pagina1.Result.List!.Select(p => p.Number));
            Assert.Equal(new[] { primera.Result!.Number }, pagina2.Result!.List!.Select(p => p.Number));

            PurchaseListItemDTO fila = pagina1.Result.List!.First();
            Assert.Equal(nombre, fila.SupplierName);
            Assert.Equal("H-3", fila.SupplierInvoiceNumber);
            Assert.Equal("Cajero Pruebas", fila.UserName);
            Assert.Equal(PurchaseStatus.Completed, fila.Status);
            Assert.Equal(500m, fila.Total);
        }

        [Fact]
        public async Task El_historial_filtra_por_numero_exacto_proveedor_y_factura()
        {
            (Guid proveedorA, string nombreA) = await CrearProveedorAsync();
            (Guid proveedorB, _) = await CrearProveedorAsync();
            Product producto = await _fixture.CrearProductoAsync("Filtro", 1000m, 0m);
            string facturaUnica = $"FU-{Guid.NewGuid().ToString("N")[..8]}";
            Response<PurchaseResultDTO> deA = await ComprarAsync(proveedorA, facturaUnica, Linea(producto, 1m, 500m));
            await ComprarAsync(proveedorB, null, Linea(producto, 1m, 500m));

            var porNumero = await ConServicioAsync(s => s.GetPaginatedListAsync(
                new PaginationRequest { Filter = deA.Result!.Number.ToString() }));
            var porProveedor = await ConServicioAsync(s => s.GetPaginatedListAsync(new PaginationRequest { Filter = nombreA }));
            var porFactura = await ConServicioAsync(s => s.GetPaginatedListAsync(
                new PaginationRequest { Filter = facturaUnica.ToLower() }));

            Assert.Equal(deA.Result.Number, Assert.Single(porNumero.Result!.List!).Number);
            Assert.Equal(deA.Result.Number, Assert.Single(porProveedor.Result!.List!).Number);
            Assert.Equal(deA.Result.Number, Assert.Single(porFactura.Result!.List!).Number);
        }

        // ---------- Detalle ----------

        [Fact]
        public async Task El_detalle_trae_las_lineas_y_permite_anular_solo_mientras_esta_completada()
        {
            (Guid proveedor, string nombre) = await CrearProveedorAsync();
            Product arroz = await _fixture.CrearProductoAsync("Arroz", 2500m, 0m, UnitOfMeasure.Kilogram);
            Product pan = await _fixture.CrearProductoAsync("Pan", 300m, 0m);
            Response<PurchaseResultDTO> compra = await ComprarAsync(proveedor, "D-1",
                Linea(arroz, 12.5m, 1800m), Linea(pan, 10m, 200m));
            Guid id = compra.Result!.Id;

            Response<PurchaseDetailDTO> completa = await ConServicioAsync(s => s.GetOneAsync(id));

            Assert.True(completa.IsSuccess, completa.Message);
            PurchaseDetailDTO detalle = completa.Result!;
            Assert.Equal(nombre, detalle.SupplierName);
            Assert.Equal("D-1", detalle.SupplierInvoiceNumber);
            Assert.Equal("Notas de prueba", detalle.Notes);
            Assert.Equal("Cajero Pruebas", detalle.UserName);
            Assert.Equal(24500m, detalle.Total); // 12.5×1800 + 10×200
            Assert.True(detalle.CanBeCancelled);
            Assert.Null(detalle.CancelledAtUtc);
            Assert.Equal(2, detalle.Items.Count);
            PurchaseDetailLineDTO lineaArroz = detalle.Items.Single(i => i.ProductName == arroz.Name);
            Assert.Equal(12.5m, lineaArroz.Quantity);
            Assert.Equal(1800m, lineaArroz.UnitCost);
            Assert.Equal(22500m, lineaArroz.Total);
            Assert.Equal(UnitOfMeasure.Kilogram, lineaArroz.Unit);

            await ConServicioAsync(s => s.CancelAsync(id, new CancelPurchaseDTO { Reason = "Prueba" }, _fixture.CajeroId));
            PurchaseDetailDTO anulada = (await ConServicioAsync(s => s.GetOneAsync(id))).Result!;

            Assert.Equal(PurchaseStatus.Cancelled, anulada.Status);
            Assert.False(anulada.CanBeCancelled);
            Assert.Equal("Prueba", anulada.CancelReason);
            Assert.Equal("Cajero Pruebas", anulada.CancelledByName);
            Assert.NotNull(anulada.CancelledAtUtc);
        }

        [Fact]
        public async Task Una_compra_inexistente_no_tiene_detalle()
        {
            Response<PurchaseDetailDTO> respuesta = await ConServicioAsync(s => s.GetOneAsync(Guid.NewGuid()));

            Assert.False(respuesta.IsSuccess);
            Assert.Contains("no existe", respuesta.Message);
        }

        // ---------- Buscadores ----------

        [Fact]
        public async Task El_buscador_incluye_productos_sin_stock_y_de_categorias_desactivadas()
        {
            string marca = Guid.NewGuid().ToString("N")[..8];
            Product sinStock = await _fixture.CrearProductoAsync($"Agotado {marca}", 1000m, 0m);
            Product inactivo = await _fixture.CrearProductoAsync($"Descontinuado {marca}", 1000m, 5m,
                categoriaId: _fixture.CategoriaInactivaId);
            Product peso = await _fixture.CrearProductoAsync($"Carne {marca}", 20000m, 3m, UnitOfMeasure.Kilogram);

            Response<List<PurchaseProductDTO>> respuesta = await ConServicioAsync(s => s.SearchProductsAsync(marca));

            Assert.True(respuesta.IsSuccess, respuesta.Message);
            Assert.Equal(3, respuesta.Result!.Count);
            Assert.Contains(respuesta.Result, p => p.Id == sinStock.Id);
            Assert.Contains(respuesta.Result, p => p.Id == inactivo.Id);

            PurchaseProductDTO carne = respuesta.Result.Single(p => p.Id == peso.Id);
            Assert.True(carne.AllowsDecimals);
            Assert.Equal("kg", carne.UnitAbbreviation);
            Assert.Equal(3m, carne.Stock);
            Assert.Null(carne.Cost); // nunca se ha comprado

            PurchaseProductDTO unidad = respuesta.Result.Single(p => p.Id == sinStock.Id);
            Assert.False(unidad.AllowsDecimals);
        }

        [Fact]
        public async Task El_buscador_devuelve_el_ultimo_costo_y_busca_por_codigo()
        {
            (Guid proveedor, _) = await CrearProveedorAsync();
            string codigo = $"CC{Guid.NewGuid().ToString("N")[..10]}";
            Product producto = await _fixture.CrearProductoAsync("ConCosto", 1000m, 0m, codigo: codigo);
            await ComprarAsync(proveedor, null, Linea(producto, 2m, 650m));

            Response<List<PurchaseProductDTO>> porCodigo = await ConServicioAsync(s => s.SearchProductsAsync(codigo));

            PurchaseProductDTO encontrado = Assert.Single(porCodigo.Result!);
            Assert.Equal(producto.Id, encontrado.Id);
            Assert.Equal(650m, encontrado.Cost);
            Assert.Equal(codigo, encontrado.Barcode);
        }

        [Fact]
        public async Task El_buscador_con_texto_vacio_no_devuelve_nada()
        {
            Response<List<PurchaseProductDTO>> respuesta = await ConServicioAsync(s => s.SearchProductsAsync("   "));

            Assert.True(respuesta.IsSuccess);
            Assert.Empty(respuesta.Result!);
        }

        [Fact]
        public async Task El_lector_encuentra_el_codigo_exacto_aunque_el_producto_no_tenga_stock()
        {
            string codigo = $"LC{Guid.NewGuid().ToString("N")[..10]}";
            Product producto = await _fixture.CrearProductoAsync("Escaneado", 1000m, 0m, codigo: codigo,
                categoriaId: _fixture.CategoriaInactivaId);

            Response<PurchaseProductDTO?> encontrado = await ConServicioAsync(s => s.FindProductByBarcodeAsync($"  {codigo}  "));
            Response<PurchaseProductDTO?> inexistente = await ConServicioAsync(s => s.FindProductByBarcodeAsync("NOEXISTE999"));
            Response<PurchaseProductDTO?> invalido = await ConServicioAsync(s => s.FindProductByBarcodeAsync("a b;c"));
            Response<PurchaseProductDTO?> vacio = await ConServicioAsync(s => s.FindProductByBarcodeAsync(null));

            Assert.True(encontrado.IsSuccess, encontrado.Message);
            Assert.Equal(producto.Id, encontrado.Result!.Id);

            Assert.True(inexistente.IsSuccess);
            Assert.Null(inexistente.Result);
            Assert.Contains("NOEXISTE999", inexistente.Message);

            Assert.True(invalido.IsSuccess);
            Assert.Null(invalido.Result);

            Assert.True(vacio.IsSuccess);
            Assert.Null(vacio.Result);
        }

        // ---------- Permisos ----------

        [Fact]
        public async Task El_seeder_crea_los_permisos_del_modulo_Compras()
        {
            await using (DataContext context = _fixture.CrearContexto())
                await new PermissionsSeeder(context).SeedAsync();

            await using DataContext verificar = _fixture.CrearContexto();
            List<string> nombres = await verificar.Permissions
                .Where(p => p.Module == "Compras")
                .Select(p => p.Name)
                .OrderBy(n => n)
                .ToListAsync();

            Assert.Equal(new[] { "cancelPurchases", "createPurchases", "showPurchases" }, nombres);
        }

        [Fact]
        public void El_Supervisor_nace_con_los_permisos_de_compras_y_el_Cajero_no()
        {
            Assert.Contains("showPurchases", UserRolesSeeder.SupervisorPermissions);
            Assert.Contains("createPurchases", UserRolesSeeder.SupervisorPermissions);
            Assert.Contains("cancelPurchases", UserRolesSeeder.SupervisorPermissions);
            Assert.DoesNotContain(UserRolesSeeder.CashierPermissions, p => p.EndsWith("Purchases"));
        }
    }
}
