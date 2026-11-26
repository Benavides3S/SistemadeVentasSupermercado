# Sistema de Ventas para Supermercado

Aplicación web de punto de venta y administración para supermercados en Colombia:

- **Inventario:** productos por unidad o por peso (kg, lb, litro), categorías activas/inactivas.
- **Punto de venta:** buscador, carrito, cobro en efectivo, tarjeta o transferencia; varios cajeros a la vez sin vender stock que no existe.
- **Caja:** apertura con monto inicial, cierre con arqueo ciego (el cajero cuenta sin ver el esperado), reporte de cierre.
- **Proveedores y compras:** registro de la mercancía que llega (suma el stock y guarda el último costo de cada producto), historial y anulación.
- **Anulaciones y devoluciones parciales** que devuelven el stock y ajustan el efectivo esperado.
- **Autorización de supervisor:** un supervisor autoriza con su PIN, en la caja del cajero, una anulación o devolución.
- **Reportes:** ventas por día, productos más vendidos y actividad de caja por cajero, con exportación a Excel e impresión a PDF.
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
- **Bloqueo:** 5 intentos fallidos bloquean la cuenta 15 minutos, o hasta que alguien con `resetPasswords` la desbloquee (sin cambiar la contraseña) o la restablezca.
- **Desactivar usuarios:** quien ya no trabaja se **desactiva** (no se borra: sus ventas y turnos se conservan). Pierde el acceso de inmediato, aunque tenga la sesión abierta. Si dejó la caja abierta, el sistema avisa y alguien con `showCashSessions` debe cerrarla en **Turnos de caja**. Se puede reactivar. Nadie se desactiva a sí mismo, solo un Admin desactiva o reactiva a un Admin y siempre queda un Admin activo.
- **Actividad de usuarios:** en el menú **Actividad de usuarios** (permiso `showUserActivity`, al principio solo el Admin) se ve quién entró o intentó entrar, y quién cambió usuarios, contraseñas, roles y permisos. No guarda contraseñas. Los intentos fallidos repetidos en el mismo minuto salen en una línea con un contador. Hoy no se borra nada automáticamente. La dirección IP es solo una pista: con Docker puede ser una dirección interna.
- **Cambiar contraseña:** cualquiera, desde el menú de su nombre (arriba a la derecha).
- **Protección del rol Admin:** solo un Admin puede crear o editar usuarios Admin, asignar ese rol o restablecer la contraseña de un Admin. Nadie puede cambiar su propio rol y siempre queda al menos un Admin.

## Autorización de supervisor

Para cuando el cajero no tiene permiso de anular o devolver y no se quiere cambiar de usuario en su caja.

1. **Una sola vez:** cada persona que autoriza (Admin, o un rol con `cancelSales` o `createReturns`) entra con su usuario, abre el menú de su nombre → **Mi PIN de autorización**, confirma su contraseña y crea un PIN de 6 dígitos. No se aceptan números iguales, seguidos o repetidos.
2. El cajero entra a **Anular o devolver** (en el menú o en el punto de venta) y escribe el **número del comprobante**.
3. Escoge la operación, escribe el motivo (y en una devolución, las cantidades).
4. El supervisor revisa, elige su nombre y escribe su PIN. Autoriza **solo esa operación**: la sesión del cajero no gana permisos.

- Quedan guardados quién hizo la operación y quién la autorizó: en el comprobante, el reporte por cajero (y su Excel) y **Actividad de usuarios**, donde también salen los intentos rechazados.
- **5 errores** bloquean ese PIN 15 minutos (no el inicio de sesión). Para no esperar, el supervisor puede crear un PIN nuevo en **Mi PIN de autorización**, que quita el bloqueo.
- **Restablecer la contraseña** de alguien borra su PIN: debe crear otro. Quien está desactivado, bloqueado o con contraseña temporal no puede autorizar.
- Las reglas no cambian: solo se anula con el turno de la venta abierto, y el efectivo de una devolución sale de la caja de quien la hace.
- **Cuidados:** el PIN no sirve para iniciar sesión, pero quien lo vea podría usarlo desde su sesión: que nadie mire al escribirlo y cámbialo si hay dudas. Un cajero puede bloquearle el PIN a un supervisor equivocándose a propósito (queda registrado). Como el cajero puede consultar ventas por número, podría ir sumando las suyas antes del arqueo: la búsqueda no tiene lista para no facilitarlo.

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
- **Permisos de punta a punta:** unas pruebas arrancan la aplicación completa (base propia `SistemaVentasTestsWeb`, mismo servidor), inician sesión con cada rol de ejemplo y comprueban qué pantallas de Proveedores, Compras, Inventario, comprobantes de devolución y reportes de caja puede abrir cada uno. También recorren la autorización de supervisor, incluidos formularios manipulados. Otra prueba revisa que toda acción de los controladores tenga `[CustomAuthorize]` o `[AllowAnonymous]`: si agregas una acción sin protección, falla.
- **GitHub Actions:** `.github/workflows/pruebas.yml` compila y corre las pruebas en cada push y pull request, con un SQL Server 2022 desechable.
- Cubren ventas (incluidos varios cajeros a la vez), compras (reintentos, anulación y compras y ventas simultáneas), caja, anulaciones, devoluciones, reportes, formatos, edición de productos y contraseñas y roles de usuarios.

## Permisos

El rol **Admin** tiene acceso a todo. Para los demás roles, asigna permisos en **Roles**:

| Módulo | Permiso | Para qué |
|---|---|---|
| Ventas | `createSales` | Usar el punto de venta y pedir autorización para anular o devolver |
| Ventas | `showSales` | Ver historial y comprobantes (de todos los cajeros) |
| Ventas | `cancelSales` | Anular ventas mientras su turno de caja siga abierto, y autorizar anulaciones con PIN |
| Ventas | `createReturns` | Hacer devoluciones, y autorizarlas con PIN |
| Caja | `manageCashSessions` | Abrir y cerrar su propia caja |
| Caja | `showCashSessions` | Ver todos los turnos y cerrar cajas olvidadas |
| Compras | `showPurchases` | Ver el historial y el detalle de las compras |
| Compras | `createPurchases` | Registrar compras a proveedores (suma el stock) |
| Compras | `cancelPurchases` | Anular compras (resta el stock, solo si todavía alcanza) |
| Inventario | `showInventory` | Ver el historial de movimientos de inventario |
| Inventario | `adjustInventory` | Ajustar el stock a mano (mermas, pérdidas, conteo físico) |
| Reportes | `showReports` | Ver y exportar reportes |
| Reportes | `showCashierReport` | Ver y exportar el reporte por cajero, con datos de cada empleado (al principio solo el Admin) |
| Productos, Categorías, Proveedores, Clientes, Usuarios, Roles | `show*`, `create*`, `update*`, `delete*` | Administración |
| Usuarios | `resetPasswords` | Restablecer la contraseña de un usuario (genera una temporal) y desbloquear cuentas |
| Usuarios | `deactivateUsers` | Desactivar y reactivar usuarios (al principio solo el Admin) |
| Usuarios | `showUserActivity` | Ver el historial de actividad de todos los usuarios, incluidos los Admin (al principio solo el Admin) |

El sistema crea los roles **Cajero** (`createSales` y `manageCashSessions`) y **Supervisor** (lo del cajero, más `showSales`, `cancelSales`, `createReturns`, `showCashSessions`, `showReports`, `showPurchases`, `createPurchases`, `cancelPurchases` y `showInventory`). Puedes cambiarles los permisos en **Roles**; el sistema no los vuelve a poner. En una instalación que ya existía, el rol **Supervisor** no recibe los permisos de Compras ni de Inventario solo: márcalos en **Roles**. El permiso `adjustInventory` no se da a ningún rol de ejemplo: solo el Admin ajusta stock hasta que lo marques.

## Flujo diario

1. El cajero entra a **Mi caja** y la abre con la base en efectivo.
2. Vende en el **Punto de venta**. Sin caja abierta no se puede vender. Con un **lector de código de barras** USB (cualquiera que funcione como teclado) basta con escanear: el producto se agrega al carrito; si el código no existe o el producto no tiene stock, suena un pitido y aparece el motivo bajo el buscador. El código de cada producto se registra en **Productos** (se puede escanear sobre el campo).
3. Si hay un error, un supervisor **anula** la venta desde su comprobante (solo con el turno abierto). Si el cliente vuelve después, se hace una **devolución**. El supervisor también puede autorizarlo con su PIN en la caja del cajero (ver **Autorización de supervisor**).
4. Al final del turno, el cajero cuenta el efectivo y **cierra la caja**. El reporte de cierre muestra sobrante o faltante.

### Cuando llega mercancía

1. Crea el proveedor en **Proveedores** (si es nuevo).
2. En **Compras → Registrar compra**, elige el proveedor, escribe la factura (opcional) y agrega los productos escaneando o buscando. Cada línea lleva la **cantidad** y el **costo por unidad** (se precarga el último costo). Aquí aparecen también los productos sin stock.
3. Al guardar, el stock de cada producto sube y su **último costo** pasa a ser el de esa compra (se ve en la lista de **Productos**). Si repites la factura de un proveedor, el sistema **avisa**, pero deja registrar la compra.
4. Si hubo un error, se **anula** desde el detalle de la compra (con motivo). Solo se puede si todavía queda stock suficiente: si parte de esa mercancía ya se vendió, el sistema lo rechaza y dice qué producto falta.

### Inventario

- **Inventario** muestra cada movimiento de stock: ventas, anulaciones, devoluciones, compras, ajustes y el stock inicial, con quién, cuándo y cuánto quedó. Filtra por producto, tipo, fechas o texto; desde la lista de **Productos** hay un botón de historial por producto.
- Para corregir el stock (merma, pérdida, conteo físico, consumo interno) usa **Ajustar stock**: eliges entrada o salida, cantidad y motivo (con notas si es "Otro"). El stock ya no se edita en el formulario de Editar producto.
- Al instalar esta versión, cada producto con stock recibe un movimiento "Saldo inicial del historial": el historial empieza ahí, no reconstruye lo anterior.

## Reporte por cajero

En el menú **Reporte por cajero** se ve, entre dos fechas, lo que hizo cada persona en la caja: ventas cobradas, anulaciones, devoluciones y descuadres. Tocando un nombre sale el detalle. Se puede descargar en Excel o guardar como PDF desde la ventana de impresión.

- **Quién cobró y quién corrigió van por separado.** No sumes esas columnas: una misma operación puede estar en las dos.
- **Faltantes y sobrantes no se compensan.** Un turno de varios días puede incluir ventas anteriores a las fechas elegidas.
- **Si tienes tu caja abierta,** no ves tus propias cifras y los totales salen parciales. Esto protege el conteo a ciegas solo en este reporte; con permisos sobre las ventas o el reporte por día se pueden obtener por otro lado.
- El reporte indica qué revisar; no acusa a nadie. Tiene datos de empleados: comparte los archivos solo con quien deba verlos.

## Pantalla de Inicio

Al entrar sin sesión se pide iniciar sesión. El Inicio muestra, según tus permisos: accesos rápidos, las ventas de hoy, un aviso si no tienes la caja abierta y los productos con **5 unidades o menos** de stock (con enlace a su historial y a ajustar).

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
- La unidad de medida de un producto no se puede cambiar después de crearlo.
- El producto guarda solo el **último** costo de compra (no hay historial de costos). Al anular una compra, el costo vuelve al de la compra anterior si esa era la más reciente.
- Un proveedor con compras no se puede borrar: se desactiva. Tampoco hay pagos ni cuentas por pagar.
