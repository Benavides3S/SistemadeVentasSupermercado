# Sistema de Ventas para Supermercado

## Resumen
Aplicación web de punto de venta y administración para un supermercado: inventario (productos por unidad o por peso, categorías), clientes, usuarios con roles y permisos, y ventas con varios cajeros a la vez (en construcción). Proyecto único: `SistemadeVentasSupermercado.Web` (ASP.NET Core 8 MVC, vistas Razor, EF Core 8 + SQL Server, ASP.NET Identity). Pruebas en `SistemadeVentasSupermercado.Tests` (xUnit).

**Sobre el dueño:** es estudiante y está aprendiendo. Explica tus decisiones en lenguaje simple y en español. Meta a largo plazo: poder venderlo a negocios reales en Colombia, así que importan la seguridad, la calidad y las licencias.

Código, textos de la interfaz, comentarios y mensajes van en español.

## Comandos
Desde `SistemadeVentasSupermercado.Web/` (o indicando la ruta del `.csproj` / `.sln`):

```bash
dotnet build
dotnet run        # http://localhost:5234 (perfil "http"); --launch-profile https → https://localhost:7147
```

- Base de datos: cadena `MyConnection` en `appsettings.json` → `(localdb)\MSSQLLocalDB`, base `SistemaVentasSuperMercado`.
- **Migraciones:** no se aplican solas al arrancar. En esta máquina la herramienta global `dotnet ef` es la versión 11 y NO funciona con EF 8: no la uses ni la reinstales. Las migraciones se crean y aplican desde la Consola del Administrador de paquetes de Visual Studio (`Add-Migration <Nombre>`, `Update-Database`). Si necesitas una migración, dime el nombre y qué debe contener, y espera a que yo la genere y te la muestre.
- **Secretos:** `dotnet user-secrets set "Seed:PasswordInicial" "<clave>"`. Nunca escribir claves en el código.
- Reglas de contraseña: mínimo 8 caracteres, mayúscula, minúscula, dígito y 4 caracteres distintos.
- Pruebas: `dotnet test` desde la carpeta del `.sln`. Usan la base `SistemaVentasTests` (cadena en `SistemadeVentasSupermercado.Tests/appsettings.Tests.json`, se puede reemplazar con la variable de entorno `ConnectionStrings__TestsConnection`). `Infraestructura/BaseDeDatosFixture` la borra y recrea con las migraciones en cada ejecución, y se niega a correr si la base tiene otro nombre. Las pruebas corren en serie; cada prueba crea sus propios productos (con sufijo único, porque `Product.Name` es único) y cada "cajero" usa su propio `DataContext`.

## Arquitectura
- **Inicio:** `Program.cs` es mínimo. DI, Identity, cookie (sesión de 8 h), AutoMapper, notificaciones Notyf y registro de servicios viven en `CustomConfiguration.cs` (`AddCustomConfiguration` / `AddCustomWebApplicationConfiguration`). Todo servicio nuevo se registra en `AddServices` ahí.
- **Cultura:** `InvariantCulture` forzada globalmente: los decimales se envían y leen con punto (`1.5`). Inputs numéricos con `type="number" step="any"`. No introducir formatos numéricos por cultura en formularios. Para MOSTRAR dinero al usuario, usar formato es-CO: en Razor `Core/DisplayFormat` (`Money`, `Quantity`, `DateTimeColombia`; las fechas se guardan en UTC y se muestran en hora de Colombia) y en JavaScript `Intl.NumberFormat('es-CO')`.
- **Antiforgery:** `AutoValidateAntiforgeryTokenAttribute` es un filtro global: toda petición no GET (incluido AJAX/fetch) debe enviar el token antiforgery.
- **Capas:** Controller → `Services/Abstractions/I*Service` → `Services/Implementations/*` → `Data/DataContext`. Los servicios trabajan con DTOs (`DTOs/`), mapeados con `Core/AutoMapperProfile.cs`, y devuelven `Core/Response<T>` (`IsSuccess`, `Message`, `Errors`, `Result`; se crea con `Response<T>.Success/Failure`). Los servicios heredan de `CustomQueryableOperationsService` (CreateAsync, EditAsync, DeleteAsync, GetOneAsync, GetCompleteListAsync, GetPaginationAsync). Los controladores revisan `IsSuccess` y avisan con `INotyfService` (toast) antes de redirigir o volver a mostrar la vista.
- **Paginación:** `PaginationRequest` (query string) → el servicio arma `PaginationResponse<T>` con `QueryableExtensions.PaginateAsync`; las vistas usan `Views/Shared/_PaginationComponent.cshtml`.
- **Listas desplegables:** `Helpers/ICombosHelper` devuelve `SelectListItem` y se pasan por `ViewBag`.
- **Vistas:** layout por defecto `_MyLayout` (plantilla NiceAdmin en `wwwroot/assets`, con parciales `_Header`, `_Sidebar`, `_Footer`). Los toasts se dibujan con `@await Component.InvokeAsync("Notyf")` al final de `_MyLayout`. `Login` no usa layout.
- **Seeders:** corren en cada arranque (`CustomConfiguration.SeedData` → `Data/Seeders/SeedDb`): categorías y productos de ejemplo, permisos, rol Admin y usuarios. Los usuarios iniciales solo se crean si existe `Seed:PasswordInicial`; los de prueba (anad, jhond) solo en Development.

## Autorización (sistema propio de permisos)
Dos capas:
1. ASP.NET Identity solo para autenticación (`User : IdentityUser`, cookie "Auth", login en `/Account/Login`). Los roles de Identity NO se usan para autorizar.
2. Roles y permisos propios: `User.SistemaVentasRoleId` → `SistemaVentasRole` ↔ `RolePermission` ↔ `Permission` (`Name` como `showProducts`, `Module` como `Productos`).

Las acciones se protegen con `[CustomAuthorize(permission, module)]` (`Core/Attributes/CustomAuthorizeAttribute.cs`), que llama a `IUsersService.CurrentUserIsAuthorizedAsync`. **Primero el permiso, luego el módulo:** invertirlos falla en silencio. El rol `Env.SUPER_ADMIN_ROLE_NAME` ("Admin") se salta TODAS las verificaciones, así que los errores de permisos solo se notan probando con otros roles (anad@ y jhond@ en desarrollo).

Convención de nombres: permisos en plural (`createClients`, `showCategories`), módulo sin tildes ("Categorias"). Al crear un módulo, agregar sus permisos en `Data/Seeders/PermissionsSeeder.cs` Y también incluir esa lista en `SeedAsync`.

## Modelo de datos
- `DataContext : IdentityDbContext<User>`; claves, FKs e índices en `ConfigureKeys` / `ConfigureIndexes`. Las FKs relacionadas con ventas usan `Restrict` (salvo `SaleItem → Sale`, que es cascade): no se puede borrar un producto, cliente o usuario con ventas.
- Productos por unidad o por peso (`UnitOfMeasure`: Unidad, Kilogramo, Libra, Litro). `Stock` y `SaleItem.Quantity` son `decimal(18,3)`; el dinero es `decimal(18,2)`. Restricción en base de datos: `Stock >= 0`.
- `Category`: nombre único (sin distinguir mayúsculas) y estado activa/inactiva. `Product.CategoryId` es obligatorio. No se borra una categoría con productos: se desactiva.
- `Sale` (con `Number` identity, cajero, cliente opcional, turno de caja `CashSessionId` (vacío en ventas anteriores a la caja), método de pago, estado) y `SaleItem` (guarda copia del nombre, precio y unidad al momento de la venta).
- `CashSession` (turno de caja por cajero, no por computador): monto inicial, estado Abierta/Cerrada y datos del cierre (contado, esperado, diferencia, quién cerró). Índice único filtrado `IX_CashSessions_UserId_OneOpen` (`[Status] = 1`): un solo turno abierto por cajero, garantizado por la base de datos. Efectivo esperado = monto inicial + ventas completadas en efectivo (tarjeta y transferencia no van al cajón). Un turno cerrado no se modifica.
- `SalesService.CreateAsync` corre dentro de una transacción; exige turno de caja abierto del cajero y **bloquea la fila del turno** (`ExecuteUpdate` sin cambio real) antes de tocar productos; une líneas repetidas y las ordena por `ProductId` (evita bloqueos entre cajeros); rechaza cantidades decimales en productos por unidad y productos de categorías desactivadas, y descuenta el stock de forma atómica con `ExecuteUpdateAsync` y `Stock >= cantidad`. Orden de bloqueo: turno → productos por `ProductId`. `CashSessionService.CloseAsync` marca el turno como cerrado de forma atómica (solo si sigue abierto) ANTES de calcular el esperado, así espera a las ventas en curso y ninguna venta entra a un turno cerrado. Conservar estas propiedades al modificar la lógica de ventas o caja.

## Cómo crear un módulo nuevo
Copiar el patrón de `Category`: DTO, `I*Service` + servicio, controlador, vistas Index/Create/Edit, entrada en `Views/Shared/_Sidebar.cshtml`, permisos en `PermissionsSeeder` (y en `SeedAsync`) y registro con `AddScoped` en `AddServices`. En `AutoMapperProfile`, al mapear de DTO a entidad con propiedades de navegación, usar `.Ignore()` en la navegación (si no, AutoMapper intenta crear registros nuevos).

## Estado actual (actualizar al terminar cada etapa)
**Hecho:**
- Seguridad: política de contraseñas fuerte, sesión de 8 horas, contraseña inicial por User Secrets, permisos corregidos, validaciones de producto, CSRF global, notificaciones funcionando.
- Productos por peso y unidad de medida.
- Categorías como tabla administrable (sus permisos ya se siembran en `SeedAsync`).
- Tablas `Sales` y `SaleItems` + `SalesService.CreateAsync`.
- Pruebas automáticas de `SalesService` (creación con concurrencia y rollback, buscadores, historial, comprobante) y de `DisplayFormat`.
- Pantalla de venta (`SalesController`): punto de venta `/Sales/Create` (JS en `wwwroot/js/pos.js`; cobro por fetch con el token antiforgery en el encabezado `RequestVerificationToken`; envía solo `ProductId` y cantidad), historial `/Sales/Index` y comprobante `/Sales/Details/{id}` para impresora térmica de 80 mm. Permisos del módulo "Ventas": `createSales` (punto de venta) y `showSales` (historial y comprobantes; ve las ventas de todos los cajeros). El buscador solo muestra productos con stock y de categorías activas. El cambio en efectivo se calcula solo en pantalla (no se guarda). Datos del negocio del comprobante en la sección `Negocio` de `appsettings.json` (`Core/BusinessOptions`).
- Caja (`CashSessionsController`): "Mi caja" `/CashSessions/Current` (abrir; cerrar con **arqueo ciego**: el cajero no ve el esperado hasta después de cerrar), lista de turnos `/CashSessions/Index` y reporte de cierre `/CashSessions/Report/{id}` en 80 mm (estilos compartidos en `wwwroot/css/receipt.css`). Permisos del módulo "Caja": `manageCashSessions` (abrir/cerrar la propia) y `showCashSessions` (ver todos los turnos y cerrar los que otro olvidó). `Report` revisa los permisos dentro de la acción (supervisor, o el dueño solo con el turno ya cerrado). El punto de venta redirige a "Mi caja" si no hay turno abierto.

**Problemas conocidos (corregir cuando se pueda):**
- AutoMapper 12.0.1 tiene una vulnerabilidad conocida (GHSA-rvv3-g6hj-g44x) y las versiones corregidas (15.1.1+ y 16.1.1+) son comerciales. Decisión pendiente entre la licencia Community, Mapperly o mapeo manual. NO actualizar sin consultarme.
- Restos de otra plantilla: permisos y rol de "Secciones" y "Blogs" (rol "Gestor de contenido"), título "PrivateBlog" y mensajes como "Sección obtenida con éxito".
- `Response.Failure(string)` deja `Errors` en null.
- Los productos de ejemplo se siembran también fuera de Development.
- Solo los servicios de ventas y caja tienen pruebas automáticas; el resto de servicios, los controladores y `pos.js` no. En las pruebas, el cajero principal del fixture tiene siempre la caja abierta; las pruebas que cierran turnos crean su propio cajero con `CrearCajeroAsync`.
- Si la red se corta después de que el servidor registró una venta pero antes de que llegue la respuesta, el cajero ve un error aunque la venta sí quedó; si vuelve a cobrar, se duplica. Revisar el historial en ese caso. Solución futura: clave de idempotencia por venta (requiere migración).

## Decisiones de diseño (no cambiarlas sin preguntarme)
- Varios cajeros a la vez: el descuento atómico de stock es obligatorio.
- Los precios de una venta SIEMPRE salen de la base de datos, nunca del navegador: la pantalla solo envía `ProductId` y cantidad.
- Productos por unidad: solo cantidades enteras. Por peso: decimales.
- Sin IVA por ahora. La facturación electrónica DIAN se hará integrando un proveedor autorizado (no software propio); los requisitos se confirman con un contador.

## Plan por etapas
1. **(Hecho)** Pruebas automáticas del servicio de ventas: proyecto xUnit `SistemadeVentasSupermercado.Tests`, con base de datos SEPARADA `SistemaVentasTests` en LocalDB (se borra y recrea con las migraciones; nunca tocar la base de desarrollo). Casos: venta simple, stock insuficiente, unidad con decimales, peso con decimales, líneas repetidas, rollback si una línea falla, venta vacía o método de pago inválido, dos cajeros por la última unidad, veinte ventas simultáneas con stock 10.
2. **(Hecho)** **Pantalla de venta (POS):** buscador de productos, carrito, método de pago, cobro con `ISalesService`, historial de ventas y comprobante imprimible. Módulo de permisos "Ventas". Si el cobro se envía con fetch/AJAX, debe incluir el token antiforgery.
3. **(En curso)** Caja, anulaciones y devoluciones. Decisiones tomadas: caja por cajero; anular solo con el turno de la venta abierto (después, devolución); en devoluciones se elige por línea si vuelve al inventario; arqueo ciego.
   - Paso 1 **(Hecho)**: caja (migración `Caja`), apertura, cierre, arqueo y reporte.
   - Paso 2 **(Hecho)**: anulación de ventas (migración `AnulacionVentas`: `Sales.CancelledByUserId`). `SalesService.CancelAsync`: motivo obligatorio (máx. 200); bloquea el turno de la venta (si ya cerró, se rechaza y se indica hacer devolución; las ventas sin turno tampoco se anulan); cambio a "Anulada" atómico (solo si sigue "Completada", así dos anulaciones simultáneas no devuelven el stock dos veces); devolución de stock atómica ordenada por `ProductId`. Botón "Anular venta" en el comprobante (`POST /Sales/Cancel/{id}`, permiso `cancelSales`), visible solo si `SaleDetailDTO.CanBeCancelled`. Las ventas anuladas no cuentan en el efectivo esperado.
   - Paso 3: devoluciones parciales (migración `Devoluciones`: `SaleReturns`, `SaleReturnItems`, `Sales.ReturnedTotal`). Permiso `createReturns`. No devolver más de lo vendido (bloquear la fila de la venta), reembolso al precio original y con el mismo método de pago; si es efectivo, sale del turno abierto de quien devuelve y resta del esperado.
4. Reportes (ventas por día, más vendidos, exportar a Excel o PDF), README, Docker y copias de seguridad.
5. Más adelante: código de barras/SKU, proveedores y compras, IVA por producto, facturación electrónica con proveedor, reemplazo de AutoMapper, limpieza de restos de plantilla.

## Reglas de trabajo
- Antes de cambios grandes, muéstrame un plan y espera mi aprobación.
- No ejecutes `Update-Database` ni `Drop-Database` sin preguntarme. No modifiques migraciones existentes: crea siempre una nueva y revisa que su `Up` solo contenga lo esperado.
- No instales ni actualices herramientas globales de dotnet.
- Nunca escribas contraseñas ni cadenas de conexión reales en el código: van en User Secrets.
- Nunca uses `git push --force`. Commits pequeños con mensaje en español. Trabaja en la rama actual, no en `main`.
- Después de cada cambio, ejecuta `dotnet build` (y `dotnet test` cuando existan pruebas) y dime el resultado.
- Si una prueba falla por un posible error real del código, explícame la causa antes de corregirlo.