<#
.SYNOPSIS
    Levanta CamGuard con docker compose.

.DESCRIPTION
    Verifica Docker y el archivo .env, construye las imágenes, levanta los
    servicios y espera a que la web responda.

.PARAMETER NoBuild
    Levanta sin reconstruir las imágenes.

.PARAMETER Logs
    Al terminar, sigue los logs de la API.

.PARAMETER TimeoutSeconds
    Tiempo máximo de espera para que la web responda.

.EXAMPLE
    .\scripts\up.ps1
    .\scripts\up.ps1 -NoBuild -Logs
#>
[CmdletBinding()]
param(
    [switch]$NoBuild,
    [switch]$Logs,
    [int]$TimeoutSeconds = 180
)

$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
Set-Location $root

# En Docker Desktop (Windows) network_mode: host no expone go2rtc al host;
# el override lo pasa a la red bridge con puertos publicados.
$env:COMPOSE_FILE = 'docker-compose.yml;docker-compose.windows.yml'

function Fail([string]$message) {
    Write-Host "ERROR: $message" -ForegroundColor Red
    exit 1
}

function Read-DotEnv([string]$path) {
    $values = @{}
    foreach ($line in Get-Content $path) {
        $trimmed = $line.Trim()
        if ($trimmed -eq '' -or $trimmed.StartsWith('#')) { continue }
        $index = $trimmed.IndexOf('=')
        if ($index -lt 1) { continue }
        $key = $trimmed.Substring(0, $index).Trim()
        $value = $trimmed.Substring($index + 1).Trim().Trim('"').Trim("'")
        $values[$key] = $value
    }
    return $values
}

# 1. Docker disponible y corriendo
if (-not (Get-Command docker -ErrorAction SilentlyContinue)) {
    Fail 'No se encontró docker. Instala Docker Desktop.'
}
docker info --format '{{.ServerVersion}}' *> $null
if ($LASTEXITCODE -ne 0) {
    Fail 'Docker no está corriendo. Abre Docker Desktop y vuelve a intentar.'
}

# 2. Archivo .env
if (-not (Test-Path '.env')) {
    Copy-Item '.env.example' '.env'
    Write-Host 'Se creó .env a partir de .env.example. Completa los valores y vuelve a ejecutar.' -ForegroundColor Yellow
    exit 1
}

$envValues = Read-DotEnv '.env'
$required = @(
    'CAMERA_IP', 'CAMERA_PASSWORD', 'HOST_IP',
    'GO2RTC_USER', 'GO2RTC_PASSWORD',
    'SQL_PASSWORD', 'PUBLIC_URL',
    'KC_DB_PASSWORD', 'KC_ADMIN_PASSWORD', 'CAMGUARD_ADMIN_EMAIL', 'CAMGUARD_ADMIN_PASSWORD',
    'R2_ENDPOINT', 'R2_ACCESS_KEY', 'R2_SECRET_KEY'
)
$missing = $required | Where-Object { [string]::IsNullOrWhiteSpace($envValues[$_]) }
if ($missing) {
    Fail "Faltan valores en .env: $($missing -join ', ')"
}
if ($envValues['PUBLIC_URL'].EndsWith('/')) {
    Fail 'PUBLIC_URL no debe terminar en /: es la base del issuer de Keycloak.'
}
if ($envValues['GO2RTC_PASSWORD'] -notmatch '^[A-Za-z0-9]+$') {
    Fail 'GO2RTC_PASSWORD solo admite letras y números (va dentro de la URL rtsp://).'
}
$localIps = (Get-NetIPAddress -AddressFamily IPv4 -ErrorAction SilentlyContinue).IPAddress
if ($localIps -and $envValues['HOST_IP'] -notin $localIps) {
    Write-Host "AVISO: HOST_IP=$($envValues['HOST_IP']) no es una IP de este equipo ($($localIps -join ', ')). El video en vivo (WebRTC) no va a conectar." -ForegroundColor Yellow
}
if ($envValues['R2_ENDPOINT'] -match '<account-id>') {
    Fail 'R2_ENDPOINT todavía tiene el valor de ejemplo.'
}

# 3. Puerto de la web libre (o ya en uso por nuestro propio contenedor web)
$webPort = if ($envValues['WEB_PORT']) { $envValues['WEB_PORT'] } else { '8080' }
$holder = docker ps --format '{{.Names}}|{{.Ports}}' |
    Where-Object { $_ -match ":$webPort->" -and $_ -notmatch '^icsee-devices-web-' }
if ($holder) {
    Fail "El puerto $webPort lo usa el contenedor $($holder.Split('|')[0]). Detenlo o cambia WEB_PORT en .env."
}
if ((Get-NetTCPConnection -LocalPort $webPort -State Listen -ErrorAction SilentlyContinue)) {
    $ownWeb = docker ps --filter 'name=icsee-devices-web-' --format '{{.Ports}}' | Where-Object { $_ -match ":$webPort->" }
    if (-not $ownWeb) {
        Fail "El puerto $webPort está ocupado por otro proceso. Libéralo o cambia WEB_PORT en .env."
    }
}

# 4. Levantar servicios
$composeArgs = @('compose', 'up', '-d')
if (-not $NoBuild) { $composeArgs += '--build' }
Write-Host "docker $($composeArgs -join ' ')" -ForegroundColor Cyan
& docker @composeArgs
if ($LASTEXITCODE -ne 0) { Fail 'docker compose up falló.' }

# Si un intento anterior falló al publicar el puerto, compose no recrea el
# contenedor web y queda sin puerto; recrearlo es barato (solo nginx).
docker compose up -d --force-recreate --no-deps web
if ($LASTEXITCODE -ne 0) { Fail 'No se pudo recrear el contenedor web.' }

# 5. Esperar a que la web responda (la API aplica migraciones al arrancar)
$url = "http://localhost:$webPort"
Write-Host "Esperando a $url ..." -ForegroundColor Cyan
$deadline = (Get-Date).AddSeconds($TimeoutSeconds)
$ready = $false
while ((Get-Date) -lt $deadline) {
    try {
        $response = Invoke-WebRequest -Uri $url -UseBasicParsing -TimeoutSec 5
        if ($response.StatusCode -eq 200) { $ready = $true; break }
    } catch { }
    Start-Sleep -Seconds 3
}

docker compose ps
if (-not $ready) {
    Write-Host "La web no respondió en $TimeoutSeconds s. Revisa: docker compose logs api" -ForegroundColor Yellow
    exit 1
}

Write-Host "CamGuard listo en $url (pública: $($envValues['PUBLIC_URL']))" -ForegroundColor Green

if ($Logs) {
    docker compose logs -f api
}
