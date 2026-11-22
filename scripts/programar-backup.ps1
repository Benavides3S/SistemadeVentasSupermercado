<#
.SYNOPSIS
    Programa la copia de seguridad diaria en el Programador de tareas de Windows.

.DESCRIPTION
    Crea (o reemplaza) una tarea que ejecuta scripts\backup.ps1 todos los días a la hora indicada,
    con tu usuario de Windows. Si a esa hora el computador estaba apagado, la copia se hace
    apenas se encienda y inicies sesión.
    No necesita permisos de administrador.

    Ejemplos, desde la carpeta raíz del repositorio:
        powershell -ExecutionPolicy Bypass -File scripts\programar-backup.ps1 -Hora 21:30
        powershell -ExecutionPolicy Bypass -File scripts\programar-backup.ps1 -Docker -CopiaExtra E:\CopiasSupermercado
        # Quitar la tarea
        powershell -ExecutionPolicy Bypass -File scripts\programar-backup.ps1 -Quitar
#>
param(
    # Hora diaria (formato 24 horas). Conviene después del cierre del negocio.
    [string]$Hora = '21:00',
    [switch]$Docker,
    # Se pasan tal cual a backup.ps1 si se indican.
    [string]$Servidor,
    [string]$Base,
    [string]$CopiaExtra,
    [string]$NombreTarea = 'SistemaVentas - Copia de seguridad',
    [switch]$Quitar
)
$ErrorActionPreference = 'Stop'

if ($Quitar) {
    Unregister-ScheduledTask -TaskName $NombreTarea -Confirm:$false
    Write-Host "Tarea '$NombreTarea' eliminada."
    exit 0
}

$horaInicio = [datetime]::ParseExact($Hora, 'H:mm', $null)
$script = Join-Path $PSScriptRoot 'backup.ps1'

$argumentos = "-NoProfile -ExecutionPolicy Bypass -WindowStyle Hidden -File `"$script`""
if ($Docker) { $argumentos += ' -Docker' }
if ($Servidor) { $argumentos += " -Servidor `"$Servidor`"" }
if ($Base) { $argumentos += " -Base `"$Base`"" }
if ($CopiaExtra) {
    # [IO.Path] no exige que la unidad exista (la USB puede estar desconectada ahora).
    if (-not [IO.Path]::IsPathRooted($CopiaExtra)) { $CopiaExtra = Join-Path (Get-Location).ProviderPath $CopiaExtra }
    $CopiaExtra = [IO.Path]::GetFullPath($CopiaExtra)
    $argumentos += " -CopiaExtra `"$CopiaExtra`""
}

$accion = New-ScheduledTaskAction -Execute 'powershell.exe' -Argument $argumentos -WorkingDirectory (Split-Path -Parent $PSScriptRoot)
$disparador = New-ScheduledTaskTrigger -Daily -At $horaInicio
# StartWhenAvailable: si a la hora programada el computador estaba apagado, se hace al encenderlo.
$opciones = New-ScheduledTaskSettingsSet -StartWhenAvailable -AllowStartIfOnBatteries -DontStopIfGoingOnBatteries -ExecutionTimeLimit (New-TimeSpan -Hours 2)
# Con tu usuario y solo con la sesión iniciada: LocalDB y Docker Desktop funcionan por usuario.
$usuario = New-ScheduledTaskPrincipal -UserId "$env:USERDOMAIN\$env:USERNAME" -LogonType Interactive -RunLevel Limited

Register-ScheduledTask -TaskName $NombreTarea -Action $accion -Trigger $disparador -Settings $opciones -Principal $usuario `
    -Description 'Copia de seguridad diaria del Sistema de Ventas (scripts\backup.ps1). Registro en backups\copias.log.' -Force | Out-Null

Write-Host "Tarea '$NombreTarea' programada todos los días a las $($horaInicio.ToString('HH:mm'))."
Write-Host "Para probarla ahora: Start-ScheduledTask -TaskName '$NombreTarea'   (resultado en backups\copias.log)"
