# Componentes de terceros y licencias

Este sistema usa componentes de terceros. Cada uno conserva su propia licencia. Las versiones y licencias de los paquetes NuGet se verificaron en nuget.org; las de las librerías de JavaScript/CSS, en los encabezados de los archivos incluidos en `wwwroot`.

> Esta lista es una ayuda para cumplir las licencias, no asesoría legal. Antes de vender el sistema, confírmala con un abogado o con las páginas oficiales de cada licencia.

## ⚠️ Plantilla NiceAdmin (BootstrapMade) — pendiente

- Archivos: `wwwroot/assets` (CSS, JS e imágenes de la plantilla), vistas `_MyLayout`, `_Header`, `_Sidebar`, `_Footer`.
- Licencia: <https://bootstrapmade.com/license/>
- La versión gratuita **exige mantener el enlace de crédito "Designed by BootstrapMade"** en el pie de página. Actualmente **ese crédito no está** en `Views/Shared/_Footer.cshtml`.
- **Antes de distribuir o vender**: comprar la licencia de BootstrapMade que permite quitar el crédito, o volver a ponerlo.

## Paquetes NuGet (se distribuyen con la aplicación)

| Paquete | Versión | Licencia |
|---|---|---|
| Microsoft.EntityFrameworkCore / .SqlServer / .Tools | 8.0.34 | MIT |
| Microsoft.AspNetCore.Identity.EntityFrameworkCore | 8.0.34 | MIT |
| AspNetCoreHero.ToastNotification (notificaciones Notyf) | 1.1.0 | MIT |
| Newtonsoft.Json (versión corregida de la que trae ToastNotification) | 13.0.4 | MIT |
| ClosedXML | 0.105.1 | MIT |
| ClosedXML.Parser | 2.0.0 | MIT |
| DocumentFormat.OpenXml | 3.1.1 | MIT |
| ExcelNumberFormat | 1.1.0 | MIT |
| RBush.Signed | 4.0.0 | MIT |
| SixLabors.Fonts | 1.0.0 | Apache-2.0 |

### Nota sobre SixLabors.Fonts

Llega **indirectamente** a través de ClosedXML. La licencia de Six Labors ("Six Labors Split License", versiones 2.x) dice que el uso como *Transitive Package Dependency* (instalado por un paquete de otro autor) se rige por Apache 2.0, y la versión resuelta (1.0.0) es Apache 2.0 en todo caso.

**No agregar SixLabors.Fonts ni SixLabors.ImageSharp directamente al proyecto**: como dependencia directa, una empresa con ingresos anuales de 1 millón de dólares o más necesita una licencia comercial de Six Labors.

## Solo para desarrollo (no se distribuyen)

| Paquete | Licencia |
|---|---|
| xunit, xunit.runner.visualstudio | Apache-2.0 |
| Microsoft.NET.Test.Sdk | MIT |
| Microsoft.Extensions.Configuration.Json / .EnvironmentVariables | MIT |
| Microsoft.AspNetCore.Mvc.Testing 8.0.34 | MIT |

## Librerías de JavaScript y CSS (`wwwroot`)

| Librería | Versión | Licencia | Ubicación |
|---|---|---|---|
| Bootstrap | 5.3.3 / 5.1.0 | MIT | `assets/vendor/bootstrap`, `lib/bootstrap` |
| Bootstrap Icons | 1.11.3 | MIT | `assets/vendor/bootstrap-icons` |
| jQuery | 3.6.0 | MIT | `lib/jquery` |
| jQuery Validation | 1.19.5 | MIT | `lib/jquery-validation` |
| jQuery Validation Unobtrusive | 4.0.0 | MIT | `lib/jquery-validation-unobtrusive` |
| SweetAlert2 | 11.23.0 | MIT | `lib/sweetalert2` |

La plantilla NiceAdmin traía además ApexCharts, Chart.js, ECharts, Quill, TinyMCE, Remix Icon, Boxicons, simple-datatables y php-email-form. No se usaban y se quitaron del proyecto. Si alguna vez se vuelve a agregar una, revisar su licencia (ApexCharts 4+ y TinyMCE 7+ ya no son MIT).

### Nota sobre SweetAlert2

Desde la versión 11.4.9, SweetAlert2 incluye código que, en sitios con dominio ruso (.ru, .su, .рф) y navegador en ruso, cambia el comportamiento de la página. No afecta a instalaciones en Colombia, pero conviene saberlo.

## Tipografías externas

- **Google Fonts** (Open Sans, Nunito, Poppins), cargadas desde `fonts.googleapis.com`: licencia SIL Open Font License 1.1. El navegador de cada cajero se conecta a los servidores de Google para descargarlas.

## Servicios y software externo

- **SQL Server**: la edición *Express* es gratuita también en producción (máximo 10 GB por base de datos). La edición *Developer* es gratuita solo para desarrollo y pruebas.
