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
    /// <summary>Ajustes de inventario y consultas del historial (InventoryService).</summary>
    [Collection(ColeccionBaseDeDatos.Nombre)]
    public class InventoryServiceTests
    {
        private readonly BaseDeDatosFixture _fixture;

        public InventoryServiceTests(BaseDeDatosFixture fixture)
        {
            _fixture = fixture;
        }

        // ---------- Ayudantes ----------

        private static AdjustInventoryDTO Ajuste(Product producto, AdjustmentDirection direccion, decimal cantidad,
            AdjustmentReason motivo = AdjustmentReason.Waste, string? notas = null, Guid? codigo = null) => new AdjustInventoryDTO
        {
            ProductId = producto.Id,
            Direction = direccion,
            Quantity = cantidad,
            Reason = motivo,
            Notes = notas,
            IdempotencyKey = codigo ?? Guid.NewGuid()
        };

        private async Task<Response<AdjustInventoryResultDTO>> AjustarAsync(AdjustInventoryDTO dto, string? usuario = null)
        {
            await using DataContext context = _fixture.CrearContexto();
            return await new InventoryService(context).AdjustAsync(dto, usuario ?? _fixture.CajeroId);
        }

        private async Task<decimal> StockAsync(Guid productoId)
        {
            await using DataContext context = _fixture.CrearContexto();
            return await context.Products.Where(p => p.Id == productoId).Select(p => p.Stock).SingleAsync();
        }

        private async Task<List<InventoryMovement>> MovimientosAsync(Guid productoId)
        {
            await using DataContext context = _fixture.CrearContexto();
            return await context.InventoryMovements.AsNoTracking().Where(m => m.ProductId == productoId).OrderBy(m => m.Number).ToListAsync();
        }

        private async Task<List<InventoryMovementDTO>> ConsultarAsync(InventoryQueryDTO consulta)
        {
            await using DataContext context = _fixture.CrearContexto();
            Response<PaginationResponse<InventoryMovementDTO>> respuesta = await new InventoryService(context).GetMovementsAsync(consulta);
            Assert.True(respuesta.IsSuccess, respuesta.Message);
            return respuesta.Result!.List.ToList();
        }

        // ---------- Ajustes ----------

        [Fact]
        public async Task Una_entrada_suma_al_stock_y_deja_el_movimiento_con_motivo_y_usuario()
        {
            Product producto = await _fixture.CrearProductoAsync("Entrada", 1000m, 5m);

            Response<AdjustInventoryResultDTO> respuesta = await AjustarAsync(
                Ajuste(producto, AdjustmentDirection.Increase, 3m, AdjustmentReason.PhysicalCount, "  Contados de más  "));

            Assert.True(respuesta.IsSuccess, respuesta.Message);
            Assert.Equal(8m, respuesta.Result!.StockAfter);
            Assert.False(respuesta.Result.AlreadyRegistered);
            Assert.Equal(8m, await StockAsync(producto.Id));

            InventoryMovement movimiento = Assert.Single(await MovimientosAsync(producto.Id));
            Assert.Equal(InventoryMovementType.Adjustment, movimiento.Type);
            Assert.Equal(3m, movimiento.Quantity);
            Assert.Equal(8m, movimiento.StockAfter);
            Assert.Equal(AdjustmentReason.PhysicalCount, movimiento.Reason);
            Assert.Equal("Contados de más", movimiento.Notes);
            Assert.Equal(_fixture.CajeroId, movimiento.UserId);
            Assert.Null(movimiento.ReferenceId);
        }

        [Fact]
        public async Task Una_salida_resta_del_stock_y_puede_dejarlo_en_cero()
        {
            Product producto = await _fixture.CrearProductoAsync("Salida", 1000m, 4m);

            Response<AdjustInventoryResultDTO> respuesta = await AjustarAsync(Ajuste(producto, AdjustmentDirection.Decrease, 4m));

            Assert.True(respuesta.IsSuccess, respuesta.Message);
            Assert.Equal(0m, await StockAsync(producto.Id));
            InventoryMovement movimiento = Assert.Single(await MovimientosAsync(producto.Id));
            Assert.Equal(-4m, movimiento.Quantity);
            Assert.Equal(0m, movimiento.StockAfter);
        }

        [Fact]
        public async Task Una_salida_mayor_al_stock_se_rechaza_y_no_cambia_nada()
        {
            Product producto = await _fixture.CrearProductoAsync("Poco", 1000m, 2m);

            Response<AdjustInventoryResultDTO> respuesta = await AjustarAsync(Ajuste(producto, AdjustmentDirection.Decrease, 3m));

            Assert.False(respuesta.IsSuccess);
            Assert.Contains("Stock insuficiente", respuesta.Message);
            Assert.Equal(2m, await StockAsync(producto.Id));
            Assert.Empty(await MovimientosAsync(producto.Id));
        }

        [Fact]
        public async Task Productos_por_peso_aceptan_decimales_y_por_unidad_solo_enteros()
        {
            Product porPeso = await _fixture.CrearProductoAsync("Queso", 20000m, 1m, UnitOfMeasure.Kilogram);
            Product porUnidad = await _fixture.CrearProductoAsync("Huevo", 500m, 10m, UnitOfMeasure.Unit);

            Assert.True((await AjustarAsync(Ajuste(porPeso, AdjustmentDirection.Decrease, 0.25m))).IsSuccess);
            Assert.Equal(0.75m, await StockAsync(porPeso.Id));

            Response<AdjustInventoryResultDTO> rechazado = await AjustarAsync(Ajuste(porUnidad, AdjustmentDirection.Decrease, 1.5m));
            Assert.False(rechazado.IsSuccess);
            Assert.Equal(10m, await StockAsync(porUnidad.Id));
            Assert.Empty(await MovimientosAsync(porUnidad.Id));
        }

        [Fact]
        public async Task Validaciones_cantidad_motivo_notas_producto_y_codigo()
        {
            Product producto = await _fixture.CrearProductoAsync("Valida", 1000m, 10m);

            // Cantidad cero, negativa o que se redondea a cero.
            Assert.False((await AjustarAsync(Ajuste(producto, AdjustmentDirection.Increase, 0m))).IsSuccess);
            Assert.False((await AjustarAsync(Ajuste(producto, AdjustmentDirection.Increase, -2m))).IsSuccess);
            Assert.False((await AjustarAsync(Ajuste(producto, AdjustmentDirection.Increase, 0.0004m))).IsSuccess);
            Assert.False((await AjustarAsync(Ajuste(producto, AdjustmentDirection.Increase, 2_000_000m))).IsSuccess);

            // "Otro" exige notas; las notas tienen máximo 200 caracteres.
            Assert.False((await AjustarAsync(Ajuste(producto, AdjustmentDirection.Decrease, 1m, AdjustmentReason.Other))).IsSuccess);
            Assert.False((await AjustarAsync(Ajuste(producto, AdjustmentDirection.Decrease, 1m, AdjustmentReason.Other, "   "))).IsSuccess);
            Assert.False((await AjustarAsync(Ajuste(producto, AdjustmentDirection.Decrease, 1m, AdjustmentReason.Waste, new string('x', 201)))).IsSuccess);
            Assert.True((await AjustarAsync(Ajuste(producto, AdjustmentDirection.Decrease, 1m, AdjustmentReason.Other, "Se cayó del estante"))).IsSuccess);

            // Motivo y dirección inválidos, producto inexistente y código vacío.
            Assert.False((await AjustarAsync(Ajuste(producto, AdjustmentDirection.Decrease, 1m, (AdjustmentReason)99))).IsSuccess);
            Assert.False((await AjustarAsync(Ajuste(producto, (AdjustmentDirection)0, 1m))).IsSuccess);
            Assert.False((await AjustarAsync(new AdjustInventoryDTO
            {
                ProductId = Guid.NewGuid(), Direction = AdjustmentDirection.Increase, Quantity = 1m, IdempotencyKey = Guid.NewGuid()
            })).IsSuccess);
            AdjustInventoryDTO sinCodigo = Ajuste(producto, AdjustmentDirection.Increase, 1m);
            sinCodigo.IdempotencyKey = Guid.Empty;
            Assert.False((await AjustarAsync(sinCodigo)).IsSuccess);

            // Solo el ajuste válido dejó huella.
            Assert.Equal(9m, await StockAsync(producto.Id));
            Assert.Single(await MovimientosAsync(producto.Id));
        }

        [Fact]
        public async Task Reenviar_el_mismo_ajuste_no_lo_aplica_dos_veces()
        {
            Product producto = await _fixture.CrearProductoAsync("Reintento", 1000m, 10m);
            Guid codigo = Guid.NewGuid();

            Response<AdjustInventoryResultDTO> primero = await AjustarAsync(Ajuste(producto, AdjustmentDirection.Decrease, 3m, codigo: codigo));
            Response<AdjustInventoryResultDTO> segundo = await AjustarAsync(Ajuste(producto, AdjustmentDirection.Decrease, 3m, codigo: codigo));

            Assert.True(primero.IsSuccess);
            Assert.True(segundo.IsSuccess);
            Assert.True(segundo.Result!.AlreadyRegistered);
            Assert.Equal(primero.Result!.MovementId, segundo.Result.MovementId);
            Assert.Equal(7m, await StockAsync(producto.Id));
            Assert.Single(await MovimientosAsync(producto.Id));
        }

        [Fact]
        public async Task El_mismo_codigo_enviado_a_la_vez_se_aplica_una_sola_vez()
        {
            Product producto = await _fixture.CrearProductoAsync("Simultaneo", 1000m, 20m);
            Guid codigo = Guid.NewGuid();
            TaskCompletionSource salida = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

            Task<Response<AdjustInventoryResultDTO>>[] envios = Enumerable.Range(0, 6).Select(_ => Task.Run(async () =>
            {
                await salida.Task;
                return await AjustarAsync(Ajuste(producto, AdjustmentDirection.Decrease, 2m, codigo: codigo));
            })).ToArray();
            salida.SetResult();
            Response<AdjustInventoryResultDTO>[] respuestas = await Task.WhenAll(envios);

            Assert.All(respuestas, r => Assert.True(r.IsSuccess, r.Message)); // todos reciben el mismo resultado
            Assert.Equal(1, respuestas.Count(r => !r.Result!.AlreadyRegistered));
            Assert.Equal(18m, await StockAsync(producto.Id));
            Assert.Single(await MovimientosAsync(producto.Id));
        }

        [Fact]
        public async Task El_mismo_codigo_de_otro_usuario_es_otro_ajuste()
        {
            Product producto = await _fixture.CrearProductoAsync("DosUsuarios", 1000m, 10m);
            string otro = await _fixture.CrearCajeroAsync("Otro");
            Guid codigo = Guid.NewGuid();

            Assert.True((await AjustarAsync(Ajuste(producto, AdjustmentDirection.Decrease, 1m, codigo: codigo))).IsSuccess);
            Response<AdjustInventoryResultDTO> deOtro = await AjustarAsync(Ajuste(producto, AdjustmentDirection.Decrease, 1m, codigo: codigo), otro);

            Assert.False(deOtro.Result!.AlreadyRegistered);
            Assert.Equal(8m, await StockAsync(producto.Id));
        }

        [Fact]
        public async Task Ajustes_y_ventas_simultaneos_nunca_dejan_el_stock_negativo_y_el_historial_cuadra()
        {
            Product producto = await _fixture.CrearProductoAsync("Disputa", 1000m, 10m);
            TaskCompletionSource salida = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

            // 10 de stock, 10 ajustes de salida de 1 y 10 ventas de 1: solo pueden salir 10 en total.
            IEnumerable<Task<bool>> ajustes = Enumerable.Range(0, 10).Select(_ => Task.Run(async () =>
            {
                await salida.Task;
                return (await AjustarAsync(Ajuste(producto, AdjustmentDirection.Decrease, 1m))).IsSuccess;
            }));
            IEnumerable<Task<bool>> ventas = Enumerable.Range(0, 10).Select(_ => Task.Run(async () =>
            {
                await salida.Task;
                await using DataContext context = _fixture.CrearContexto();
                return (await new SalesService(context).CreateAsync(new CreateSaleDTO
                {
                    PaymentMethod = PaymentMethod.Cash,
                    Items = { new SaleLineDTO { ProductId = producto.Id, Quantity = 1m } },
                    IdempotencyKey = Guid.NewGuid()
                }, _fixture.CajeroId)).IsSuccess;
            }));

            Task<bool>[] todas = ajustes.Concat(ventas).ToArray();
            salida.SetResult();
            bool[] resultados = await Task.WhenAll(todas);

            Assert.Equal(10, resultados.Count(ok => ok));
            Assert.Equal(0m, await StockAsync(producto.Id));

            List<InventoryMovement> movimientos = await MovimientosAsync(producto.Id);
            Assert.Equal(10, movimientos.Count);
            Assert.Equal(-10m, movimientos.Sum(m => m.Quantity));
            decimal acumulado = 10m;
            foreach (InventoryMovement m in movimientos)
            {
                acumulado += m.Quantity;
                Assert.Equal(acumulado, m.StockAfter);
            }
        }

        // ---------- Consultas ----------

        [Fact]
        public async Task El_historial_filtra_por_producto_y_tipo_y_muestra_lo_mas_reciente_primero()
        {
            Product producto = await _fixture.CrearProductoAsync("Historia", 1000m, 10m);
            Product otro = await _fixture.CrearProductoAsync("Otro", 1000m, 10m);

            await AjustarAsync(Ajuste(producto, AdjustmentDirection.Decrease, 1m));
            await AjustarAsync(Ajuste(producto, AdjustmentDirection.Increase, 5m));
            await AjustarAsync(Ajuste(otro, AdjustmentDirection.Decrease, 2m));

            List<InventoryMovementDTO> deProducto = await ConsultarAsync(new InventoryQueryDTO { ProductId = producto.Id });
            Assert.Equal(2, deProducto.Count);
            Assert.Equal(5m, deProducto[0].Quantity); // el último primero
            Assert.Equal(-1m, deProducto[1].Quantity);
            Assert.All(deProducto, m => Assert.Equal(producto.Name, m.ProductName));
            Assert.All(deProducto, m => Assert.NotNull(m.UserName));

            Assert.Empty(await ConsultarAsync(new InventoryQueryDTO { ProductId = producto.Id, Type = InventoryMovementType.Sale }));
            Assert.Equal(2, (await ConsultarAsync(new InventoryQueryDTO { ProductId = producto.Id, Type = InventoryMovementType.Adjustment })).Count);

            // El buscador por texto encuentra por nombre.
            List<InventoryMovementDTO> porNombre = await ConsultarAsync(new InventoryQueryDTO { Filter = producto.Name.ToUpper() });
            Assert.Equal(2, porNombre.Count);
        }

        [Fact]
        public async Task El_historial_filtra_por_dias_de_Colombia_con_ambos_extremos_incluidos()
        {
            Product producto = await _fixture.CrearProductoAsync("Fechas", 1000m, 10m);
            DateOnly dia = new DateOnly(2020, 3, 10);
            DateTime inicio = ColombiaCalendar.StartOfDayUtc(dia);

            // Un movimiento a las 00:00 del 10 (Colombia), uno a las 23:59 y uno a las 00:00 del 11.
            await using (DataContext context = _fixture.CrearContexto())
            {
                foreach (DateTime fecha in new[] { inicio, inicio.AddHours(24).AddMinutes(-1), inicio.AddHours(24) })
                    context.InventoryMovements.Add(new InventoryMovement
                    {
                        Id = Guid.NewGuid(), CreatedAtUtc = fecha, ProductId = producto.Id, Type = InventoryMovementType.Adjustment,
                        Quantity = 1m, StockAfter = 10m, UserId = _fixture.CajeroId
                    });
                await context.SaveChangesAsync();
            }

            Assert.Equal(2, (await ConsultarAsync(new InventoryQueryDTO { ProductId = producto.Id, From = dia, To = dia })).Count);
            Assert.Equal(3, (await ConsultarAsync(new InventoryQueryDTO { ProductId = producto.Id, From = dia, To = dia.AddDays(1) })).Count);
            Assert.Single(await ConsultarAsync(new InventoryQueryDTO { ProductId = producto.Id, From = dia.AddDays(1) }));
            Assert.Empty(await ConsultarAsync(new InventoryQueryDTO { ProductId = producto.Id, To = dia.AddDays(-1) }));

            await using DataContext invertido = _fixture.CrearContexto();
            Assert.False((await new InventoryService(invertido).GetMovementsAsync(
                new InventoryQueryDTO { From = dia.AddDays(2), To = dia })).IsSuccess);
        }

        [Fact]
        public async Task El_historial_pagina_y_el_saldo_inicial_aparece_sin_usuario()
        {
            Product producto = await _fixture.CrearProductoAsync("Paginado", 1000m, 50m);
            await using (DataContext context = _fixture.CrearContexto())
                await new InventorySeeder(context).SeedOpeningBalanceAsync();
            for (int i = 0; i < 4; i++)
                await AjustarAsync(Ajuste(producto, AdjustmentDirection.Decrease, 1m));

            await using DataContext lectura = _fixture.CrearContexto();
            Response<PaginationResponse<InventoryMovementDTO>> pagina = await new InventoryService(lectura).GetMovementsAsync(
                new InventoryQueryDTO { ProductId = producto.Id, Page = 2, RecordsPerPage = 3 });

            Assert.True(pagina.IsSuccess);
            Assert.Equal(5, pagina.Result!.TotalCount);
            Assert.Equal(2, pagina.Result.TotalPages);
            InventoryMovementDTO ultimo = Assert.Single(pagina.Result.List.Skip(1));
            Assert.Equal(InventoryMovementType.OpeningBalance, ultimo.Type);
            Assert.Null(ultimo.UserName);
        }

        [Fact]
        public async Task GetProductAsync_devuelve_el_stock_actual_y_falla_si_no_existe()
        {
            Product producto = await _fixture.CrearProductoAsync("Cabecera", 1000m, 6m, UnitOfMeasure.Kilogram);
            await using DataContext context = _fixture.CrearContexto();
            InventoryService servicio = new InventoryService(context);

            Response<InventoryProductDTO> existe = await servicio.GetProductAsync(producto.Id);
            Assert.True(existe.IsSuccess);
            Assert.Equal(6m, existe.Result!.Stock);
            Assert.Equal(UnitOfMeasure.Kilogram, existe.Result.Unit);

            Assert.False((await servicio.GetProductAsync(Guid.NewGuid())).IsSuccess);
        }

        // ---------- Permisos ----------

        [Fact]
        public async Task Los_permisos_de_inventario_existen_y_el_supervisor_solo_nace_con_ver()
        {
            await using (DataContext context = _fixture.CrearContexto())
                await new PermissionsSeeder(context).SeedAsync();

            await using DataContext lectura = _fixture.CrearContexto();
            List<string> modulo = await lectura.Permissions.Where(p => p.Module == "Inventario").Select(p => p.Name).ToListAsync();
            Assert.Contains("showInventory", modulo);
            Assert.Contains("adjustInventory", modulo);

            Assert.Contains("showInventory", UserRolesSeeder.SupervisorPermissions);
            Assert.DoesNotContain("adjustInventory", UserRolesSeeder.SupervisorPermissions);
            Assert.DoesNotContain("showInventory", UserRolesSeeder.CashierPermissions);
        }
    }
}
