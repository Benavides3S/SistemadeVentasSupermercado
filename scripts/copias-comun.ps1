# Funciones compartidas por backup.ps1 y restaurar.ps1. No se ejecuta sola.
# Dos formas de trabajar:
#   - Windows: SQL Server instalado en el computador (LocalDB o Express). Se conecta con la
#     cuenta de Windows (sin contraseña escrita en ningún lado) usando System.Data.SqlClient,
#     que ya viene con Windows PowerShell: no hace falta instalar sqlcmd.
#   - Docker: ejecuta sqlcmd DENTRO del contenedor "db". La contraseña de sa nunca sale del
#     contenedor (sqlcmd la toma de la variable SQLCMDPASSWORD).

$ErrorActionPreference = 'Stop'
$script:Raiz = Split-Path -Parent $PSScriptRoot
$script:CarpetaPorDefecto = Join-Path $script:Raiz 'backups'
$script:CarpetaContenedor = '/var/opt/mssql/backups'
# -I: QUOTED_IDENTIFIER ON. -f 65001: archivos en UTF-8. -b: detenerse ante el primer error.
$script:SqlcmdDocker = 'SQLCMDPASSWORD=$MSSQL_SA_PASSWORD /opt/mssql-tools18/bin/sqlcmd -S localhost -U sa -C -b -I -f 65001'

function Assert-NombreBase([string]$nombre) {
    if ($nombre -notmatch '^[A-Za-z0-9_]+$') {
        throw "El nombre de la base solo puede tener letras sin tilde, números y guion bajo: '$nombre'"
    }
}

# Convierte una ruta (relativa a la carpeta actual o absoluta) en ruta completa, exista o no.
function Get-RutaCompleta([string]$ruta) {
    # [IO.Path] no exige que la unidad exista (por ejemplo, una USB desconectada).
    if (-not [IO.Path]::IsPathRooted($ruta)) { $ruta = Join-Path (Get-Location).ProviderPath $ruta }
    return [IO.Path]::GetFullPath($ruta)
}

function Get-NombreBasePorDefecto([switch]$Docker) {
    if (-not $Docker) { return 'SistemaVentasSuperMercado' }
    $archivoEnv = Join-Path $script:Raiz '.env'
    if (-not (Test-Path $archivoEnv)) { throw "No existe el archivo .env. Copia .env.example como .env y llena los valores." }
    $nombre = 'SistemaVentasSuperMercado'
    $linea = Get-Content $archivoEnv | Where-Object { $_ -match '^\s*DB_NAME\s*=' } | Select-Object -First 1
    if ($linea) { $nombre = ($linea -split '=', 2)[1].Trim() }
    return $nombre
}

# Deja constancia en <carpeta>\copias.log y en pantalla. Cuando la copia la lanza el
# Programador de tareas nadie ve la pantalla: el registro es la única forma de saber qué pasó.
function Write-Registro([string]$carpeta, [string]$mensaje) {
    $linea = (Get-Date -Format 'yyyy-MM-dd HH:mm:ss') + '  ' + $mensaje
    Write-Host $linea
    try {
        New-Item -ItemType Directory -Force $carpeta | Out-Null
        Add-Content -Path (Join-Path $carpeta 'copias.log') -Value $linea -Encoding UTF8
    } catch {
        Write-Warning "No se pudo escribir el registro en $carpeta"
    }
}

# ---------- Docker ----------

function Invoke-Docker {
    & docker @args
    if ($LASTEXITCODE -ne 0) { throw "Falló: docker $($args -join ' ')" }
}

# Ejecuta un texto SQL dentro del contenedor (se envía como archivo: pasar comillas por la
# línea de comandos de PowerShell 5.1 las daña). Devuelve lo que imprime sqlcmd.
function Invoke-SqlDocker([string]$sql, [string]$opciones = '') {
    $tmp = Join-Path $env:TEMP ('sistemaventas-' + [guid]::NewGuid().ToString('N') + '.sql')
    [IO.File]::WriteAllText($tmp, $sql, (New-Object Text.UTF8Encoding $false))
    try {
        Invoke-Docker compose cp $tmp db:/tmp/copias.sql | Out-Null
        Invoke-Docker compose exec -T db sh -c "$script:SqlcmdDocker $opciones -i /tmp/copias.sql"
    } finally {
        Remove-Item $tmp -ErrorAction SilentlyContinue
    }
}

function Start-BaseDocker {
    Invoke-Docker compose up -d --wait db | Out-Host
    Invoke-Docker compose exec -T db mkdir -p $script:CarpetaContenedor | Out-Host
}

# Crea la copia dentro del contenedor, la verifica y la trae al computador.
function New-CopiaDocker([string]$base, [string]$archivo) {
    Assert-NombreBase $base
    $nombre = Split-Path $archivo -Leaf
    $enContenedor = "$script:CarpetaContenedor/$nombre"
    Start-BaseDocker
    try {
        Invoke-SqlDocker ("BACKUP DATABASE [$base] TO DISK = N'$enContenedor' WITH INIT, CHECKSUM;`n" +
                          "RESTORE VERIFYONLY FROM DISK = N'$enContenedor' WITH CHECKSUM;") | Out-Host
        Invoke-Docker compose cp "db:$enContenedor" $archivo | Out-Host
    } finally {
        # La copia no se queda dentro del contenedor: ocuparía espacio en el volumen de la base.
        & docker compose exec -T db rm -f $enContenedor
    }
    if (-not (Test-Path $archivo) -or (Get-Item $archivo).Length -eq 0) {
        throw "La copia no llegó a $archivo"
    }
}

# ---------- Windows (LocalDB o SQL Server Express instalado) ----------

function New-ConexionSql([string]$servidor) {
    $conexion = New-Object System.Data.SqlClient.SqlConnection "Server=$servidor;Database=master;Integrated Security=True;Connect Timeout=60;Application Name=SistemaVentas-Copias"
    $conexion.Open()
    return $conexion
}

function New-ComandoSql($conexion, [string]$sql, [hashtable]$parametros) {
    $comando = $conexion.CreateCommand()
    $comando.CommandText = $sql
    $comando.CommandTimeout = 0   # una copia grande puede tardar varios minutos
    if ($parametros) {
        foreach ($clave in $parametros.Keys) { [void]$comando.Parameters.AddWithValue($clave, $parametros[$clave]) }
    }
    return $comando
}

function Invoke-SqlWindows($conexion, [string]$sql, [hashtable]$parametros) {
    $comando = New-ComandoSql $conexion $sql $parametros
    try { [void]$comando.ExecuteNonQuery() } finally { $comando.Dispose() }
}

function Invoke-ConsultaWindows($conexion, [string]$sql, [hashtable]$parametros) {
    $comando = New-ComandoSql $conexion $sql $parametros
    $tabla = New-Object System.Data.DataTable
    try {
        $lector = $comando.ExecuteReader()
        try { $tabla.Load($lector) } finally { $lector.Dispose() }
    } finally {
        $comando.Dispose()
    }
    return , $tabla
}

function Test-BaseExiste($conexion, [string]$base) {
    $tabla = Invoke-ConsultaWindows $conexion 'SELECT DB_ID(@base) AS Id' @{ '@base' = $base }
    return -not ($tabla.Rows[0].Id -is [DBNull])
}

# Ojo: el archivo lo escribe SQL Server, no este script. Con LocalDB es tu mismo usuario de
# Windows; con SQL Server Express es la cuenta del servicio, que necesita permiso en la carpeta.
function New-CopiaWindows([string]$servidor, [string]$base, [string]$archivo) {
    Assert-NombreBase $base
    $conexion = New-ConexionSql $servidor
    try {
        if (-not (Test-BaseExiste $conexion $base)) { throw "No existe la base '$base' en $servidor" }
        Invoke-SqlWindows $conexion 'BACKUP DATABASE @base TO DISK = @archivo WITH INIT, CHECKSUM, NAME = @nombre;' @{
            '@base' = $base; '@archivo' = $archivo; '@nombre' = "$base - copia completa" }
        Invoke-SqlWindows $conexion 'RESTORE VERIFYONLY FROM DISK = @archivo WITH CHECKSUM;' @{ '@archivo' = $archivo }
    } finally {
        $conexion.Dispose()
    }
}

# ---------- Retención ----------

# Borra las copias automáticas (Base_aaaammdd_hhmmss.bak) con más de $dias días, pero siempre
# deja al menos las $minimo más recientes. Las copias con otro nombre (por ejemplo las de
# "antes-de-restaurar") nunca se borran solas.
function Remove-CopiasViejas([string]$carpeta, [string]$base, [int]$dias, [int]$minimo, [string]$carpetaRegistro) {
    $patron = '^' + [regex]::Escape($base) + '_(\d{8}_\d{6})\.bak$'
    $copias = foreach ($f in (Get-ChildItem -Path $carpeta -File -Filter "${base}_*.bak")) {
        $m = [regex]::Match($f.Name, $patron)
        if ($m.Success) {
            [pscustomobject]@{ Archivo = $f; Fecha = [datetime]::ParseExact($m.Groups[1].Value, 'yyyyMMdd_HHmmss', $null) }
        }
    }
    $limite = (Get-Date).AddDays(-$dias)
    $viejas = $copias | Sort-Object Fecha -Descending | Select-Object -Skip $minimo | Where-Object { $_.Fecha -lt $limite }
    foreach ($c in $viejas) {
        Remove-Item $c.Archivo.FullName
        Write-Registro $carpetaRegistro "Borrada por antigüedad: $($c.Archivo.FullName)"
    }
}
