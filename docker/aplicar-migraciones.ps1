<#
.SYNOPSIS
    Crea la base de datos (si no existe) y le aplica docker/migraciones.sql dentro del contenedor de SQL Server.

.DESCRIPTION
    migraciones.sql es un script "idempotente": revisa qué migraciones ya están aplicadas y solo
    ejecuta las que faltan, así que se puede correr varias veces sin dañar nada.
    Se genera en Visual Studio (Consola del Administrador de paquetes), ver docker/LEEME.md.

    Uso, desde la carpeta raíz del repositorio:
        powershell -ExecutionPolicy Bypass -File docker\aplicar-migraciones.ps1
#>
$ErrorActionPreference = 'Stop'
$raiz = Split-Path -Parent $PSScriptRoot
Set-Location $raiz

$script = Join-Path $PSScriptRoot 'migraciones.sql'
if (-not (Test-Path $script)) {
    throw "No existe docker\migraciones.sql. Genéralo con Script-Migration -Idempotent (ver docker\LEEME.md)."
}
if (-not (Test-Path (Join-Path $raiz '.env'))) {
    throw "No existe el archivo .env. Copia .env.example como .env y llena los valores."
}

# Nombre de la base: el de .env o el valor por defecto.
$nombreBase = 'SistemaVentasSuperMercado'
$linea = Get-Content (Join-Path $raiz '.env') | Where-Object { $_ -match '^\s*DB_NAME\s*=' } | Select-Object -First 1
if ($linea) { $nombreBase = ($linea -split '=', 2)[1].Trim() }
if ($nombreBase -notmatch '^[A-Za-z0-9_]+$') {
    throw "DB_NAME solo puede tener letras sin tilde, números y guion bajo: '$nombreBase'"
}

function Invoke-Docker {
    & docker @args
    if ($LASTEXITCODE -ne 0) { throw "Falló: docker $($args -join ' ')" }
}

Write-Host "1/4 Encendiendo SQL Server y esperando a que esté listo..."
Invoke-Docker compose up -d --wait db

Write-Host "2/4 Creando la base '$nombreBase' si no existe..."
$crear = Join-Path $env:TEMP 'sistemaventas-crear-base.sql'
[IO.File]::WriteAllText($crear, "IF DB_ID(N'$nombreBase') IS NULL CREATE DATABASE [$nombreBase];")
Invoke-Docker compose cp $crear db:/tmp/crear-base.sql
Remove-Item $crear

# La contraseña no sale del contenedor: sqlcmd la toma de la variable SQLCMDPASSWORD.
# -I: QUOTED_IDENTIFIER ON (lo exigen los índices filtrados). -f 65001: el archivo está en UTF-8 (tildes).
# -b: detenerse ante el primer error.
$sqlcmd = 'SQLCMDPASSWORD=$MSSQL_SA_PASSWORD /opt/mssql-tools18/bin/sqlcmd -S localhost -U sa -C -b -I -f 65001'
Invoke-Docker compose exec -T db sh -c "$sqlcmd -i /tmp/crear-base.sql"

Write-Host "3/4 Aplicando las migraciones que falten..."
Invoke-Docker compose cp $script db:/tmp/migraciones.sql
Invoke-Docker compose exec -T db sh -c "$sqlcmd -d $nombreBase -i /tmp/migraciones.sql"

Write-Host "4/4 Migraciones aplicadas en la base de datos:"
[IO.File]::WriteAllText((Join-Path $env:TEMP 'sistemaventas-lista.sql'), "SET NOCOUNT ON; SELECT MigrationId FROM __EFMigrationsHistory ORDER BY MigrationId;")
Invoke-Docker compose cp (Join-Path $env:TEMP 'sistemaventas-lista.sql') db:/tmp/lista.sql
Remove-Item (Join-Path $env:TEMP 'sistemaventas-lista.sql')
Invoke-Docker compose exec -T db sh -c "$sqlcmd -d $nombreBase -h -1 -W -i /tmp/lista.sql"

Write-Host ""
Write-Host "Listo. Ahora enciende (o reinicia) la aplicación con: docker compose up -d"
