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
| CsvHelper | 33.1.0 | MS-PL o Apache-2.0 (a elección) |
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

## Librerías de JavaScript y CSS (`wwwroot`)

| Librería | Versión | Licencia | Ubicación |
|---|---|---|---|
| Bootstrap | 5.3.3 / 5.1.0 | MIT | `assets/vendor/bootstrap`, `lib/bootstrap` |
| Bootstrap Icons | 1.11.3 | MIT | `assets/vendor/bootstrap-icons` |
| jQuery | 3.6.0 | MIT | `lib/jquery` |
| jQuery Validation | 1.19.5 | MIT | `lib/jquery-validation` |
| jQuery Validation Unobtrusive | 4.0.0 | MIT | `lib/jquery-validation-unobtrusive` |
| SweetAlert2 | 11.23.0 | MIT | `lib/sweetalert2` |
| ApexCharts | 3.47.0 | MIT (las versiones 4+ cambiaron de licencia: no actualizar sin revisar) | `assets/vendor/apexcharts` |
| Chart.js | 4.4.2 | MIT | `assets/vendor/chart.js` |
| Apache ECharts | 5.5.0 | Apache-2.0 | `assets/vendor/echarts` |
| Quill | 1.3.7 | BSD-3-Clause | `assets/vendor/quill` |
| TinyMCE | 6.8.3 | MIT (las versiones 7+ son GPL o comerciales: no actualizar sin revisar) | `assets/vendor/tinymce` |
| Remix Icon | 4.2.0 | Apache-2.0 (según el encabezado incluido) | `assets/vendor/remixicon` |
| Boxicons | — | MIT | `assets/vendor/boxicons` |
| simple-datatables | — | LGPL-3.0 (verificar la versión; usar el archivo sin modificar) | `assets/vendor/simple-datatables` |

Varias de estas librerías vienen con la plantilla y **no se usan** en las pantallas del sistema (ApexCharts, Chart.js, ECharts, Quill, TinyMCE, simple-datatables). Quitarlas reduciría el tamaño y la lista de licencias por cumplir.

### Nota sobre SweetAlert2

Desde la versión 11.4.9, SweetAlert2 incluye código que, en sitios con dominio ruso (.ru, .su, .рф) y navegador en ruso, cambia el comportamiento de la página. No afecta a instalaciones en Colombia, pero conviene saberlo.

## Tipografías externas

- **Google Fonts** (Open Sans, Nunito, Poppins), cargadas desde `fonts.googleapis.com`: licencia SIL Open Font License 1.1. El navegador de cada cajero se conecta a los servidores de Google para descargarlas.

## Servicios y software externo

- **SQL Server**: la edición *Express* es gratuita también en producción (máximo 10 GB por base de datos). La edición *Developer* es gratuita solo para desarrollo y pruebas.
