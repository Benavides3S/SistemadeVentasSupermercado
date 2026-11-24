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
    /// Anulación de compras: resta del inventario lo que la compra sumó, todo o nada, solo si el stock
    /// alcanza, sin límite de tiempo y sin restar dos veces aunque dos personas anulen a la vez.
    /// También: un proveedor con compras no se borra.
    /// </summary>
    [Collection(ColeccionBaseDeDatos.Nombre)]
    public class PurchaseCancellationTests
    {
        private readonly BaseDeDatosFixture _fixture;

        public PurchaseCancellationTests(BaseDeDatosFixture fixture)
        {
            _fixture = fixture;
        }

        // ---------- Ayudantes ----------

        private async Task<Guid> CrearProveedorAsync()
        {
            await using DataContext context = _fixture.CrearContexto();
            Supplier proveedor = new Supplier { Id = Guid.NewGuid(), Name = $"Proveedor {Guid.NewGuid().ToString("N")[..8]}" };
            context.Suppliers.Add(proveedor);
            await context.SaveChangesAsync();
            return proveedor.Id;
        }

        private static PurchaseLineDTO Linea(Product producto, decimal cantidad, decimal costo)
            => new PurchaseLineDTO { ProductId = producto.Id, Quantity = cantidad, UnitCost = costo };

        /// <summary>Registra una compra y devuelve su id (la prueba falla si no se pudo registrar).</summary>
        private async Task<Guid> ComprarAsync(Guid proveedorId, string? factura, params PurchaseLineDTO[] lineas)
        {
            await using DataContext context = _fixture.CrearContexto();
            Response<PurchaseResultDTO> respuesta = await new PurchasesService(context).CreateAsync(new CreatePurchaseDTO
            {
                SupplierId = proveedorId,
                SupplierInvoiceNumber = factura,
                Items = lineas.ToList(),
                IdempotencyKey = Guid.NewGuid()
            }, _fixture.CajeroId);
            Assert.True(respuesta.IsSuccess, respuesta.Message);
            return respuesta.Result!.Id;
        }

        private async Task<Response<PurchaseResultDTO>> AnularAsync(Guid compraId, string motivo = "Error de captura", string? usuarioId = null)
        {
            await using DataContext context = _fixture.CrearContexto();
            return await new PurchasesService(context).CancelAsync(compraId, new CancelPurchaseDTO { Reason = motivo },
                usuarioId ?? _fixture.CajeroId);
        }

        private async Task VenderAsync(Product producto, decimal cantidad)
        {
            await using DataContext context = _fixture.CrearContexto();
            Response<SaleResultDTO> venta = await new SalesService(context).CreateAsync(new CreateSaleDTO
            {
                PaymentMethod = PaymentMethod.Cash,
                Items = { new SaleLineDTO { ProductId = producto.Id, Quantity = cantidad } },
                IdempotencyKey = Guid.NewGuid()
            }, _fixture.CajeroId);
            Assert.True(venta.IsSuccess, venta.Message);
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

        private async Task<Purchase> LeerCompraAsync(Guid compraId)
        {
            await using DataContext context = _fixture.CrearContexto();
            return await context.Purchases.AsNoTracking().SingleAsync(p => p.Id == compraId);
        }

        // ---------- Anulación ----------

        [Fact]
        public async Task Anular_resta_el_stock_y_guarda_quien_cuando_y_por_que()
        {
            Guid proveedor = await CrearProveedorAsync();
            Product arroz = await _fixture.CrearProductoAsync("Arroz", 2500m, 4m);
            Guid compra = await ComprarAsync(proveedor, null, Linea(arroz, 10m, 1800m));
            string supervisor = await _fixture.CrearCajeroAsync("Supervisor");

            Response<PurchaseResultDTO> respuesta = await AnularAsync(compra, "  Mercancía mal recibida  ", supervisor);

            Assert.True(respuesta.IsSuccess, respuesta.Message);
            Assert.Equal(compra, respuesta.Result!.Id);

            Purchase guardada = await LeerCompraAsync(compra);
            Assert.Equal(PurchaseStatus.Cancelled, guardada.Status);
            Assert.Equal("Mercancía mal recibida", guardada.CancelReason);
            Assert.Equal(supervisor, guardada.CancelledByUserId);
            Assert.NotNull(guardada.CancelledAtUtc);

            (decimal stock, decimal? costo) = await EstadoAsync(arroz.Id);
            Assert.Equal(4m, stock);      // volvió al stock de antes de la compra
            Assert.Null(costo);           // era la única compra: el último costo vuelve a quedar vacío
        }

        [Fact]
        public async Task Anular_una_compra_con_varias_lineas_resta_todas()
        {
            Guid proveedor = await CrearProveedorAsync();
            Product a = await _fixture.CrearProductoAsync("ProductoA", 1000m, 1m);
            Product b = await _fixture.CrearProductoAsync("ProductoB", 1000m, 2m, UnitOfMeasure.Kilogram);
            Guid compra = await ComprarAsync(proveedor, null, Linea(a, 5m, 500m), Linea(b, 2.5m, 700m), Linea(a, 3m, 550m));

            Response<PurchaseResultDTO> respuesta = await AnularAsync(compra);

            Assert.True(respuesta.IsSuccess, respuesta.Message);
            Assert.Equal(1m, (await EstadoAsync(a.Id)).stock);
            Assert.Equal(2m, (await EstadoAsync(b.Id)).stock);
        }

        [Fact]
        public async Task Anular_sin_motivo_o_con_motivo_largo_se_rechaza()
        {
            Guid proveedor = await CrearProveedorAsync();
            Product sal = await _fixture.CrearProductoAsync("Sal", 1000m, 0m);
            Guid compra = await ComprarAsync(proveedor, null, Linea(sal, 5m, 400m));

            Response<PurchaseResultDTO> sinMotivo = await AnularAsync(compra, "   ");
            Response<PurchaseResultDTO> largo = await AnularAsync(compra, new string('x', 201));

            Assert.False(sinMotivo.IsSuccess);
            Assert.Contains("obligatorio", sinMotivo.Message);
            Assert.False(largo.IsSuccess);
            Assert.Equal(PurchaseStatus.Completed, (await LeerCompraAsync(compra)).Status);
            Assert.Equal(5m, (await EstadoAsync(sal.Id)).stock);
        }

        [Fact]
        public async Task Compra_inexistente_no_se_puede_anular()
        {
            Response<PurchaseResultDTO> respuesta = await AnularAsync(Guid.NewGuid());

            Assert.False(respuesta.IsSuccess);
            Assert.Contains("no existe", respuesta.Message);
        }

        [Fact]
        public async Task Una_compra_ya_anulada_no_se_anula_dos_veces()
        {
            Guid proveedor = await CrearProveedorAsync();
            Product leche = await _fixture.CrearProductoAsync("Leche", 1200m, 10m);
            Guid compra = await ComprarAsync(proveedor, null, Linea(leche, 5m, 800m));

            Response<PurchaseResultDTO> primera = await AnularAsync(compra);
            Response<PurchaseResultDTO> segunda = await AnularAsync(compra);

            Assert.True(primera.IsSuccess, primera.Message);
            Assert.False(segunda.IsSuccess);
            Assert.Contains("ya está anulada", segunda.Message);
            Assert.Equal(10m, (await EstadoAsync(leche.Id)).stock); // se restó una sola vez
        }

        [Fact]
        public async Task Anulaciones_simultaneas_restan_el_stock_una_sola_vez()
        {
            Guid proveedor = await CrearProveedorAsync();
            Product aceite = await _fixture.CrearProductoAsync("Aceite", 9000m, 20m);
            Guid compra = await ComprarAsync(proveedor, null, Linea(aceite, 5m, 6000m));

            TaskCompletionSource salida = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            Task<Response<PurchaseResultDTO>>[] tareas = Enumerable.Range(0, 5)
                .Select(_ => Task.Run(async () =>
                {
                    await salida.Task;
                    return await AnularAsync(compra);
                }))
                .ToArray();
            salida.SetResult();
            Response<PurchaseResultDTO>[] respuestas = await Task.WhenAll(tareas);

            Assert.Single(respuestas, r => r.IsSuccess);
            Assert.Equal(4, respuestas.Count(r => !r.IsSuccess));
            Assert.Equal(20m, (await EstadoAsync(aceite.Id)).stock);
        }

        // ---------- Cuando el stock no alcanza ----------

        [Fact]
        public async Task Si_parte_de_lo_comprado_ya_se_vendio_la_anulacion_se_rechaza_sin_cambios()
        {
            Guid proveedor = await CrearProveedorAsync();

            // Las líneas se procesan ordenadas por ProductId: la que alcanza va primero y la que no
            // después, así se comprueba que lo ya restado se deshace.
            Guid[] ids = { Guid.NewGuid(), Guid.NewGuid() };
            Array.Sort(ids);
            Product alcanza = await _fixture.CrearProductoAsync("Alcanza", 1000m, 0m, id: ids[0]);
            Product noAlcanza = await _fixture.CrearProductoAsync("NoAlcanza", 1000m, 0m, id: ids[1]);
            Guid compra = await ComprarAsync(proveedor, null, Linea(alcanza, 5m, 500m), Linea(noAlcanza, 5m, 500m));

            await VenderAsync(noAlcanza, 3m); // quedan 2 de 5

            Response<PurchaseResultDTO> respuesta = await AnularAsync(compra);

            Assert.False(respuesta.IsSuccess);
            Assert.Contains(noAlcanza.Name, respuesta.Message);
            Assert.Contains("La compra sigue vigente", respuesta.Message);

            Purchase guardada = await LeerCompraAsync(compra);
            Assert.Equal(PurchaseStatus.Completed, guardada.Status);
            Assert.Null(guardada.CancelReason);
            Assert.Null(guardada.CancelledByUserId);
            Assert.Equal(5m, (await EstadoAsync(alcanza.Id)).stock);   // no quedó a medias
            Assert.Equal(2m, (await EstadoAsync(noAlcanza.Id)).stock);
        }

        [Fact]
        public async Task Se_puede_anular_aunque_se_haya_vendido_algo_si_el_stock_todavia_alcanza()
        {
            Guid proveedor = await CrearProveedorAsync();
            Product pan = await _fixture.CrearProductoAsync("Pan", 300m, 10m);
            Guid compra = await ComprarAsync(proveedor, null, Linea(pan, 5m, 200m)); // stock 15

            await VenderAsync(pan, 8m); // stock 7, y la compra pide restar 5

            Response<PurchaseResultDTO> respuesta = await AnularAsync(compra);

            Assert.True(respuesta.IsSuccess, respuesta.Message);
            Assert.Equal(2m, (await EstadoAsync(pan.Id)).stock);
        }

        [Fact]
        public async Task Anulacion_y_venta_simultaneas_nunca_dejan_el_stock_negativo()
        {
            Guid proveedor = await CrearProveedorAsync();
            Product harina = await _fixture.CrearProductoAsync("Harina", 3000m, 0m);
            Guid compra = await ComprarAsync(proveedor, null, Linea(harina, 5m, 2000m)); // stock 5

            TaskCompletionSource salida = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

            Task<bool> anulacion = Task.Run(async () =>
            {
                await salida.Task;
                return (await AnularAsync(compra)).IsSuccess;
            });

            Task<bool> venta = Task.Run(async () =>
            {
                await salida.Task;
                await using DataContext context = _fixture.CrearContexto();
                Response<SaleResultDTO> r = await new SalesService(context).CreateAsync(new CreateSaleDTO
                {
                    PaymentMethod = PaymentMethod.Cash,
                    Items = { new SaleLineDTO { ProductId = harina.Id, Quantity = 5m } },
                    IdempotencyKey = Guid.NewGuid()
                }, _fixture.CajeroId);
                return r.IsSuccess;
            });

            salida.SetResult();
            bool anulo = await anulacion;
            bool vendio = await venta;

            // Los 5 en inventario solo alcanzan para una de las dos operaciones.
            Assert.True(anulo ^ vendio, $"anuló={anulo}, vendió={vendio}: debía ganar exactamente una");
            Assert.Equal(0m, (await EstadoAsync(harina.Id)).stock);
            Assert.Equal(anulo ? PurchaseStatus.Cancelled : PurchaseStatus.Completed, (await LeerCompraAsync(compra)).Status);
        }

        // ---------- Último costo ----------

        [Fact]
        public async Task Anular_la_unica_compra_deja_el_costo_vacio()
        {
            Guid proveedor = await CrearProveedorAsync();
            Product sal = await _fixture.CrearProductoAsync("Sal", 1000m, 0m);
            Guid compra = await ComprarAsync(proveedor, null, Linea(sal, 5m, 700m));

            Assert.True((await AnularAsync(compra)).IsSuccess);

            Assert.Null((await EstadoAsync(sal.Id)).costo);
        }

        [Fact]
        public async Task Anular_la_ultima_compra_devuelve_el_costo_de_la_anterior()
        {
            Guid proveedor = await CrearProveedorAsync();
            Product aceite = await _fixture.CrearProductoAsync("Aceite", 9000m, 0m);
            await ComprarAsync(proveedor, null, Linea(aceite, 5m, 6000m));
            Guid segunda = await ComprarAsync(proveedor, null, Linea(aceite, 5m, 6500m));

            Assert.Equal(6500m, (await EstadoAsync(aceite.Id)).costo);
            Assert.True((await AnularAsync(segunda)).IsSuccess);

            Assert.Equal(6000m, (await EstadoAsync(aceite.Id)).costo);
        }

        [Fact]
        public async Task Anular_una_compra_vieja_no_pisa_el_costo_de_una_compra_posterior()
        {
            Guid proveedor = await CrearProveedorAsync();
            Product harina = await _fixture.CrearProductoAsync("Harina", 3000m, 0m);
            Guid primera = await ComprarAsync(proveedor, null, Linea(harina, 5m, 2000m));
            await ComprarAsync(proveedor, null, Linea(harina, 5m, 2200m));

            Assert.True((await AnularAsync(primera)).IsSuccess);

            Assert.Equal(2200m, (await EstadoAsync(harina.Id)).costo);
        }

        [Fact]
        public async Task Las_compras_anuladas_no_cuentan_como_costo_anterior()
        {
            Guid proveedor = await CrearProveedorAsync();
            Product pasta = await _fixture.CrearProductoAsync("Pasta", 3000m, 0m);
            await ComprarAsync(proveedor, null, Linea(pasta, 5m, 1000m));
            Guid segunda = await ComprarAsync(proveedor, null, Linea(pasta, 5m, 1100m));
            Guid tercera = await ComprarAsync(proveedor, null, Linea(pasta, 5m, 1200m));

            Assert.True((await AnularAsync(segunda)).IsSuccess);
            Assert.Equal(1200m, (await EstadoAsync(pasta.Id)).costo); // la tercera sigue siendo la última

            Assert.True((await AnularAsync(tercera)).IsSuccess);
            Assert.Equal(1000m, (await EstadoAsync(pasta.Id)).costo); // salta la anulada y vuelve a la primera
        }

        [Fact]
        public async Task Anular_no_toca_el_costo_de_un_producto_cuyo_costo_ya_no_es_el_de_esa_compra()
        {
            Guid proveedor = await CrearProveedorAsync();
            Product atun = await _fixture.CrearProductoAsync("Atún", 5000m, 0m);
            Guid compra = await ComprarAsync(proveedor, null, Linea(atun, 5m, 3000m));

            await using (DataContext context = _fixture.CrearContexto())
                await context.Products.Where(p => p.Id == atun.Id)
                    .ExecuteUpdateAsync(s => s.SetProperty(p => p.Cost, (decimal?)3500m));

            Assert.True((await AnularAsync(compra)).IsSuccess);

            Assert.Equal(3500m, (await EstadoAsync(atun.Id)).costo);
        }

        [Fact]
        public async Task Una_anulacion_rechazada_no_cambia_el_costo()
        {
            Guid proveedor = await CrearProveedorAsync();
            Product jugo = await _fixture.CrearProductoAsync("Jugo", 4000m, 0m);
            await ComprarAsync(proveedor, null, Linea(jugo, 5m, 2000m));
            Guid segunda = await ComprarAsync(proveedor, null, Linea(jugo, 5m, 2500m));
            await VenderAsync(jugo, 8m); // quedan 2: no alcanza para restar 5

            Assert.False((await AnularAsync(segunda)).IsSuccess);

            Assert.Equal(2500m, (await EstadoAsync(jugo.Id)).costo);
        }

        // ---------- Otras reglas ----------

        [Fact]
        public async Task La_anulacion_no_tiene_limite_de_tiempo()
        {
            Guid proveedor = await CrearProveedorAsync();
            Product te = await _fixture.CrearProductoAsync("Té", 2000m, 0m);
            Guid compra = await ComprarAsync(proveedor, null, Linea(te, 4m, 1000m));

            await using (DataContext context = _fixture.CrearContexto())
            {
                DateTime vieja = new DateTime(2020, 3, 1, 12, 0, 0, DateTimeKind.Utc);
                await context.Purchases.Where(p => p.Id == compra)
                    .ExecuteUpdateAsync(s => s.SetProperty(p => p.CreatedAtUtc, vieja));
            }

            Response<PurchaseResultDTO> respuesta = await AnularAsync(compra);

            Assert.True(respuesta.IsSuccess, respuesta.Message);
            Assert.Equal(0m, (await EstadoAsync(te.Id)).stock);
        }

        [Fact]
        public async Task Una_compra_anulada_ya_no_cuenta_para_el_aviso_de_factura_repetida()
        {
            Guid proveedor = await CrearProveedorAsync();
            Product jabon = await _fixture.CrearProductoAsync("Jabón", 2000m, 0m);
            Guid primera = await ComprarAsync(proveedor, "FAC-9", Linea(jabon, 1m, 1000m));
            Assert.True((await AnularAsync(primera)).IsSuccess);

            await using DataContext context = _fixture.CrearContexto();
            Response<PurchaseResultDTO> segunda = await new PurchasesService(context).CreateAsync(new CreatePurchaseDTO
            {
                SupplierId = proveedor,
                SupplierInvoiceNumber = "FAC-9",
                Items = { Linea(jabon, 1m, 1000m) },
                IdempotencyKey = Guid.NewGuid()
            }, _fixture.CajeroId);

            Assert.True(segunda.IsSuccess, segunda.Message);
            Assert.Null(segunda.Result!.Warning); // la anterior está anulada: no es un duplicado
        }

        [Fact]
        public async Task Reintentar_una_compra_ya_anulada_no_vuelve_a_sumar_el_stock()
        {
            Guid proveedor = await CrearProveedorAsync();
            Product maiz = await _fixture.CrearProductoAsync("Maíz", 1500m, 0m);
            Guid codigo = Guid.NewGuid();
            CreatePurchaseDTO dto = new CreatePurchaseDTO
            {
                SupplierId = proveedor,
                Items = { Linea(maiz, 6m, 900m) },
                IdempotencyKey = codigo
            };

            await using (DataContext context = _fixture.CrearContexto())
            {
                Assert.True((await new PurchasesService(context).CreateAsync(dto, _fixture.CajeroId)).IsSuccess);
            }

            Guid compra;
            await using (DataContext context = _fixture.CrearContexto())
            {
                compra = await context.Purchases.Where(p => p.IdempotencyKey == codigo).Select(p => p.Id).SingleAsync();
            }
            Assert.True((await AnularAsync(compra)).IsSuccess);

            // Llega tarde un reintento del mismo envío: se reconoce y no se suma nada.
            await using DataContext contextoReintento = _fixture.CrearContexto();
            Response<PurchaseResultDTO> reintento = await new PurchasesService(contextoReintento).CreateAsync(dto, _fixture.CajeroId);

            Assert.True(reintento.IsSuccess, reintento.Message);
            Assert.True(reintento.Result!.AlreadyRegistered);
            Assert.Equal(0m, (await EstadoAsync(maiz.Id)).stock);
        }

        // ---------- Proveedores con compras ----------

        [Fact]
        public async Task Un_proveedor_con_compras_no_se_puede_borrar()
        {
            Guid proveedor = await CrearProveedorAsync();
            Product cafe = await _fixture.CrearProductoAsync("Café", 8000m, 0m);
            await ComprarAsync(proveedor, null, Linea(cafe, 2m, 5000m));

            await using DataContext context = _fixture.CrearContexto();
            Response<object> respuesta = await new SuppliersService(context).DeleteAsync(proveedor);

            Assert.False(respuesta.IsSuccess);
            Assert.Contains("Desactívalo", respuesta.Message);

            await using DataContext verificacion = _fixture.CrearContexto();
            Assert.True(await verificacion.Suppliers.AnyAsync(s => s.Id == proveedor));
        }

        [Fact]
        public async Task Un_proveedor_sin_compras_si_se_puede_borrar()
        {
            Guid proveedor = await CrearProveedorAsync();

            await using DataContext context = _fixture.CrearContexto();
            Response<object> respuesta = await new SuppliersService(context).DeleteAsync(proveedor);

            Assert.True(respuesta.IsSuccess, respuesta.Message);

            await using DataContext verificacion = _fixture.CrearContexto();
            Assert.False(await verificacion.Suppliers.AnyAsync(s => s.Id == proveedor));
        }
    }
}
