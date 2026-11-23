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
    /// Registro de compras: suma el stock y guarda el último costo, todo o nada, sin duplicar
    /// por reintentos y con varios usuarios (y ventas) al mismo tiempo.
    /// </summary>
    [Collection(ColeccionBaseDeDatos.Nombre)]
    public class PurchasesServiceTests
    {
        private readonly BaseDeDatosFixture _fixture;

        public PurchasesServiceTests(BaseDeDatosFixture fixture)
        {
            _fixture = fixture;
        }

        // ---------- Ayudantes ----------

        private static PurchaseLineDTO Linea(Product producto, decimal cantidad, decimal costo)
            => new PurchaseLineDTO { ProductId = producto.Id, Quantity = cantidad, UnitCost = costo };

        private static CreatePurchaseDTO Compra(Guid proveedorId, params PurchaseLineDTO[] lineas) => new CreatePurchaseDTO
        {
            SupplierId = proveedorId,
            Items = lineas.ToList(),
            IdempotencyKey = Guid.NewGuid()
        };

        /// <summary>Cada llamada usa su propio contexto, como una petición web distinta.</summary>
        private async Task<Response<PurchaseResultDTO>> ComprarAsync(CreatePurchaseDTO compra, string? usuarioId = null)
        {
            await using DataContext context = _fixture.CrearContexto();
            return await new PurchasesService(context).CreateAsync(compra, usuarioId ?? _fixture.CajeroId);
        }

        private async Task<Guid> CrearProveedorAsync(bool activo = true)
        {
            await using DataContext context = _fixture.CrearContexto();
            Supplier proveedor = new Supplier
            {
                Id = Guid.NewGuid(),
                Name = $"Proveedor {Guid.NewGuid().ToString("N")[..8]}",
                IsActive = activo
            };
            context.Suppliers.Add(proveedor);
            await context.SaveChangesAsync();
            return proveedor.Id;
        }

        private async Task<(decimal stock, decimal? costo)> EstadoAsync(Guid productoId)
        {
            await using DataContext context = _fixture.CrearContexto();
            var producto = await context.Products.AsNoTracking()
                .Where(p => p.Id == productoId)
                .Select(p => new { p.Stock, p.Cost })
                .SingleAsync();
            return (producto.Stock, producto.Cost);
        }

        private async Task<int> ComprasConCodigoAsync(Guid codigo)
        {
            await using DataContext context = _fixture.CrearContexto();
            return await context.Purchases.CountAsync(p => p.IdempotencyKey == codigo);
        }

        // ---------- Compra normal ----------

        [Fact]
        public async Task Compra_simple_suma_stock_y_guarda_el_costo()
        {
            Guid proveedor = await CrearProveedorAsync();
            Product arroz = await _fixture.CrearProductoAsync("Arroz", 2500m, 4m);

            CreatePurchaseDTO dto = Compra(proveedor, Linea(arroz, 10m, 1800m));
            dto.SupplierInvoiceNumber = "  F-100  ";
            dto.Notes = "   ";

            Response<PurchaseResultDTO> respuesta = await ComprarAsync(dto);

            Assert.True(respuesta.IsSuccess, respuesta.Message);
            Assert.False(respuesta.Result!.AlreadyRegistered);
            Assert.True(respuesta.Result.Number > 0);
            Assert.Equal(18000m, respuesta.Result.Total);
            Assert.Null(respuesta.Result.Warning);

            (decimal stock, decimal? costo) = await EstadoAsync(arroz.Id);
            Assert.Equal(14m, stock);
            Assert.Equal(1800m, costo);

            await using DataContext verificacion = _fixture.CrearContexto();
            Purchase guardada = await verificacion.Purchases.Include(p => p.Items).SingleAsync(p => p.Id == respuesta.Result.Id);
            Assert.Equal(PurchaseStatus.Completed, guardada.Status);
            Assert.Equal(_fixture.CajeroId, guardada.UserId);
            Assert.Equal("F-100", guardada.SupplierInvoiceNumber);
            Assert.Null(guardada.Notes);
            PurchaseItem item = Assert.Single(guardada.Items);
            Assert.Equal(arroz.Name, item.ProductName);
            Assert.Equal(1800m, item.UnitCost);
            Assert.Equal(10m, item.Quantity);
            Assert.Equal(18000m, item.Total);
        }

        [Fact]
        public async Task Una_compra_nueva_cambia_el_ultimo_costo()
        {
            Guid proveedor = await CrearProveedorAsync();
            Product aceite = await _fixture.CrearProductoAsync("Aceite", 9000m, 0m);

            await ComprarAsync(Compra(proveedor, Linea(aceite, 5m, 6000m)));
            Response<PurchaseResultDTO> segunda = await ComprarAsync(Compra(proveedor, Linea(aceite, 5m, 6500m)));

            Assert.True(segunda.IsSuccess, segunda.Message);
            (decimal stock, decimal? costo) = await EstadoAsync(aceite.Id);
            Assert.Equal(10m, stock);
            Assert.Equal(6500m, costo);
        }

        [Fact]
        public async Task Lineas_repetidas_con_el_mismo_costo_se_unen()
        {
            Guid proveedor = await CrearProveedorAsync();
            Product leche = await _fixture.CrearProductoAsync("Leche", 1200m, 0m);

            Response<PurchaseResultDTO> respuesta = await ComprarAsync(
                Compra(proveedor, Linea(leche, 3m, 800m), Linea(leche, 2m, 800m)));

            Assert.True(respuesta.IsSuccess, respuesta.Message);
            Assert.Equal(4000m, respuesta.Result!.Total);
            Assert.Equal(5m, (await EstadoAsync(leche.Id)).stock);

            await using DataContext verificacion = _fixture.CrearContexto();
            PurchaseItem item = await verificacion.PurchaseItems.SingleAsync(i => i.PurchaseId == respuesta.Result.Id);
            Assert.Equal(5m, item.Quantity);
        }

        [Fact]
        public async Task Mismo_producto_con_costos_distintos_queda_en_lineas_separadas_y_gana_el_ultimo()
        {
            Guid proveedor = await CrearProveedorAsync();
            Product pan = await _fixture.CrearProductoAsync("Pan", 300m, 0m);

            Response<PurchaseResultDTO> respuesta = await ComprarAsync(
                Compra(proveedor, Linea(pan, 10m, 200m), Linea(pan, 10m, 250m)));

            Assert.True(respuesta.IsSuccess, respuesta.Message);
            Assert.Equal(4500m, respuesta.Result!.Total); // 10×200 + 10×250: ningún costo se pierde

            (decimal stock, decimal? costo) = await EstadoAsync(pan.Id);
            Assert.Equal(20m, stock);
            Assert.Equal(250m, costo); // el último ingresado

            await using DataContext verificacion = _fixture.CrearContexto();
            Assert.Equal(2, await verificacion.PurchaseItems.CountAsync(i => i.PurchaseId == respuesta.Result.Id));
        }

        [Fact]
        public async Task Producto_por_peso_acepta_decimales_y_por_unidad_no()
        {
            Guid proveedor = await CrearProveedorAsync();
            Product carne = await _fixture.CrearProductoAsync("Carne", 20000m, 0m, UnitOfMeasure.Kilogram);
            Product huevos = await _fixture.CrearProductoAsync("Huevos", 500m, 0m);

            Response<PurchaseResultDTO> porPeso = await ComprarAsync(Compra(proveedor, Linea(carne, 12.5m, 14000m)));
            Response<PurchaseResultDTO> porUnidad = await ComprarAsync(Compra(proveedor, Linea(huevos, 2.5m, 300m)));

            Assert.True(porPeso.IsSuccess, porPeso.Message);
            Assert.Equal(175000m, porPeso.Result!.Total);
            Assert.Equal(12.5m, (await EstadoAsync(carne.Id)).stock);

            Assert.False(porUnidad.IsSuccess);
            Assert.Contains("número entero", porUnidad.Message);
            Assert.Equal(0m, (await EstadoAsync(huevos.Id)).stock);
        }

        [Fact]
        public async Task Se_puede_comprar_un_producto_de_una_categoria_desactivada()
        {
            Guid proveedor = await CrearProveedorAsync();
            Product viejo = await _fixture.CrearProductoAsync("Descontinuado", 1000m, 0m,
                categoriaId: _fixture.CategoriaInactivaId);

            Response<PurchaseResultDTO> respuesta = await ComprarAsync(Compra(proveedor, Linea(viejo, 3m, 700m)));

            Assert.True(respuesta.IsSuccess, respuesta.Message);
            Assert.Equal(3m, (await EstadoAsync(viejo.Id)).stock);
        }

        // ---------- Validaciones ----------

        [Fact]
        public async Task Proveedor_inexistente_o_desactivado_se_rechaza()
        {
            Product sal = await _fixture.CrearProductoAsync("Sal", 1000m, 5m);
            Guid inactivo = await CrearProveedorAsync(activo: false);

            Response<PurchaseResultDTO> inexistente = await ComprarAsync(Compra(Guid.NewGuid(), Linea(sal, 1m, 500m)));
            Response<PurchaseResultDTO> desactivado = await ComprarAsync(Compra(inactivo, Linea(sal, 1m, 500m)));

            Assert.False(inexistente.IsSuccess);
            Assert.Contains("no existe", inexistente.Message);
            Assert.False(desactivado.IsSuccess);
            Assert.Contains("desactivado", desactivado.Message);
            Assert.Equal(5m, (await EstadoAsync(sal.Id)).stock);
        }

        [Fact]
        public async Task Producto_inexistente_se_rechaza()
        {
            Guid proveedor = await CrearProveedorAsync();

            Response<PurchaseResultDTO> respuesta = await ComprarAsync(Compra(proveedor,
                new PurchaseLineDTO { ProductId = Guid.NewGuid(), Quantity = 1m, UnitCost = 100m }));

            Assert.False(respuesta.IsSuccess);
            Assert.Contains("ya no existe", respuesta.Message);
        }

        [Theory]
        [InlineData(0, 100)]
        [InlineData(-1, 100)]
        [InlineData(1, 0)]
        [InlineData(1, -50)]
        [InlineData(1, 0.004)] // se redondea a 0.00
        [InlineData(0.0004, 100)] // se redondea a 0.000
        public async Task Cantidad_o_costo_que_no_son_mayores_que_cero_se_rechazan(double cantidad, double costo)
        {
            Guid proveedor = await CrearProveedorAsync();
            Product azucar = await _fixture.CrearProductoAsync("Azúcar", 3000m, 5m, UnitOfMeasure.Kilogram);

            Response<PurchaseResultDTO> respuesta = await ComprarAsync(
                Compra(proveedor, Linea(azucar, (decimal)cantidad, (decimal)costo)));

            Assert.False(respuesta.IsSuccess);
            Assert.Equal(5m, (await EstadoAsync(azucar.Id)).stock);
        }

        [Fact]
        public async Task Compra_sin_lineas_o_sin_codigo_se_rechaza()
        {
            Guid proveedor = await CrearProveedorAsync();
            Product cafe = await _fixture.CrearProductoAsync("Café", 8000m, 5m);

            Response<PurchaseResultDTO> sinLineas = await ComprarAsync(Compra(proveedor));

            CreatePurchaseDTO sinCodigo = Compra(proveedor, Linea(cafe, 1m, 5000m));
            sinCodigo.IdempotencyKey = Guid.Empty;
            Response<PurchaseResultDTO> respuestaSinCodigo = await ComprarAsync(sinCodigo);

            Assert.False(sinLineas.IsSuccess);
            Assert.Contains("al menos un producto", sinLineas.Message);
            Assert.False(respuestaSinCodigo.IsSuccess);
            Assert.Contains("Recarga la página", respuestaSinCodigo.Message);
            Assert.Equal(5m, (await EstadoAsync(cafe.Id)).stock);
        }

        [Fact]
        public async Task Factura_o_notas_demasiado_largas_se_rechazan()
        {
            Guid proveedor = await CrearProveedorAsync();
            Product te = await _fixture.CrearProductoAsync("Té", 2000m, 5m);

            CreatePurchaseDTO facturaLarga = Compra(proveedor, Linea(te, 1m, 1000m));
            facturaLarga.SupplierInvoiceNumber = new string('F', 51);

            CreatePurchaseDTO notasLargas = Compra(proveedor, Linea(te, 1m, 1000m));
            notasLargas.Notes = new string('N', 201);

            Assert.False((await ComprarAsync(facturaLarga)).IsSuccess);
            Assert.False((await ComprarAsync(notasLargas)).IsSuccess);
            Assert.Equal(5m, (await EstadoAsync(te.Id)).stock);
        }

        [Fact]
        public async Task Si_una_linea_falla_no_queda_nada_de_la_compra()
        {
            Guid proveedor = await CrearProveedorAsync();

            // Las líneas se procesan ordenadas por ProductId: la buena va primero y la mala después,
            // así se comprueba que lo ya sumado se deshace.
            Guid[] ids = { Guid.NewGuid(), Guid.NewGuid() };
            Array.Sort(ids);
            Product bueno = await _fixture.CrearProductoAsync("Bueno", 1000m, 2m, UnitOfMeasure.Kilogram, id: ids[0]);
            Product malo = await _fixture.CrearProductoAsync("Malo", 1000m, 2m, UnitOfMeasure.Unit, id: ids[1]);

            CreatePurchaseDTO dto = Compra(proveedor, Linea(bueno, 5m, 700m), Linea(malo, 1.5m, 700m));

            Response<PurchaseResultDTO> respuesta = await ComprarAsync(dto);

            Assert.False(respuesta.IsSuccess);
            Assert.Equal((2m, (decimal?)null), await EstadoAsync(bueno.Id));
            Assert.Equal((2m, (decimal?)null), await EstadoAsync(malo.Id));
            Assert.Equal(0, await ComprasConCodigoAsync(dto.IdempotencyKey));
        }

        [Fact]
        public async Task Factura_repetida_del_mismo_proveedor_avisa_pero_registra()
        {
            Guid proveedor = await CrearProveedorAsync();
            Guid otroProveedor = await CrearProveedorAsync();
            Product jabon = await _fixture.CrearProductoAsync("Jabón", 2000m, 0m);

            CreatePurchaseDTO primera = Compra(proveedor, Linea(jabon, 1m, 1000m));
            primera.SupplierInvoiceNumber = "FAC-77";
            CreatePurchaseDTO repetida = Compra(proveedor, Linea(jabon, 1m, 1000m));
            repetida.SupplierInvoiceNumber = "fac-77"; // sin distinguir mayúsculas
            CreatePurchaseDTO deOtro = Compra(otroProveedor, Linea(jabon, 1m, 1000m));
            deOtro.SupplierInvoiceNumber = "FAC-77";

            Response<PurchaseResultDTO> r1 = await ComprarAsync(primera);
            Response<PurchaseResultDTO> r2 = await ComprarAsync(repetida);
            Response<PurchaseResultDTO> r3 = await ComprarAsync(deOtro);

            Assert.Null(r1.Result!.Warning);
            Assert.True(r2.IsSuccess, r2.Message);
            Assert.NotNull(r2.Result!.Warning); // avisa, pero la compra quedó
            Assert.Contains("FAC-77", r2.Result.Warning, StringComparison.OrdinalIgnoreCase);
            Assert.Null(r3.Result!.Warning);    // otro proveedor: es otra factura
            Assert.Equal(3m, (await EstadoAsync(jabon.Id)).stock);
        }

        // ---------- Reintentos ----------

        [Fact]
        public async Task Mismo_codigo_dos_veces_registra_una_sola_compra()
        {
            Guid proveedor = await CrearProveedorAsync();
            Product harina = await _fixture.CrearProductoAsync("Harina", 3000m, 1m);
            CreatePurchaseDTO dto = Compra(proveedor, Linea(harina, 4m, 2000m));

            Response<PurchaseResultDTO> primera = await ComprarAsync(dto);
            Response<PurchaseResultDTO> reintento = await ComprarAsync(dto);

            Assert.True(primera.IsSuccess, primera.Message);
            Assert.False(primera.Result!.AlreadyRegistered);
            Assert.True(reintento.IsSuccess, reintento.Message);
            Assert.True(reintento.Result!.AlreadyRegistered);
            Assert.Equal(primera.Result.Id, reintento.Result.Id);
            Assert.Equal(primera.Result.Number, reintento.Result.Number);
            Assert.Equal(8000m, reintento.Result.Total);

            Assert.Equal(5m, (await EstadoAsync(harina.Id)).stock); // se sumó una sola vez
            Assert.Equal(1, await ComprasConCodigoAsync(dto.IdempotencyKey));
        }

        [Fact]
        public async Task Reintentos_simultaneos_con_el_mismo_codigo_registran_una_sola_compra()
        {
            Guid proveedor = await CrearProveedorAsync();
            Product lentejas = await _fixture.CrearProductoAsync("Lentejas", 4000m, 0m);
            CreatePurchaseDTO dto = Compra(proveedor, Linea(lentejas, 6m, 2500m));

            TaskCompletionSource salida = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            Task<Response<PurchaseResultDTO>>[] tareas = Enumerable.Range(0, 5)
                .Select(_ => Task.Run(async () =>
                {
                    await salida.Task;
                    return await ComprarAsync(dto);
                }))
                .ToArray();
            salida.SetResult();
            Response<PurchaseResultDTO>[] respuestas = await Task.WhenAll(tareas);

            Assert.All(respuestas, r => Assert.True(r.IsSuccess, r.Message));
            Assert.Single(respuestas.Select(r => r.Result!.Id).Distinct());
            Assert.Single(respuestas, r => !r.Result!.AlreadyRegistered);
            Assert.Equal(6m, (await EstadoAsync(lentejas.Id)).stock);
            Assert.Equal(1, await ComprasConCodigoAsync(dto.IdempotencyKey));
        }

        [Fact]
        public async Task Una_compra_que_fallo_no_gasta_el_codigo()
        {
            Guid proveedor = await CrearProveedorAsync();
            Product pasta = await _fixture.CrearProductoAsync("Pasta", 2000m, 0m);
            CreatePurchaseDTO dto = Compra(proveedor, Linea(pasta, 1.5m, 900m)); // decimales en producto por unidad

            Response<PurchaseResultDTO> fallida = await ComprarAsync(dto);
            Assert.False(fallida.IsSuccess);

            dto.Items[0].Quantity = 2m; // se corrige y se reenvía con el mismo código
            Response<PurchaseResultDTO> corregida = await ComprarAsync(dto);

            Assert.True(corregida.IsSuccess, corregida.Message);
            Assert.False(corregida.Result!.AlreadyRegistered);
            Assert.Equal(2m, (await EstadoAsync(pasta.Id)).stock);
        }

        [Fact]
        public async Task El_mismo_codigo_en_dos_usuarios_son_compras_distintas()
        {
            Guid proveedor = await CrearProveedorAsync();
            string otroUsuario = await _fixture.CrearCajeroAsync("Otro");
            Product frijol = await _fixture.CrearProductoAsync("Frijol", 5000m, 0m);
            CreatePurchaseDTO dto = Compra(proveedor, Linea(frijol, 1m, 3000m));

            Response<PurchaseResultDTO> deA = await ComprarAsync(dto, _fixture.CajeroId);
            Response<PurchaseResultDTO> deB = await ComprarAsync(dto, otroUsuario);

            Assert.True(deA.IsSuccess, deA.Message);
            Assert.True(deB.IsSuccess, deB.Message);
            Assert.False(deB.Result!.AlreadyRegistered);
            Assert.NotEqual(deA.Result!.Id, deB.Result.Id);
            Assert.Equal(2m, (await EstadoAsync(frijol.Id)).stock);
        }

        // ---------- Concurrencia ----------

        [Fact]
        public async Task Veinte_compras_simultaneas_del_mismo_producto_suman_exacto()
        {
            Guid proveedor = await CrearProveedorAsync();
            Product maiz = await _fixture.CrearProductoAsync("Maíz", 1500m, 0m);

            TaskCompletionSource salida = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            Task<Response<PurchaseResultDTO>>[] tareas = Enumerable.Range(0, 20)
                .Select(_ => Task.Run(async () =>
                {
                    await salida.Task;
                    return await ComprarAsync(Compra(proveedor, Linea(maiz, 1m, 900m)));
                }))
                .ToArray();
            salida.SetResult();
            Response<PurchaseResultDTO>[] respuestas = await Task.WhenAll(tareas);

            Assert.All(respuestas, r => Assert.True(r.IsSuccess, r.Message));
            Assert.Equal(20, respuestas.Select(r => r.Result!.Number).Distinct().Count());
            Assert.Equal(20m, (await EstadoAsync(maiz.Id)).stock);
        }

        [Fact]
        public async Task Compras_en_orden_distinto_sobre_dos_productos_no_se_bloquean_entre_si()
        {
            Guid proveedor = await CrearProveedorAsync();
            Product a = await _fixture.CrearProductoAsync("ProductoA", 1000m, 0m);
            Product b = await _fixture.CrearProductoAsync("ProductoB", 1000m, 0m);

            TaskCompletionSource salida = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            Task<Response<PurchaseResultDTO>>[] tareas = Enumerable.Range(0, 10)
                .Select(i => Task.Run(async () =>
                {
                    await salida.Task;
                    // Unas compras escriben A y B, otras B y A: el servicio las ordena igual.
                    CreatePurchaseDTO dto = i % 2 == 0
                        ? Compra(proveedor, Linea(a, 1m, 500m), Linea(b, 1m, 500m))
                        : Compra(proveedor, Linea(b, 1m, 500m), Linea(a, 1m, 500m));
                    return await ComprarAsync(dto);
                }))
                .ToArray();
            salida.SetResult();
            Response<PurchaseResultDTO>[] respuestas = await Task.WhenAll(tareas);

            Assert.All(respuestas, r => Assert.True(r.IsSuccess, r.Message));
            Assert.Equal(10m, (await EstadoAsync(a.Id)).stock);
            Assert.Equal(10m, (await EstadoAsync(b.Id)).stock);
        }

        [Fact]
        public async Task Compras_y_ventas_simultaneas_del_mismo_producto_cuadran()
        {
            Guid proveedor = await CrearProveedorAsync();
            Product gaseosa = await _fixture.CrearProductoAsync("Gaseosa", 3000m, 10m);

            TaskCompletionSource salida = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

            IEnumerable<Task<bool>> compras = Enumerable.Range(0, 5).Select(_ => Task.Run(async () =>
            {
                await salida.Task;
                return (await ComprarAsync(Compra(proveedor, Linea(gaseosa, 2m, 1800m)))).IsSuccess;
            }));

            IEnumerable<Task<bool>> ventas = Enumerable.Range(0, 5).Select(_ => Task.Run(async () =>
            {
                await salida.Task;
                await using DataContext context = _fixture.CrearContexto();
                Response<SaleResultDTO> venta = await new SalesService(context).CreateAsync(new CreateSaleDTO
                {
                    PaymentMethod = PaymentMethod.Cash,
                    Items = { new SaleLineDTO { ProductId = gaseosa.Id, Quantity = 1m } },
                    IdempotencyKey = Guid.NewGuid()
                }, _fixture.CajeroId);
                return venta.IsSuccess;
            }));

            Task<bool>[] todas = compras.Concat(ventas).ToArray();
            salida.SetResult();
            bool[] resultados = await Task.WhenAll(todas);

            Assert.All(resultados, ok => Assert.True(ok));
            Assert.Equal(15m, (await EstadoAsync(gaseosa.Id)).stock); // 10 + 5×2 − 5×1
        }
    }
}
