<#
.SYNOPSIS
    Copia de seguridad completa de la base de datos, verificada, con nombre por fecha y borrado
    de copias viejas.

.DESCRIPTION
    Crea <Carpeta>\<Base>_aaaammdd_hhmmss.bak, comprueba que el archivo se puede leer (RESTORE
    VERIFYONLY) y después borra las copias automáticas con más de -DiasRetencion días (siempre
    deja al menos -MinimoCopias). Todo queda anotado en <Carpeta>\copias.log.
    Si algo falla termina con código 1 (el Programador de tareas lo muestra como error).

    Ejemplos, desde la carpeta raíz del repositorio:
        # SQL Server instalado en Windows (LocalDB por defecto)
        powershell -ExecutionPolicy Bypass -File scripts\backup.ps1
        # Instalación con Docker
        powershell -ExecutionPolicy Bypass -File scripts\backup.ps1 -Docker
        # Además, copiar a una memoria USB o a una carpeta sincronizada con la nube
        powershell -ExecutionPolicy Bypass -File scripts\backup.ps1 -CopiaExtra E:\CopiasSupermercado

    Guía completa: scripts/LEEME.md
#>
param(
    # Usar la base del contenedor de Docker en lugar de un SQL Server instalado en Windows.
    [switch]$Docker,
    # Solo sin -Docker. Ejemplos: '(localdb)\MSSQLLocalDB', '.\SQLEXPRESS'.
    [string]$Servidor = '(localdb)\MSSQLLocalDB',
    # Por defecto: SistemaVentasSuperMercado (con -Docker, el DB_NAME de .env).
    [string]$Base,
    # Por defecto: la carpeta backups\ del repositorio (está en .gitignore).
    [string]$Carpeta,
    # Carpeta adicional donde dejar otra copia (USB, disco externo, carpeta de OneDrive...).
    [string]$CopiaExtra,
    [int]$DiasRetencion = 30,
    [int]$MinimoCopias = 7
)
. (Join-Path $PSScriptRoot 'copias-comun.ps1')

if (-not $Carpeta) { $Carpeta = $script:CarpetaPorDefecto }
$Carpeta = Get-RutaCompleta $Carpeta
if ($CopiaExtra) { $CopiaExtra = Get-RutaCompleta $CopiaExtra }

$codigoSalida = 0
$archivo = $null
$verificada = $false
Push-Location $script:Raiz
try {
    if ($DiasRetencion -lt 1 -or $MinimoCopias -lt 1) { throw 'DiasRetencion y MinimoCopias deben ser al menos 1.' }
    if (-not $Base) { $Base = Get-NombreBasePorDefecto -Docker:$Docker }
    Assert-NombreBase $Base
    New-Item -ItemType Directory -Force $Carpeta | Out-Null

    $archivo = Join-Path $Carpeta ("${Base}_" + (Get-Date -Format 'yyyyMMdd_HHmmss') + '.bak')
    $origen = if ($Docker) { 'Docker' } else { $Servidor }
    Write-Registro $Carpeta "Iniciando copia de '$Base' ($origen)..."

    if ($Docker) { New-CopiaDocker $Base $archivo } else { New-CopiaWindows $Servidor $Base $archivo }

    $verificada = $true
    $megas = [math]::Round((Get-Item $archivo).Length / 1MB, 1)
    Write-Registro $Carpeta "OK: $archivo ($megas MB, verificada)"
    Remove-CopiasViejas $Carpeta $Base $DiasRetencion $MinimoCopias $Carpeta

    if ($CopiaExtra) {
        try {
            New-Item -ItemType Directory -Force $CopiaExtra | Out-Null
            Copy-Item $archivo -Destination $CopiaExtra
        } catch {
            throw "La copia principal quedó bien, pero no se pudo dejar la copia extra en $CopiaExtra (¿la USB o el disco está conectado?): $($_.Exception.Message)"
        }
        Write-Registro $Carpeta "OK: copia extra en $CopiaExtra"
        Remove-CopiasViejas $CopiaExtra $Base $DiasRetencion $MinimoCopias $Carpeta
    }
} catch {
    Write-Registro $Carpeta "ERROR: $($_.Exception.Message)"
    $codigoSalida = 1
    # Una copia a medias o que no pasó la verificación no sirve: se borra para que nunca
    # cuente como una de las copias buenas.
    if ($archivo -and -not $verificada -and (Test-Path $archivo)) { Remove-Item $archivo -ErrorAction SilentlyContinue }
} finally {
    Pop-Location
}
exit $codigoSalida
