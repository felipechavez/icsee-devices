<#
.SYNOPSIS
    Respalda lo que git no guarda antes de formatear este equipo.

.DESCRIPTION
    Copia a una carpeta nueva dentro de -Destino:
      repo\    archivos ignorados, sin versionar o modificados (.env, poc\...), sin lo regenerable
      claude\  CLAUDE.md, settings.json, skills y memorias de este proyecto (sin credenciales)
      docker\  volcados de CamGuard y Keycloak y el volumen apidata (llaves y clips en cola)
    Verifica cada copia por hash y nunca borra nada. Sale con 1 si algo no quedo respaldado.
    El respaldo lleva secretos en claro: guardalo en un lugar privado.

.PARAMETER Destino
    Carpeta donde crear el respaldo. Debe estar fuera del disco que se va a formatear.

.PARAMETER PermitirDiscoDelSistema
    Acepta un destino en el disco del sistema (p. ej. una carpeta que se sincroniza con la nube).

.EXAMPLE
    .\scripts\backup-local.ps1 -Destino E:\Respaldos
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory)][string]$Destino,
    [switch]$PermitirDiscoDelSistema
)

$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
Set-Location $root

# Mismo override que scripts/up.ps1: en Windows compose siempre lo necesita.
$env:COMPOSE_FILE = 'docker-compose.yml;docker-compose.windows.yml'
try { [Console]::OutputEncoding = [Text.Encoding]::UTF8 } catch { }

# Lo que se regenera con npm install o un build no se respalda.
$regenerable = '(^|/)(bin|obj|node_modules|dist|TestResults|\.vs)/'

$failures = New-Object System.Collections.ArrayList
$warnings = New-Object System.Collections.ArrayList

function Add-Failure([string]$message) {
    [void]$failures.Add($message)
    Write-Host "FALLO: $message" -ForegroundColor Red
}

# En PowerShell 5.1 el stderr de un ejecutable es un error terminante con 'Stop'.
function Invoke-Native([string]$exe, [string[]]$arguments) {
    $ErrorActionPreference = 'Continue'
    $output = & $exe @arguments 2>&1 | ForEach-Object { "$_" }
    [pscustomobject]@{ Ok = ($LASTEXITCODE -eq 0); Output = (@($output) -join "`n") }
}

function Get-GitLines {
    $ErrorActionPreference = 'Continue'
    & git -c core.quotepath=off @args 2>$null | Where-Object { $_ }
}

function Copy-Verified([string]$source, [string]$target) {
    try {
        [void][IO.Directory]::CreateDirectory((Split-Path -Parent $target))
        Copy-Item -LiteralPath $source -Destination $target -Force
        if ((Get-FileHash -LiteralPath $source).Hash -ne (Get-FileHash -LiteralPath $target).Hash) {
            throw 'la copia no coincide con el original'
        }
    } catch {
        Add-Failure "$source -> $_"
    }
}

function Copy-Folder([string]$source, [string]$target) {
    if (-not (Test-Path -LiteralPath $source)) { return }
    $prefix = (Get-Item -LiteralPath $source -Force).FullName.Length + 1
    foreach ($file in Get-ChildItem -LiteralPath $source -Recurse -File -Force) {
        Copy-Verified $file.FullName (Join-Path $target $file.FullName.Substring($prefix))
    }
}

# pg_dump escribe dentro del contenedor: un pipe de PowerShell 5.1 corromperia el gzip.
function Backup-Database([string]$service, [string]$user, [string]$database, [string]$target) {
    $tmp = '/tmp/camguard-respaldo.sql.gz'
    $dump = Invoke-Native docker @('compose', 'exec', '-T', $service, 'pg_dump', '-U', $user, '-d', $database, '--clean', '--if-exists', '-Z', '6', '-f', $tmp)
    if (-not $dump.Ok) { Add-Failure "Volcado de ${database}: $($dump.Output)"; return }

    $check = Invoke-Native docker @('compose', 'exec', '-T', $service, 'sh', '-c', "gzip -t $tmp && sha256sum $tmp")
    $match = [regex]::Match($check.Output, '\b[0-9a-f]{64}\b')
    if (-not $check.Ok -or -not $match.Success) { Add-Failure "Volcado de ${database} invalido: $($check.Output)"; return }

    $copy = Invoke-Native docker @('compose', 'cp', "${service}:$tmp", $target)
    [void](Invoke-Native docker @('compose', 'exec', '-T', $service, 'rm', '-f', $tmp))
    if (-not $copy.Ok) { Add-Failure "Copia del volcado de ${database}: $($copy.Output)"; return }
    if ((Get-FileHash -LiteralPath $target -Algorithm SHA256).Hash -ine $match.Value) {
        Add-Failure "Volcado de ${database}: la copia no coincide con el contenedor"
        return
    }
    Write-Host "  $database -> $target"
}

# 1. Destino: fuera del repo y, salvo que se pida, fuera del disco que se va a formatear.
$destFull = $ExecutionContext.SessionState.Path.GetUnresolvedProviderPathFromPSPath($Destino).TrimEnd('\')
if ($destFull -ieq $root -or $destFull.StartsWith("$root\", [StringComparison]::OrdinalIgnoreCase)) {
    Write-Host 'ERROR: el destino no puede estar dentro del repositorio.' -ForegroundColor Red
    exit 1
}
$destRoot = [IO.Path]::GetPathRoot($destFull)
if (-not $PermitirDiscoDelSistema -and $destRoot -in @("$env:SystemDrive\", [IO.Path]::GetPathRoot($root))) {
    Write-Host "ERROR: $destRoot es el disco del sistema o del repositorio y se perderia al formatear. Usa un disco externo, o -PermitirDiscoDelSistema si la carpeta se sincroniza con la nube." -ForegroundColor Red
    exit 1
}
if (-not (Test-Path -LiteralPath $destRoot)) {
    Write-Host "ERROR: no existe la unidad $destRoot." -ForegroundColor Red
    exit 1
}

$run = Join-Path $destFull ('camguard-respaldo-' + (Get-Date).ToString('yyyyMMdd-HHmmss', [Globalization.CultureInfo]::InvariantCulture))
[void][IO.Directory]::CreateDirectory($run)
Write-Host "Respaldando en $run" -ForegroundColor Cyan

# 2. Archivos del repo que no estan en GitHub.
Write-Host 'Archivos del repositorio...' -ForegroundColor Cyan
$ignored = @(Get-GitLines ls-files --others --ignored --exclude-standard) | Where-Object { $_ -notmatch $regenerable }
$untracked = @(Get-GitLines ls-files --others --exclude-standard)
$modified = @(Get-GitLines diff HEAD --name-only --diff-filter=d)
$repoFiles = @($ignored) + $untracked + $modified | Sort-Object -Unique
foreach ($file in $repoFiles) {
    Copy-Verified (Join-Path $root $file) (Join-Path (Join-Path $run 'repo') $file)
}
Write-Host "  $($repoFiles.Count) archivos"

# 3. Configuracion de Claude. Las credenciales no se copian: se inicia sesion de nuevo.
Write-Host 'Configuracion de Claude...' -ForegroundColor Cyan
$claudeHome = Join-Path $env:USERPROFILE '.claude'
$claudeTarget = Join-Path $run 'claude'
foreach ($name in 'CLAUDE.md', 'settings.json') {
    $file = Join-Path $claudeHome $name
    if (Test-Path -LiteralPath $file) { Copy-Verified $file (Join-Path $claudeTarget $name) }
}
# skills\synced lo vuelve a descargar Claude Code desde la cuenta.
$skillsHome = Join-Path $claudeHome 'skills'
foreach ($skill in Get-ChildItem -LiteralPath $skillsHome -Directory -Force -ErrorAction SilentlyContinue | Where-Object { $_.Name -ne 'synced' }) {
    Copy-Folder $skill.FullName (Join-Path $claudeTarget "skills\$($skill.Name)")
}
$projectSlug = $root -replace '[:\\/]', '-'
Copy-Folder (Join-Path $claudeHome "projects\$projectSlug\memory") (Join-Path $claudeTarget "projects\$projectSlug\memory")

# 4. Datos en volumenes de Docker.
Write-Host 'Datos de Docker...' -ForegroundColor Cyan
$dockerTarget = Join-Path $run 'docker'
[void][IO.Directory]::CreateDirectory($dockerTarget)
if (-not (Invoke-Native docker @('info', '--format', '{{.ServerVersion}}')).Ok) {
    Add-Failure 'Docker no esta corriendo: faltan las bases de datos y el volumen apidata. Abre Docker Desktop y vuelve a ejecutar.'
} else {
    $up = Invoke-Native docker @('compose', 'up', '-d', '--wait', 'db', 'keycloak-db')
    if (-not $up.Ok) {
        Add-Failure "No se pudieron levantar las bases de datos: $($up.Output)"
    } else {
        Backup-Database 'db' 'camguard' 'camguard' (Join-Path $dockerTarget 'camguard.sql.gz')
        Backup-Database 'keycloak-db' 'keycloak' 'keycloak' (Join-Path $dockerTarget 'keycloak.sql.gz')
    }

    $apidata = Join-Path $dockerTarget 'apidata'
    $copy = Invoke-Native docker @('compose', 'cp', 'api:/data', $apidata)
    if (-not $copy.Ok) {
        Add-Failure "Volumen apidata: $($copy.Output)"
    } elseif (-not (Get-ChildItem -LiteralPath (Join-Path $apidata 'keys') -File -ErrorAction SilentlyContinue)) {
        [void]$warnings.Add('apidata no tiene llaves en keys\: sin ellas no se descifran las contrasenas DVRIP guardadas.')
    } else {
        Write-Host "  apidata -> $apidata"
    }
}

# 5. Lo que git tampoco tiene en el remoto.
$unpushed = @(Get-GitLines log --branches --not --remotes --oneline)
if ($unpushed.Count -gt 0) { [void]$warnings.Add("$($unpushed.Count) commits sin subir a GitHub. Haz git push: este respaldo no los incluye.") }
$stashes = @(Get-GitLines stash list)
if ($stashes.Count -gt 0) { [void]$warnings.Add("$($stashes.Count) entradas en git stash: este respaldo no las incluye.") }
$pending = @($untracked) + $modified | Sort-Object -Unique
if ($pending.Count -gt 0) { [void]$warnings.Add("$($pending.Count) archivos sin commit: se copiaron, pero no estan en GitHub.") }

# 6. Instrucciones y manifiesto.
@'
Restaurar CamGuard despues de formatear
---------------------------------------
Estas rutas son relativas a la raiz del repositorio clonado. RESPALDO es esta carpeta.

1. Clona el repositorio y copia RESPALDO\repo\* sobre el clon (trae .env).
2. Copia RESPALDO\claude\* a %USERPROFILE%\.claude e inicia sesion de nuevo en Claude Code.
   La carpeta de memorias depende de la ruta del repo: clona en la misma ruta.
3. Levanta todo: .\scripts\up.ps1
4. Restaura los datos con la API y Keycloak detenidos:
     $env:COMPOSE_FILE = 'docker-compose.yml;docker-compose.windows.yml'
     docker compose stop api keycloak
     docker compose cp RESPALDO\docker\camguard.sql.gz db:/tmp/r.sql.gz
     docker compose exec db sh -c "gunzip -c /tmp/r.sql.gz | psql -U camguard -d camguard"
     docker compose cp RESPALDO\docker\keycloak.sql.gz keycloak-db:/tmp/r.sql.gz
     docker compose exec keycloak-db sh -c "gunzip -c /tmp/r.sql.gz | psql -U keycloak -d keycloak"
     docker compose cp RESPALDO\docker\apidata\. api:/data
     docker compose up -d
   docker cp deja los archivos a nombre de root y la API no corre como root:
     docker compose exec -u 0 api sh -c 'chown -R "$APP_UID" /data'
     docker compose restart api
5. MANIFIESTO.csv lista cada archivo con su SHA256.

Esta carpeta contiene secretos en claro.
'@ | Set-Content -LiteralPath (Join-Path $run 'LEEME.txt') -Encoding ASCII

$manifest = Join-Path $run 'MANIFIESTO.csv'
$prefix = $run.Length + 1
$entries = foreach ($file in Get-ChildItem -LiteralPath $run -Recurse -File -Force) {
    [pscustomobject]@{
        Ruta   = $file.FullName.Substring($prefix)
        Bytes  = $file.Length
        SHA256 = (Get-FileHash -LiteralPath $file.FullName -Algorithm SHA256).Hash
    }
}
$entries | Export-Csv -LiteralPath $manifest -NoTypeInformation -Encoding UTF8

# 7. Resultado.
Write-Host ''
foreach ($warning in $warnings) { Write-Host "AVISO: $warning" -ForegroundColor Yellow }
if ($failures.Count -gt 0) {
    Write-Host "RESPALDO INCOMPLETO ($($failures.Count) fallos). No formatees todavia: $run" -ForegroundColor Red
    exit 1
}
Write-Host "Respaldo completo y verificado: $run" -ForegroundColor Green
