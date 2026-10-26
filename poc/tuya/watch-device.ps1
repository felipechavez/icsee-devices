# Vigila la cerradura en la Tuya Cloud API: muestra cada cambio de "en línea", de DPs
# y los reportes nuevos del registro del equipo, con su retraso respecto a la hora del evento.
# Sirve para ver qué señal llega al tocar el timbre / pasar frente a la cámara y cuánto tarda.
param(
    [int]$IntervalSeconds = 3
)

. "$PSScriptRoot/tuya-api.ps1"
$cfg = Import-TuyaConfig
$id = $cfg.deviceId

$online = $null
$lastStatus = @{}
$seenLogs = @{}
$since = Get-UnixMs (Get-Date).AddMinutes(-1)

Write-Host "Vigilando $id cada $IntervalSeconds s (Ctrl+C para salir). Anota la hora exacta de cada prueba."

while ($true) {
    $now = Get-Date
    $stamp = $now.ToString("HH:mm:ss.fff", [Globalization.CultureInfo]::InvariantCulture)

    try {
        # En línea / fuera de línea
        $info = Invoke-TuyaApi -Path "/v1.0/devices/$id"
        if ($info.success -and $info.result.online -ne $online) {
            $label = if ($info.result.online) { "EN LÍNEA" } else { "fuera de línea" }
            Write-Host "$stamp  [online] $label" -ForegroundColor Green
            $online = $info.result.online
        }

        # Cambios de DPs
        $status = Invoke-TuyaApi -Path "/v1.0/devices/$id/status"
        if ($status.success) {
            foreach ($s in $status.result) {
                $value = $s.value | ConvertTo-Json -Compress
                if ($lastStatus.ContainsKey($s.code) -and $lastStatus[$s.code] -ne $value) {
                    Write-Host "$stamp  [status] $($s.code): $($lastStatus[$s.code]) -> $value" -ForegroundColor Cyan
                }
                $lastStatus[$s.code] = $value
            }
        }

        # Reportes del equipo (type 7 = datos reportados, 1 = en línea, 2 = fuera de línea)
        $logs = Invoke-TuyaApi -Path "/v1.0/devices/$id/logs" -Query @{
            type = "1,2,7"; start_time = $since; end_time = (Get-UnixMs $now); size = 50
        }
        if ($logs.success) {
            foreach ($l in ($logs.result.logs | Sort-Object event_time)) {
                $key = "$($l.event_time)|$($l.event_id)|$($l.code)|$($l.value)"
                if ($seenLogs.ContainsKey($key)) { continue }
                $seenLogs[$key] = $true
                $delay = ((Get-UnixMs $now) - [long]$l.event_time) / 1000
                Write-Host ("$stamp  [log]    evento {0}  {1} = {2}  (llegó {3:N1} s después)" -f (Format-UnixMs $l.event_time), $l.code, $l.value, $delay) -ForegroundColor Magenta
            }
        } elseif (-not $script:logsWarned) {
            Write-Host "$stamp  [log]    no disponible: [$($logs.code)] $($logs.msg)" -ForegroundColor Yellow
            $script:logsWarned = $true
        }
    } catch {
        Write-Host "$stamp  error: $($_.Exception.Message)" -ForegroundColor Red
    }

    Start-Sleep -Seconds $IntervalSeconds
}
