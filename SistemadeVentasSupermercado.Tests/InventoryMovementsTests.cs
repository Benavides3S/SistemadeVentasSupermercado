using Microsoft.EntityFrameworkCore;
using SistemadeVentasSupermercado.Tests.Infraestructura;
using SistemadeVentasSupermercado.Web.Core;
using SistemadeVentasSupermercado.Web.Data;
using SistemadeVentasSupermercado.Web.Data.Entities;
using SistemadeVentasSupermercado.Web.Data.Seeders;
using SistemadeVentasSupermercado.Web.DTOs;
using SistemadeVentasSupermercado.Web.Services.Implementations;

namespace SistemadeVentasSupermercado.Tests
{
    /// <summary>
    /// Historial de inventario: cada operación que cambia el stock deja su movimiento (tipo, cantidad con
    /// signo, stock después, usuario y referencia) dentro de la misma transacción, y si la operación falla
    /// no queda nada.
    /// </summary>
    [Collection(ColeccionBaseDeDatos.Nombre)]
    public class InventoryMovementsTests
    {
        private readonly BaseDeDatosFixture _fixture;

        public InventoryMovementsTests(BaseDeDatosFixture fixture)
        {
            _fixture = fixture;
        }

        // ---------- Ayudantes ----------

        private async Task<List<InventoryMovement>> MovimientosAsync(Guid productoId)
        {
            await using DataContext context = _fixture.CrearContexto();
            return await context.InventoryMovements.AsNoTracking()
                .Where(m => m.ProductId == productoId)
                .OrderBy(m => m.Number)
                .ToListAsync();
        }

        private async Task<decimal> StockAsync(Guid productoId)
        {
            await using DataContext context = _fixture.CrearContexto();
            return await context.Products.Where(p => p.Id == productoId).Select(p => p.Stock).SingleAsync();
        }

        private static CreateSaleDTO Venta(Guid? codigo, params (Product producto, decimal cantidad)[] lineas) => new CreateSaleDTO
        {
            PaymentMethod = PaymentMethod.Cash,
            Items = lineas.Select(l => new SaleLineDTO { ProductId = l.producto.Id, Quantity = l.cantidad }).ToList(),
            IdempotencyKey = codigo ?? Guid.NewGuid()
        };

        private async Task<Response<SaleResultDTO>> VenderAsync(CreateSaleDTO venta)
        {
            await using DataContext context = _fixture.CrearContexto();
            return await new SalesService(context).CreateAsync(venta, _fixture.CajeroId);
        }

        private async Task<Guid> CrearProveedorAsync()
        {
            await using DataContext context = _fixture.CrearContexto();
            Supplier proveedor = new Supplier { Id = Guid.NewGuid(), Name = $"Proveedor {Guid.NewGuid().ToString("N")[..8]}" };
            context.Suppliers.Add(proveedor);
            await context.SaveChangesAsync();
            return proveedor.Id;
        }

        private async Task<Response<PurchaseResultDTO>> ComprarAsync(Guid proveedor, params (Product producto, decimal cantidad, decimal costo)[] lineas)
        {
            await using DataContext context = _fixture.CrearContexto();
            return await new PurchasesService(context).CreateAsync(new CreatePurchaseDTO
            {
                SupplierId = proveedor,
                Items = lineas.Select(l => new PurchaseLineDTO { ProductId = l.producto.Id, Quantity = l.cantidad, UnitCost = l.costo }).ToList(),
                IdempotencyKey = Guid.NewGuid()
            }, _fixture.CajeroId);
        }

        // ---------- Ventas ----------

        [Fact]
        public async Task Una_venta_deja_un_movimiento_por_producto_con_su_numero_y_el_stock_despues()
        {
            Product arroz = await _fixture.CrearProductoAsync("Arroz", 2500m, 10m);
            Product leche = await _fixture.CrearProductoAsync("Leche", 1200m, 5m);

            Response<SaleResultDTO> venta = await VenderAsync(Venta(null, (arroz, 3m), (leche, 5m)));

            Assert.True(venta.IsSuccess, venta.Message);

            InventoryMovement deArroz = Assert.Single(await MovimientosAsync(arroz.Id));
            Assert.Equal(InventoryMovementType.Sale, deArroz.Type);
            Assert.Equal(-3m, deArroz.Quantity);
            Assert.Equal(7m, deArroz.StockAfter);
            Assert.Equal(venta.Result!.Id, deArroz.ReferenceId);
            Assert.Equal(venta.Result.Number, deArroz.ReferenceNumber);
            Assert.Equal(_fixture.CajeroId, deArroz.UserId);

            InventoryMovement deLeche = Assert.Single(await MovimientosAsync(leche.Id));
            Assert.Equal(-5m, deLeche.Quantity);
            Assert.Equal(0m, deLeche.StockAfter);
        }

        [Fact]
        public async Task Una_venta_que_falla_a_medias_no_deja_ningun_movimiento()
        {
            // Las líneas se procesan ordenadas por ProductId: la buena primero y la que no alcanza después.
            Guid[] ids = { Guid.NewGuid(), Guid.NewGuid() };
            Array.Sort(ids);
            Product bueno = await _fixture.CrearProductoAsync("Bueno", 1000m, 10m, id: ids[0]);
            Product sinStock = await _fixture.CrearProductoAsync("SinStock", 1000m, 1m, id: ids[1]);

            Response<SaleResultDTO> venta = await VenderAsync(Venta(null, (bueno, 2m), (sinStock, 5m)));

            Assert.False(venta.IsSuccess);
            Assert.Empty(await MovimientosAsync(bueno.Id));
            Assert.Empty(await MovimientosAsync(sinStock.Id));
            Assert.Equal(10m, await StockAsync(bueno.Id));
        }

        [Fact]
        public async Task Reintentar_el_mismo_cobro_no_duplica_el_movimiento()
        {
            Product pan = await _fixture.CrearProductoAsync("Pan", 300m, 10m);
            Guid codigo = Guid.NewGuid();

            await VenderAsync(Venta(codigo, (pan, 2m)));
            Response<SaleResultDTO> reintento = await VenderAsync(Venta(codigo, (pan, 2m)));

            Assert.True(reintento.Result!.AlreadyRegistered);
            Assert.Single(await MovimientosAsync(pan.Id));
            Assert.Equal(8m, await StockAsync(pan.Id));
        }

        [Fact]
        public async Task Anular_una_venta_devuelve_el_stock_y_lo_deja_en_el_historial()
        {
            Product aceite = await _fixture.CrearProductoAsync("Aceite", 9000m, 10m);
            Response<SaleResultDTO> venta = await VenderAsync(Venta(null, (aceite, 4m)));
            string supervisor = await _fixture.CrearCajeroAsync("Supervisor");

            await using (DataContext context = _fixture.CrearContexto())
            {
                Response<SaleDetailDTO> anulada = await new SalesService(context)
                    .CancelAsync(venta.Result!.Id, new CancelSaleDTO { Reason = "Error de captura" }, supervisor);
                Assert.True(anulada.IsSuccess, anulada.Message);
            }

            List<InventoryMovement> movimientos = await MovimientosAsync(aceite.Id);
            Assert.Equal(2, movimientos.Count);
            InventoryMovement anulacion = movimientos[1];
            Assert.Equal(InventoryMovementType.SaleCancellation, anulacion.Type);
            Assert.Equal(4m, anulacion.Quantity);
            Assert.Equal(10m, anulacion.StockAfter);
            Assert.Equal(venta.Result.Id, anulacion.ReferenceId);
            Assert.Equal(venta.Result.Number, anulacion.ReferenceNumber);
            Assert.Equal(supervisor, anulacion.UserId);
        }

        [Fact]
        public async Task Una_anulacion_rechazada_no_deja_movimiento()
        {
            Product sal = await _fixture.CrearProductoAsync("Sal", 1000m, 10m);
            Response<SaleResultDTO> venta = await VenderAsync(Venta(null, (sal, 2m)));

            await using (DataContext context = _fixture.CrearContexto())
            {
                await new SalesService(context).CancelAsync(venta.Result!.Id, new CancelSaleDTO { Reason = "Primera" }, _fixture.CajeroId);
            }
            await using (DataContext context = _fixture.CrearContexto())
            {
                Response<SaleDetailDTO> segunda = await new SalesService(context)
                    .CancelAsync(venta.Result!.Id, new CancelSaleDTO { Reason = "Otra vez" }, _fixture.CajeroId);
                Assert.False(segunda.IsSuccess);
            }

            Assert.Equal(2, (await MovimientosAsync(sal.Id)).Count); // venta + una sola anulación
            Assert.Equal(10m, await StockAsync(sal.Id));
        }

        // ---------- Devoluciones ----------

        [Fact]
        public async Task Una_devolucion_solo_registra_las_lineas_que_vuelven_al_inventario()
        {
            Product vuelve = await _fixture.CrearProductoAsync("Vuelve", 1000m, 10m);
            Product seDescarta = await _fixture.CrearProductoAsync("SeDescarta", 1000m, 10m);
            Response<SaleResultDTO> venta = await VenderAsync(Venta(null, (vuelve, 3m), (seDescarta, 2m)));

            Guid itemVuelve, itemDescarta;
            await using (DataContext context = _fixture.CrearContexto())
            {
                itemVuelve = await context.SaleItems.Where(i => i.SaleId == venta.Result!.Id && i.ProductId == vuelve.Id).Select(i => i.Id).SingleAsync();
                itemDescarta = await context.SaleItems.Where(i => i.SaleId == venta.Result!.Id && i.ProductId == seDescarta.Id).Select(i => i.Id).SingleAsync();
            }

            Response<SaleReturnDetailDTO> devolucion;
            await using (DataContext context = _fixture.CrearContexto())
            {
                devolucion = await new SaleReturnsService(context).CreateAsync(venta.Result!.Id, new CreateSaleReturnDTO
                {
                    Reason = "Cliente devolvió",
                    Lines =
                    {
                        new SaleReturnLineDTO { SaleItemId = itemVuelve, Quantity = 2m, Restock = true },
                        new SaleReturnLineDTO { SaleItemId = itemDescarta, Quantity = 1m, Restock = false }
                    }
                }, _fixture.CajeroId);
            }
            Assert.True(devolucion.IsSuccess, devolucion.Message);

            List<InventoryMovement> deVuelve = await MovimientosAsync(vuelve.Id);
            InventoryMovement movimiento = deVuelve[1];
            Assert.Equal(InventoryMovementType.SaleReturn, movimiento.Type);
            Assert.Equal(2m, movimiento.Quantity);
            Assert.Equal(9m, movimiento.StockAfter); // 10 − 3 + 2
            Assert.Equal(devolucion.Result!.Id, movimiento.ReferenceId);
            Assert.Equal(devolucion.Result.Number, movimiento.ReferenceNumber);

            // El producto que se descartó (no vuelve al inventario) no tiene movimiento de devolución.
            Assert.Single(await MovimientosAsync(seDescarta.Id));
            Assert.Equal(8m, await StockAsync(seDescarta.Id));
        }

        // ---------- Compras ----------

        [Fact]
        public async Task Una_compra_deja_un_movimiento_por_linea_con_el_stock_despues_de_cada_una()
        {
            Guid proveedor = await CrearProveedorAsync();
            Product pan = await _fixture.CrearProductoAsync("Pan", 300m, 0m);

            // Mismo producto con dos costos: dos líneas, y cada movimiento guarda su propio "stock después".
            Response<PurchaseResultDTO> compra = await ComprarAsync(proveedor, (pan, 10m, 200m), (pan, 5m, 250m));

            Assert.True(compra.IsSuccess, compra.Message);
            List<InventoryMovement> movimientos = await MovimientosAsync(pan.Id);
            Assert.Equal(2, movimientos.Count);
            Assert.All(movimientos, m =>
            {
                Assert.Equal(InventoryMovementType.Purchase, m.Type);
                Assert.Equal(compra.Result!.Id, m.ReferenceId);
                Assert.Equal(compra.Result.Number, m.ReferenceNumber);
                Assert.Equal(_fixture.CajeroId, m.UserId);
            });
            Assert.Equal(new[] { 10m, 5m }, movimientos.Select(m => m.Quantity));
            Assert.Equal(new[] { 10m, 15m }, movimientos.Select(m => m.StockAfter));
        }

        [Fact]
        public async Task Una_compra_que_falla_no_deja_movimientos_y_un_reintento_no_los_duplica()
        {
            Guid proveedor = await CrearProveedorAsync();
            Guid[] ids = { Guid.NewGuid(), Guid.NewGuid() };
            Array.Sort(ids);
            Product bueno = await _fixture.CrearProductoAsync("Bueno", 1000m, 0m, UnitOfMeasure.Kilogram, id: ids[0]);
            Product malo = await _fixture.CrearProductoAsync("Malo", 1000m, 0m, UnitOfMeasure.Unit, id: ids[1]);

            Response<PurchaseResultDTO> fallida = await ComprarAsync(proveedor, (bueno, 5m, 700m), (malo, 1.5m, 700m));
            Assert.False(fallida.IsSuccess);
            Assert.Empty(await MovimientosAsync(bueno.Id));
            Assert.Empty(await MovimientosAsync(malo.Id));

            Guid codigo = Guid.NewGuid();
            CreatePurchaseDTO dto = new CreatePurchaseDTO
            {
                SupplierId = proveedor,
                Items = { new PurchaseLineDTO { ProductId = bueno.Id, Quantity = 4m, UnitCost = 100m } },
                IdempotencyKey = codigo
            };
            await using (DataContext context = _fixture.CrearContexto())
                Assert.True((await new PurchasesService(context).CreateAsync(dto, _fixture.CajeroId)).IsSuccess);
            await using (DataContext context = _fixture.CrearContexto())
                Assert.True((await new PurchasesService(context).CreateAsync(dto, _fixture.CajeroId)).Result!.AlreadyRegistered);

            Assert.Single(await MovimientosAsync(bueno.Id));
        }

        [Fact]
        public async Task Anular_una_compra_deja_el_movimiento_y_una_anulacion_rechazada_no()
        {
            Guid proveedor = await CrearProveedorAsync();
            Product harina = await _fixture.CrearProductoAsync("Harina", 3000m, 0m);
            Response<PurchaseResultDTO> compra = await ComprarAsync(proveedor, (harina, 10m, 2000m));
            await VenderAsync(Venta(null, (harina, 8m))); // quedan 2: ya no alcanza para anular 10

            Response<PurchaseResultDTO> rechazada;
            await using (DataContext context = _fixture.CrearContexto())
                rechazada = await new PurchasesService(context).CancelAsync(compra.Result!.Id, new CancelPurchaseDTO { Reason = "Prueba" }, _fixture.CajeroId);
            Assert.False(rechazada.IsSuccess);
            Assert.Equal(2, (await MovimientosAsync(harina.Id)).Count); // compra + venta, sin anulación

            await VenderAsync(Venta(null, (harina, 0m + 2m))); // vende lo que quedaba para comprobar el caso contrario más abajo
            Product azucar = await _fixture.CrearProductoAsync("Azúcar", 3000m, 5m);
            Response<PurchaseResultDTO> otra = await ComprarAsync(proveedor, (azucar, 10m, 1000m));
            string supervisor = await _fixture.CrearCajeroAsync("Supervisor");
            await using (DataContext context = _fixture.CrearContexto())
                Assert.True((await new PurchasesService(context).CancelAsync(otra.Result!.Id, new CancelPurchaseDTO { Reason = "Error" }, supervisor)).IsSuccess);

            List<InventoryMovement> deAzucar = await MovimientosAsync(azucar.Id);
            Assert.Equal(2, deAzucar.Count);
            InventoryMovement anulacion = deAzucar[1];
            Assert.Equal(InventoryMovementType.PurchaseCancellation, anulacion.Type);
            Assert.Equal(-10m, anulacion.Quantity);
            Assert.Equal(5m, anulacion.StockAfter);
            Assert.Equal(otra.Result.Id, anulacion.ReferenceId);
            Assert.Equal(otra.Result.Number, anulacion.ReferenceNumber);
            Assert.Equal(supervisor, anulacion.UserId);
        }

        // ---------- Productos ----------

        private ProductDTO NuevoProducto(string nombre, decimal stock) => new ProductDTO
        {
            Name = $"{nombre} {Guid.NewGuid().ToString("N")[..8]}",
            Price = 1000m,
            Stock = stock,
            Unit = UnitOfMeasure.Unit,
            CategoryId = _fixture.CategoriaId
        };

        [Fact]
        public async Task Crear_un_producto_con_stock_deja_el_movimiento_de_stock_inicial()
        {
            ProductDTO dto = NuevoProducto("ConStock", 12m);

            Response<ProductDTO> respuesta;
            await using (DataContext context = _fixture.CrearContexto())
                respuesta = await new ProductsService(context).CreateAsync(dto, _fixture.CajeroId);
            Assert.True(respuesta.IsSuccess, respuesta.Message);

            await using DataContext verificacion = _fixture.CrearContexto();
            Guid id = await verificacion.Products.Where(p => p.Name == dto.Name).Select(p => p.Id).SingleAsync();
            InventoryMovement movimiento = Assert.Single(await MovimientosAsync(id));
            Assert.Equal(InventoryMovementType.InitialStock, movimiento.Type);
            Assert.Equal(12m, movimiento.Quantity);
            Assert.Equal(12m, movimiento.StockAfter);
            Assert.Equal(_fixture.CajeroId, movimiento.UserId);
            Assert.Null(movimiento.ReferenceId);
        }

        [Fact]
        public async Task Crear_un_producto_sin_stock_o_que_falla_no_deja_movimientos()
        {
            ProductDTO sinStock = NuevoProducto("SinStock", 0m);
            ProductDTO repetido = NuevoProducto("Repetido", 5m);

            await using (DataContext context = _fixture.CrearContexto())
            {
                Assert.True((await new ProductsService(context).CreateAsync(sinStock, _fixture.CajeroId)).IsSuccess);
                Assert.True((await new ProductsService(context).CreateAsync(repetido, _fixture.CajeroId)).IsSuccess);
            }
            int movimientosAntes;
            await using (DataContext context = _fixture.CrearContexto())
                movimientosAntes = await context.InventoryMovements.CountAsync();

            await using (DataContext context = _fixture.CrearContexto())
            {
                ProductDTO duplicado = NuevoProducto("Otro", 9m);
                duplicado.Name = repetido.Name; // el nombre ya existe: se rechaza
                Response<ProductDTO> respuesta = await new ProductsService(context).CreateAsync(duplicado, _fixture.CajeroId);
                Assert.False(respuesta.IsSuccess);
            }

            await using DataContext verificacion = _fixture.CrearContexto();
            Guid idSinStock = await verificacion.Products.Where(p => p.Name == sinStock.Name).Select(p => p.Id).SingleAsync();
            Assert.Empty(await MovimientosAsync(idSinStock));
            Assert.Equal(movimientosAntes, await verificacion.InventoryMovements.CountAsync()); // el rechazado no dejó nada
        }

        // ---------- Saldo inicial del historial ----------

        [Fact]
        public async Task El_saldo_inicial_solo_se_crea_para_productos_con_stock_y_sin_movimientos_y_no_se_repite()
        {
            Product conStock = await _fixture.CrearProductoAsync("ConStock", 1000m, 7.5m, UnitOfMeasure.Kilogram);
            Product sinStock = await _fixture.CrearProductoAsync("SinStock", 1000m, 0m);
            Product conHistorial = await _fixture.CrearProductoAsync("ConHistorial", 1000m, 4m);
            await VenderAsync(Venta(null, (conHistorial, 1m))); // ya tiene un movimiento

            await using (DataContext context = _fixture.CrearContexto())
                Assert.True(await new InventorySeeder(context).SeedOpeningBalanceAsync() >= 1);

            InventoryMovement saldo = Assert.Single(await MovimientosAsync(conStock.Id));
            Assert.Equal(InventoryMovementType.OpeningBalance, saldo.Type);
            Assert.Equal(7.5m, saldo.Quantity);
            Assert.Equal(7.5m, saldo.StockAfter);
            Assert.Null(saldo.UserId);

            Assert.Empty(await MovimientosAsync(sinStock.Id));
            Assert.Single(await MovimientosAsync(conHistorial.Id)); // no se le agregó saldo inicial

            // Segunda vez: no crea nada más.
            await using (DataContext context = _fixture.CrearContexto())
                Assert.Equal(0, await new InventorySeeder(context).SeedOpeningBalanceAsync());
            Assert.Single(await MovimientosAsync(conStock.Id));
        }

        // ---------- El historial siempre cuadra con el stock ----------

        [Fact]
        public async Task Con_compras_y_ventas_simultaneas_el_historial_suma_exacto_y_cada_stock_despues_es_el_acumulado()
        {
            Guid proveedor = await CrearProveedorAsync();
            Product gaseosa = await _fixture.CrearProductoAsync("Gaseosa", 3000m, 10m);

            TaskCompletionSource salida = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

            IEnumerable<Task<bool>> compras = Enumerable.Range(0, 10).Select(_ => Task.Run(async () =>
            {
                await salida.Task;
                return (await ComprarAsync(proveedor, (gaseosa, 2m, 1800m))).IsSuccess;
            }));
            IEnumerable<Task<bool>> ventas = Enumerable.Range(0, 10).Select(_ => Task.Run(async () =>
            {
                await salida.Task;
                return (await VenderAsync(Venta(null, (gaseosa, 1m)))).IsSuccess;
            }));

            Task<bool>[] todas = compras.Concat(ventas).ToArray();
            salida.SetResult();
            Assert.All(await Task.WhenAll(todas), ok => Assert.True(ok));

            List<InventoryMovement> movimientos = await MovimientosAsync(gaseosa.Id);
            Assert.Equal(20, movimientos.Count);

            // Stock final = 10 inicial + 10 compras de 2 − 10 ventas de 1 = 20, y coincide con la suma del historial.
            Assert.Equal(20m, await StockAsync(gaseosa.Id));
            Assert.Equal(10m, movimientos.Sum(m => m.Quantity));

            // En el orden del historial, cada "stock después" es el stock inicial más lo acumulado hasta ahí.
            decimal acumulado = 10m;
            foreach (InventoryMovement movimiento in movimientos)
            {
                acumulado += movimiento.Quantity;
                Assert.Equal(acumulado, movimiento.StockAfter);
                Assert.True(movimiento.StockAfter >= 0);
            }
        }
    }
}
