# Consulta en la Tuya Cloud API qué expone la cerradura: datos del equipo, DPs
# (funciones y estado), registros de la cerradura y si admite video en vivo.
# Guarda las respuestas crudas en poc/tuya/out/ para revisarlas después.
param(
    [int]$LogDays = 7
)

. "$PSScriptRoot/tuya-api.ps1"
$cfg = Import-TuyaConfig
$id = $cfg.deviceId
$raw = [ordered]@{}

function Show-Section([string]$Title) { Write-Host ""; Write-Host "== $Title ==" -ForegroundColor Cyan }

function Show-Failure($Resp) { Write-Host "  No disponible: [$($Resp.code)] $($Resp.msg)" -ForegroundColor Yellow }

# Los endpoints de registros no usan el mismo nombre de lista (logs, records…): se toma la primera.
function Show-LogItems($Result) {
    $list = $Result.PSObject.Properties | Where-Object { $_.Value -is [array] } | Select-Object -First 1
    if (-not $list -or $list.Value.Count -eq 0) { Write-Host "  (sin registros)"; return }
    foreach ($item in $list.Value) { Write-Host "  $($item | ConvertTo-Json -Compress -Depth 5)" }
}

# 1. Equipo
Show-Section "Equipo"
$info = Invoke-TuyaApi -Path "/v1.0/devices/$id"
$raw["device"] = $info
if (-not $info.success) {
    Show-Failure $info
    Write-Host "Sin datos del equipo; no se sigue. Si el error es de suscripción, renueva el servicio en Cloud Services;"
    Write-Host "si es de permisos o equipo, revisa deviceId y que la cuenta de la app esté vinculada al proyecto."
    exit 1
}
$d = $info.result
Write-Host "  Nombre:     $($d.name)"
Write-Host "  Producto:   $($d.product_name) ($($d.product_id))"
Write-Host "  Categoría:  $($d.category)"
Write-Host "  En línea:   $($d.online)"
Write-Host "  uid:        $($d.uid)   (para la fuente tuya:// de go2rtc con la Cloud API)"
Write-Host "  Sub-device: $($d.sub)"
# local_key permite controlar el equipo en la LAN: no se muestra ni se guarda.
if ($d.PSObject.Properties["local_key"]) { $d.local_key = "***" }

# 2. Especificación de DPs (qué funciones y estados declara)
Show-Section "DPs declarados"
$spec = Invoke-TuyaApi -Path "/v1.0/devices/$id/specifications"
$raw["specifications"] = $spec
if ($spec.success) {
    Write-Host "  Estados (lo que reporta):"
    foreach ($s in $spec.result.status) { Write-Host ("    {0,-28} {1,-8} {2}" -f $s.code, $s.type, $s.values) }
    Write-Host "  Funciones (lo que acepta):"
    foreach ($f in $spec.result.functions) { Write-Host ("    {0,-28} {1,-8} {2}" -f $f.code, $f.type, $f.values) }
} else { Show-Failure $spec }

# 3. Estado actual
Show-Section "Estado actual"
$status = Invoke-TuyaApi -Path "/v1.0/devices/$id/status"
$raw["status"] = $status
if ($status.success) {
    foreach ($s in $status.result) { Write-Host ("  {0,-28} = {1}" -f $s.code, ($s.value | ConvertTo-Json -Compress)) }
} else { Show-Failure $status }

# 4. Registros propios de cerradura (Smart Lock Open Service)
$now = Get-Date
$range = @{ start_time = (Get-UnixMs $now.AddDays(-$LogDays)); end_time = (Get-UnixMs $now) }

Show-Section "Aperturas (últimos $LogDays días)"
$open = Invoke-TuyaApi -Path "/v1.0/devices/$id/door-lock/open-logs" -Query ($range + @{ page_no = 1; page_size = 10 })
$raw["openLogs"] = $open
if ($open.success) { Show-LogItems $open.result } else { Show-Failure $open }

Show-Section "Alarmas (últimos $LogDays días)"
$alarm = Invoke-TuyaApi -Path "/v1.0/devices/$id/door-lock/alarm-logs" -Query ($range + @{ page_no = 1; page_size = 10 })
$raw["alarmLogs"] = $alarm
if ($alarm.success) { Show-LogItems $alarm.result } else { Show-Failure $alarm }

# 5. Video: lo mismo que pide go2rtc (webrtc-configs) y la URL de stream de la API de IPC.
#    Requiere el servicio "IoT Video Live Stream" y, normalmente, la cerradura despierta.
Show-Section "Video en vivo (despierta la cerradura antes: toca el timbre)"
if ($d.uid) {
    $webrtc = Invoke-TuyaApi -Path "/v1.0/users/$($d.uid)/devices/$id/webrtc-configs"
    $raw["webrtcConfigs"] = $webrtc
    if ($webrtc.success) {
        Write-Host "  webrtc-configs: OK (supports_webrtc = $($webrtc.result.supports_webrtc))" -ForegroundColor Green
        # El resultado trae credenciales temporales de ICE/MQTT: no se guardan.
        $raw["webrtcConfigs"] = @{ success = $true; supports_webrtc = $webrtc.result.supports_webrtc }
    } else { Write-Host -NoNewline "  webrtc-configs:"; Show-Failure $webrtc }
}

foreach ($type in "rtsp", "hls") {
    $stream = Invoke-TuyaApi -Method POST -Path "/v1.0/devices/$id/stream/actions/allocate" -Body @{ type = $type }
    $raw["stream_$type"] = if ($stream.success) { @{ success = $true } } else { $stream }
    if ($stream.success) {
        # La URL lleva un token temporal: se muestra pero no se guarda.
        Write-Host "  stream ${type}: $($stream.result.url)" -ForegroundColor Green
    } else { Write-Host -NoNewline "  stream ${type}:"; Show-Failure $stream }
}

# Respuestas crudas
$outDir = Join-Path $PSScriptRoot "out"
New-Item -ItemType Directory -Force $outDir | Out-Null
$outFile = Join-Path $outDir ("probe-{0}.json" -f $now.ToString("yyyyMMdd-HHmmss", [Globalization.CultureInfo]::InvariantCulture))
$raw | ConvertTo-Json -Depth 12 | Out-File $outFile -Encoding utf8
Write-Host ""
Write-Host "Respuestas crudas: $outFile"
