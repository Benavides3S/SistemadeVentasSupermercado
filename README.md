# Sistema de Ventas para Supermercado

Aplicación web de punto de venta y administración para supermercados en Colombia:

- **Inventario:** productos por unidad o por peso (kg, lb, litro), categorías activas/inactivas.
- **Punto de venta:** buscador, carrito, cobro en efectivo, tarjeta o transferencia; varios cajeros a la vez sin vender stock que no existe.
- **Caja:** apertura con monto inicial, cierre con arqueo ciego (el cajero cuenta sin ver el esperado), reporte de cierre.
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

   Al arrancar se crean (si no existen) los permisos, el rol **Admin**, categorías y productos de ejemplo y los usuarios iniciales. En ambiente *Development* también se crean dos usuarios de prueba sin el rol Admin (`anad@yopmail.com` y `jhond@yopmail.com`), útiles para probar permisos.

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
- Cubren ventas (incluidos varios cajeros a la vez), caja, anulaciones, devoluciones, reportes y formatos.

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
| Reportes | `showReports` | Ver y exportar reportes |
| Productos, Categorías, Clientes, Usuarios, Roles | `show*`, `create*`, `update*`, `delete*` | Administración |

Un cajero típico necesita `createSales` y `manageCashSessions`. Un supervisor, además, `showSales`, `cancelSales`, `createReturns`, `showCashSessions` y `showReports`.

## Flujo diario

1. El cajero entra a **Mi caja** y la abre con la base en efectivo.
2. Vende en el **Punto de venta**. Sin caja abierta no se puede vender.
3. Si hay un error, un supervisor **anula** la venta desde su comprobante (solo con el turno abierto). Si el cliente vuelve después, se hace una **devolución**.
4. Al final del turno, el cajero cuenta el efectivo y **cierra la caja**. El reporte de cierre muestra sobrante o faltante.

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
