# Sistema de Ventas para Supermercado

Aplicación web de punto de venta y administración para supermercados en Colombia:

- **Inventario:** productos por unidad o por peso (kg, lb, litro), categorías activas/inactivas.
- **Punto de venta:** buscador, carrito, cobro en efectivo, tarjeta o transferencia; varios cajeros a la vez sin vender stock que no existe.
- **Caja:** apertura con monto inicial, cierre con arqueo ciego (el cajero cuenta sin ver el esperado), reporte de cierre.
- **Proveedores y compras:** registro de la mercancía que llega (suma el stock y guarda el último costo de cada producto), historial y anulación.
- **Anulaciones y devoluciones parciales** que devuelven el stock y ajustan el efectivo esperado.
- **Reportes:** ventas por día y productos más vendidos, con exportación a Excel e impresión a PDF.
- **Usuarios, roles y permisos** propios por módulo.
- Comprobantes para **impresora térmica de 80 mm**.

> ⚠️ Los comprobantes **no son facturas electrónicas**. La facturación electrónica DIAN se hará integrando un proveedor autorizado.

Tecnología: ASP.NET Core 8 MVC (Razor), Entity Framework Core 8, SQL Server, ASP.NET Identity, xUnit.

---

## Requisitos

- [.NET 8 SDK](https://dotnet.microsoft.com/download/dotnet/8.0)
- SQL Server: en desarrollo basta **LocalDB** (viene con Visual Studio). En un negocio, **SQL Server Express** (gratis, hasta 10 GB por base de datos).
- Visual Studio 2022 (recomendado, para crear y aplicar migraciones).

## Puesta en marcha (desarrollo)

1. **Cadena de conexión.** En `SistemadeVentasSupermercado.Web/appsettings.json`, `ConnectionStrings:MyConnection` apunta a LocalDB (base `SistemaVentasSuperMercado`).

2. **Contraseña de los usuarios iniciales** (nunca se escribe en el código). Desde `SistemadeVentasSupermercado.Web/`:

   ```bash
   dotnet user-secrets set "Seed:PasswordInicial" "<una clave fuerte>"
   ```

   Reglas: mínimo 8 caracteres, mayúscula, minúscula, número y 4 caracteres distintos. Sin esta clave no se crean usuarios.

3. **Datos del negocio para los comprobantes.** El repositorio trae valores genéricos en la sección `Negocio` de `appsettings.json`. Pon los reales con User Secrets (en desarrollo) o variables de entorno (en producción):

   ```bash
   dotnet user-secrets set "Negocio:Nombre" "Mi Supermercado"
   dotnet user-secrets set "Negocio:Nit" "900123456-7"
   dotnet user-secrets set "Negocio:Direccion" "Calle 1 # 2-3"
   dotnet user-secrets set "Negocio:Telefono" "3001234567"
   ```

   En producción: variables `Negocio__Nombre`, `Negocio__Nit`, etc. (doble guion bajo).

4. **Base de datos.** Las migraciones **no se aplican solas** al arrancar. En Visual Studio, *Herramientas → Administrador de paquetes NuGet → Consola*, con el proyecto Web seleccionado:

   ```powershell
   Update-Database
   ```

5. **Ejecutar:**

   ```bash
   cd SistemadeVentasSupermercado.Web
   dotnet run            # http://localhost:5234
   ```

   Al arrancar se crean (si no existen) los permisos, los roles **Admin**, **Cajero** y **Supervisor**, categorías de ejemplo y los usuarios iniciales. En ambiente *Development* también se crean productos de ejemplo y dos usuarios de prueba: `anad@yopmail.com` (Cajero) y `jhond@yopmail.com` (Supervisor), útiles para probar permisos.

   El Admin inicial entra con `Seed:PasswordInicial` y el sistema le pide cambiarla en el primer inicio de sesión.

## Usuarios y contraseñas

- **Crear usuario:** el sistema genera una **contraseña temporal** y la muestra **una sola vez**. Entrégala en privado; al entrar, el usuario debe cambiarla por una propia.
- **Olvidó su contraseña:** en **Usuarios**, el botón 🔑 (permiso `resetPasswords`) genera una temporal nueva y desbloquea la cuenta.
- **Bloqueo:** 5 intentos fallidos bloquean la cuenta 15 minutos (o hasta que se restablezca).
- **Cambiar contraseña:** cualquiera, desde el menú de su nombre (arriba a la derecha).
- **Protección del rol Admin:** solo un Admin puede crear o editar usuarios Admin, asignar ese rol o restablecer la contraseña de un Admin. Nadie puede cambiar su propio rol y siempre queda al menos un Admin.

## Instalación con Docker (para un negocio)

Ver la guía [docker/LEEME.md](docker/LEEME.md): primera instalación, actualizaciones, HTTPS y seguridad.

## Copias de seguridad

Copia completa verificada, con fecha en el nombre y borrado automático de las viejas, programable cada día en Windows, para SQL Server instalado o para Docker. Incluye cómo restaurar. Ver [scripts/LEEME.md](scripts/LEEME.md).

## Pruebas automáticas

```bash
dotnet test           # desde la carpeta del .sln
```

- Usan una base **separada**, `SistemaVentasTests` (cadena en `SistemadeVentasSupermercado.Tests/appsettings.Tests.json`, o la variable de entorno `ConnectionStrings__TestsConnection`).
- **Esa base se borra y se recrea** con las migraciones en cada ejecución. Por seguridad, las pruebas se niegan a correr si la cadena apunta a una base con otro nombre.
- Cubren ventas (incluidos varios cajeros a la vez), compras (reintentos, anulación y compras y ventas simultáneas), caja, anulaciones, devoluciones, reportes, formatos, edición de productos y contraseñas y roles de usuarios.

## Permisos

El rol **Admin** tiene acceso a todo. Para los demás roles, asigna permisos en **Roles**:

| Módulo | Permiso | Para qué |
|---|---|---|
| Ventas | `createSales` | Usar el punto de venta |
| Ventas | `showSales` | Ver historial y comprobantes (de todos los cajeros) |
| Ventas | `cancelSales` | Anular ventas mientras su turno de caja siga abierto |
| Ventas | `createReturns` | Hacer devoluciones |
| Caja | `manageCashSessions` | Abrir y cerrar su propia caja |
| Caja | `showCashSessions` | Ver todos los turnos y cerrar cajas olvidadas |
| Compras | `showPurchases` | Ver el historial y el detalle de las compras |
| Compras | `createPurchases` | Registrar compras a proveedores (suma el stock) |
| Compras | `cancelPurchases` | Anular compras (resta el stock, solo si todavía alcanza) |
| Reportes | `showReports` | Ver y exportar reportes |
| Productos, Categorías, Proveedores, Clientes, Usuarios, Roles | `show*`, `create*`, `update*`, `delete*` | Administración |
| Usuarios | `resetPasswords` | Restablecer la contraseña de un usuario (genera una temporal) |

El sistema crea los roles **Cajero** (`createSales` y `manageCashSessions`) y **Supervisor** (lo del cajero, más `showSales`, `cancelSales`, `createReturns`, `showCashSessions`, `showReports`, `showPurchases`, `createPurchases` y `cancelPurchases`). Puedes cambiarles los permisos en **Roles**; el sistema no los vuelve a poner. En una instalación que ya existía, el rol **Supervisor** no recibe los permisos de Compras solo: márcalos en **Roles**.

## Flujo diario

1. El cajero entra a **Mi caja** y la abre con la base en efectivo.
2. Vende en el **Punto de venta**. Sin caja abierta no se puede vender. Con un **lector de código de barras** USB (cualquiera que funcione como teclado) basta con escanear: el producto se agrega al carrito; si el código no existe o el producto no tiene stock, suena un pitido y aparece el motivo bajo el buscador. El código de cada producto se registra en **Productos** (se puede escanear sobre el campo).
3. Si hay un error, un supervisor **anula** la venta desde su comprobante (solo con el turno abierto). Si el cliente vuelve después, se hace una **devolución**.
4. Al final del turno, el cajero cuenta el efectivo y **cierra la caja**. El reporte de cierre muestra sobrante o faltante.

### Cuando llega mercancía

1. Crea el proveedor en **Proveedores** (si es nuevo).
2. En **Compras → Registrar compra**, elige el proveedor, escribe la factura (opcional) y agrega los productos escaneando o buscando. Cada línea lleva la **cantidad** y el **costo por unidad** (se precarga el último costo). Aquí aparecen también los productos sin stock.
3. Al guardar, el stock de cada producto sube y su **último costo** pasa a ser el de esa compra (se ve en la lista de **Productos**). Si repites la factura de un proveedor, el sistema **avisa**, pero deja registrar la compra.
4. Si hubo un error, se **anula** desde el detalle de la compra (con motivo). Solo se puede si todavía queda stock suficiente: si parte de esa mercancía ya se vendió, el sistema lo rechaza y dice qué producto falta.

## Reglas importantes del diseño

- Los precios de una venta **siempre** salen de la base de datos; el navegador solo envía producto y cantidad.
- El stock se descuenta de forma **atómica**: dos cajeros no pueden vender la última unidad a la vez.
- Productos por unidad: solo cantidades enteras. Por peso: decimales.
- Las fechas se guardan en UTC y se muestran en hora de Colombia (UTC−5).
- Sin IVA por ahora.

## Funcionamiento sin internet

El sistema funciona en la red local sin internet. La única dependencia externa es **Google Fonts** (tipografía de la plantilla): sin internet se usa una tipografía del sistema y todo sigue funcionando.

## Licencias

El código de terceros y sus licencias están en [THIRD-PARTY-NOTICES.md](THIRD-PARTY-NOTICES.md). Antes de vender o distribuir el sistema, revisa en especial la sección de la plantilla **NiceAdmin**.

## Problemas conocidos

- Si la red se corta justo después de cobrar, el cajero puede volver a cobrar el mismo carrito sin riesgo: el sistema reconoce el reintento y no registra la venta dos veces. Pero si **recarga la página (F5)** después del error, el carrito se pierde; en ese caso revisar el historial antes de armar la venta de nuevo.
- Al editar un producto, el stock se guarda como **ajuste** (la diferencia con lo que se veía al abrir el formulario), así no se pierden las ventas hechas mientras tanto. Todavía no hay historial de ajustes.
- La unidad de medida de un producto no se puede cambiar después de crearlo.
- El producto guarda solo el **último** costo de compra. Al anular una compra el stock se descuenta, pero el costo anterior no se restaura. Tampoco hay historial de movimientos de inventario todavía.
- Un proveedor con compras no se puede borrar: se desactiva. Tampoco hay pagos ni cuentas por pagar.
