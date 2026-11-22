<#
.SYNOPSIS
    Restaura la base de datos desde un archivo .bak. REEMPLAZA todos los datos actuales.

.DESCRIPTION
    Antes de restaurar:
      1. Comprueba que el archivo .bak se puede leer.
      2. Pide escribir el nombre de la base para confirmar.
      3. Si la base ya existe, guarda una copia "antes-de-restaurar" de lo que tiene ahora
         (por si te equivocaste de archivo). Esa copia no se borra sola.
    Con -Docker apaga la aplicación mientras restaura y la vuelve a encender al final.

    Ejemplos, desde la carpeta raíz del repositorio:
        powershell -ExecutionPolicy Bypass -File scripts\restaurar.ps1 -Archivo backups\SistemaVentasSuperMercado_20261121_210000.bak
        powershell -ExecutionPolicy Bypass -File scripts\restaurar.ps1 -Docker -Archivo backups\SistemaVentasSuperMercado_20261121_210000.bak
        # Revisar una copia vieja sin tocar la base real: restaurarla con otro nombre
        powershell -ExecutionPolicy Bypass -File scripts\restaurar.ps1 -Archivo <archivo.bak> -Base CopiaParaRevisar

    Guía completa: scripts/LEEME.md
#>
param(
    [Parameter(Mandatory = $true)]
    [string]$Archivo,
    [switch]$Docker,
    # Solo sin -Docker.
    [string]$Servidor = '(localdb)\MSSQLLocalDB',
    # Base que se va a reemplazar (o crear). Por defecto: SistemaVentasSuperMercado (con -Docker, el DB_NAME de .env).
    [string]$Base,
    # Dónde guardar la copia "antes-de-restaurar". Por defecto: backups\ del repositorio.
    [string]$Carpeta,
    # No pedir confirmación (para pruebas automáticas). Úsalo con cuidado.
    [switch]$SinConfirmar
)
. (Join-Path $PSScriptRoot 'copias-comun.ps1')

$Archivo = Get-RutaCompleta $Archivo
if (-not $Carpeta) { $Carpeta = $script:CarpetaPorDefecto }
$Carpeta = Get-RutaCompleta $Carpeta

function Format-TextoSql([string]$texto) { return $texto.Replace("'", "''") }

# Archivos de destino: si la base ya existe se reutilizan sus archivos actuales; si no, van a
# la carpeta de datos por defecto del servidor con el nombre de la base.
function Get-Destinos($archivosCopia, [string]$base, $actuales, [string]$carpetaDatos, [string]$carpetaLog, [string]$separador) {
    $datosActuales = @($actuales | Where-Object { $_.Tipo -eq 'D' })
    $logActuales = @($actuales | Where-Object { $_.Tipo -eq 'L' })
    $datosCopia = @($archivosCopia | Where-Object { $_.Tipo -eq 'D' })
    $logCopia = @($archivosCopia | Where-Object { $_.Tipo -eq 'L' })
    $reutilizar = $datosActuales.Count -eq $datosCopia.Count -and $logActuales.Count -eq $logCopia.Count

    $destinos = @()
    for ($i = 0; $i -lt $datosCopia.Count; $i++) {
        $ruta = if ($reutilizar) { $datosActuales[$i].Ruta }
                elseif ($i -eq 0) { "$carpetaDatos$separador$base.mdf" }
                else { "$carpetaDatos$separador${base}_$i.ndf" }
        $destinos += [pscustomobject]@{ Logico = $datosCopia[$i].Logico; Ruta = $ruta }
    }
    for ($i = 0; $i -lt $logCopia.Count; $i++) {
        $ruta = if ($reutilizar) { $logActuales[$i].Ruta }
                elseif ($i -eq 0) { "$carpetaLog$separador${base}_log.ldf" }
                else { "$carpetaLog$separador${base}_log_$i.ldf" }
        $destinos += [pscustomobject]@{ Logico = $logCopia[$i].Logico; Ruta = $ruta }
    }
    return , $destinos
}

function Confirm-Restauracion([string]$base, [string]$destino) {
    Write-Host ""
    Write-Host "Vas a REEMPLAZAR la base '$base' ($destino) con:" -ForegroundColor Yellow
    Write-Host "  $Archivo" -ForegroundColor Yellow
    Write-Host "Todo lo que se registró después de esa copia se perderá (queda guardado en la copia 'antes-de-restaurar')." -ForegroundColor Yellow
    $respuesta = Read-Host "Para continuar escribe el nombre de la base ($base)"
    if ($respuesta -cne $base) { throw 'Restauración cancelada: el nombre no coincide.' }
}

function Restore-Windows {
    $conexion = New-ConexionSql $Servidor
    try {
        Write-Host "1/4 Verificando el archivo..."
        Invoke-SqlWindows $conexion 'RESTORE VERIFYONLY FROM DISK = @archivo;' @{ '@archivo' = $Archivo }
        $lista = Invoke-ConsultaWindows $conexion 'RESTORE FILELISTONLY FROM DISK = @archivo;' @{ '@archivo' = $Archivo }
        $archivosCopia = foreach ($fila in $lista.Rows) { [pscustomobject]@{ Logico = [string]$fila.LogicalName; Tipo = [string]$fila.Type } }

        $existe = Test-BaseExiste $conexion $Base
        if (-not $SinConfirmar) { Confirm-Restauracion $Base $Servidor }

        if ($existe) {
            $copiaPrevia = Join-Path $Carpeta ("${Base}_antes-de-restaurar_" + (Get-Date -Format 'yyyyMMdd_HHmmss') + '.bak')
            Write-Host "2/4 Guardando lo que tiene la base ahora en $copiaPrevia ..."
            New-Item -ItemType Directory -Force $Carpeta | Out-Null
            New-CopiaWindows $Servidor $Base $copiaPrevia
            Write-Registro $Carpeta "Copia antes de restaurar: $copiaPrevia"
        } else {
            Write-Host "2/4 La base '$Base' no existe: se creará."
        }

        $actuales = Invoke-ConsultaWindows $conexion "SELECT CASE type WHEN 0 THEN 'D' ELSE 'L' END AS Tipo, physical_name AS Ruta FROM sys.master_files WHERE database_id = DB_ID(@base) AND type IN (0, 1) ORDER BY type, file_id;" @{ '@base' = $Base }
        $rutasActuales = foreach ($fila in $actuales.Rows) { [pscustomobject]@{ Tipo = [string]$fila.Tipo; Ruta = [string]$fila.Ruta } }
        $carpetas = Invoke-ConsultaWindows $conexion "SELECT CAST(SERVERPROPERTY('InstanceDefaultDataPath') AS nvarchar(400)) AS Datos, CAST(SERVERPROPERTY('InstanceDefaultLogPath') AS nvarchar(400)) AS Log;"
        $carpetaDatos = ([string]$carpetas.Rows[0].Datos).TrimEnd('\')
        $carpetaLog = ([string]$carpetas.Rows[0].Log).TrimEnd('\')
        $destinos = Get-Destinos $archivosCopia $Base $rutasActuales $carpetaDatos $carpetaLog '\'

        Write-Host "3/4 Restaurando..."
        $parametros = @{ '@archivo' = $Archivo }
        $mover = @()
        for ($i = 0; $i -lt $destinos.Count; $i++) {
            $mover += "MOVE @l$i TO @r$i"
            $parametros["@l$i"] = $destinos[$i].Logico
            $parametros["@r$i"] = $destinos[$i].Ruta
        }
        # Un solo lote: se sacan las conexiones abiertas y se restaura enseguida.
        $sql = ''
        if ($existe) { $sql += "ALTER DATABASE [$Base] SET SINGLE_USER WITH ROLLBACK IMMEDIATE;`n" }
        $sql += "RESTORE DATABASE [$Base] FROM DISK = @archivo WITH REPLACE, " + ($mover -join ', ') + ";`n"
        $sql += "ALTER DATABASE [$Base] SET MULTI_USER;"
        try {
            Invoke-SqlWindows $conexion $sql $parametros
        } catch {
            try { Invoke-SqlWindows $conexion "IF DB_ID(N'$Base') IS NOT NULL ALTER DATABASE [$Base] SET MULTI_USER;" } catch { }
            throw
        }
        Write-Host "4/4 Listo."
    } finally {
        $conexion.Dispose()
    }
}

function Restore-Docker {
    $webEncendida = @(& docker compose ps --status running --services) -contains 'web'
    $enContenedor = "$script:CarpetaContenedor/restaurar.bak"
    Start-BaseDocker
    try {
        Write-Host "1/5 Copiando el archivo al contenedor y verificándolo..."
        Invoke-Docker compose cp $Archivo "db:$enContenedor" | Out-Host
        Invoke-SqlDocker "RESTORE VERIFYONLY FROM DISK = N'$enContenedor';" | Out-Host
        # Lista de archivos de la copia, separada por | (los nombres de archivo pueden tener espacios).
        $salida = Invoke-SqlDocker "SET NOCOUNT ON; RESTORE FILELISTONLY FROM DISK = N'$enContenedor';" "-h -1 -W -s '|'"
        $archivosCopia = foreach ($linea in $salida) {
            $partes = ([string]$linea).Split('|')
            if ($partes.Count -ge 3 -and ($partes[2] -eq 'D' -or $partes[2] -eq 'L')) {
                [pscustomobject]@{ Logico = $partes[0]; Tipo = $partes[2] }
            }
        }
        if (-not $archivosCopia) { throw "No se pudo leer la lista de archivos de la copia." }

        $existe = ((Invoke-SqlDocker "SET NOCOUNT ON; SELECT CASE WHEN DB_ID(N'$Base') IS NULL THEN 'NO' ELSE 'SI' END;" '-h -1 -W') -join '').Trim() -eq 'SI'
        if (-not $SinConfirmar) { Confirm-Restauracion $Base 'Docker' }

        if ($existe) {
            $copiaPrevia = Join-Path $Carpeta ("${Base}_antes-de-restaurar_" + (Get-Date -Format 'yyyyMMdd_HHmmss') + '.bak')
            Write-Host "2/5 Guardando lo que tiene la base ahora en $copiaPrevia ..."
            New-Item -ItemType Directory -Force $Carpeta | Out-Null
            New-CopiaDocker $Base $copiaPrevia
            Write-Registro $Carpeta "Copia antes de restaurar: $copiaPrevia"
        } else {
            Write-Host "2/5 La base '$Base' no existe: se creará."
        }

        Write-Host "3/5 Apagando la aplicación mientras se restaura..."
        Invoke-Docker compose stop web | Out-Host

        Write-Host "4/5 Restaurando..."
        # Mismos nombres que usa CREATE DATABASE en el contenedor (/var/opt/mssql/data/<Base>.mdf).
        $destinos = Get-Destinos $archivosCopia $Base @() '/var/opt/mssql/data' '/var/opt/mssql/data' '/'
        $mover = foreach ($d in $destinos) { "MOVE N'$(Format-TextoSql $d.Logico)' TO N'$(Format-TextoSql $d.Ruta)'" }
        $sql = "IF DB_ID(N'$Base') IS NOT NULL ALTER DATABASE [$Base] SET SINGLE_USER WITH ROLLBACK IMMEDIATE;`n" +
               "RESTORE DATABASE [$Base] FROM DISK = N'$enContenedor' WITH REPLACE, " + ($mover -join ', ') + ";`n" +
               "ALTER DATABASE [$Base] SET MULTI_USER;"
        try {
            Invoke-SqlDocker $sql | Out-Host
        } catch {
            try { Invoke-SqlDocker "IF DB_ID(N'$Base') IS NOT NULL ALTER DATABASE [$Base] SET MULTI_USER;" | Out-Host } catch { }
            throw
        }
    } finally {
        & docker compose exec -T db rm -f $enContenedor
    }
    if ($webEncendida) {
        Write-Host "5/5 Encendiendo la aplicación otra vez..."
        Invoke-Docker compose start web | Out-Host
    } else {
        Write-Host "5/5 Listo. La aplicación estaba apagada; enciéndela con: docker compose up -d"
    }
}

$codigoSalida = 0
Push-Location $script:Raiz
try {
    if (-not (Test-Path $Archivo -PathType Leaf)) { throw "No existe el archivo $Archivo" }
    if (-not $Base) { $Base = Get-NombreBasePorDefecto -Docker:$Docker }
    Assert-NombreBase $Base
    if ($Docker) { Restore-Docker } else { Restore-Windows }
    Write-Registro $Carpeta "Restaurada la base '$Base' desde $Archivo"
    Write-Host ""
    Write-Host "Si la copia es de una versión anterior del programa, la aplicación avisará que faltan migraciones: aplícalas como dice la guía."
} catch {
    Write-Host "ERROR: $($_.Exception.Message)" -ForegroundColor Red
    $codigoSalida = 1
} finally {
    Pop-Location
}
exit $codigoSalida
