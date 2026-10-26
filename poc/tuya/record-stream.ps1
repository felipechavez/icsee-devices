# Pide a la Tuya Cloud API una URL de stream nueva (son temporales) y graba un clip con
# los mismos parámetros que usa CamGuard (poc/record-clip.ps1).
# Despierta la cerradura (timbre) justo antes de ejecutarlo.
param(
    [int]$Seconds = 20,
    [ValidateSet("rtsp", "hls")] [string]$Type = "rtsp",
    # La cámara de la C20 va montada de lado: 90° a la derecha la deja derecha.
    [ValidateSet(0, 90, 180, 270)] [int]$Rotate = 90
)

. "$PSScriptRoot/tuya-api.ps1"
$cfg = Import-TuyaConfig

$requested = Get-Date
$stream = Invoke-TuyaApi -Method POST -Path "/v1.0/devices/$($cfg.deviceId)/stream/actions/allocate" -Body @{ type = $Type }
if (-not $stream.success) {
    Write-Error "No se obtuvo URL de stream: [$($stream.code)] $($stream.msg)"
    exit 1
}
Write-Host ("URL {0} obtenida en {1:N1} s (pedida a las {2})" -f $Type, ((Get-Date) - $requested).TotalSeconds, $requested.ToString("HH:mm:ss.fff", [Globalization.CultureInfo]::InvariantCulture))

# La URL lleva credenciales temporales: no se muestra completa.
& (Join-Path $PSScriptRoot "../record-clip.ps1") -Seconds $Seconds -Url $stream.result.url -Rotate $Rotate
