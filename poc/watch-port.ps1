# Muestra cada cambio despierta/dormida del puerto DVRIP, igual que CameraMonitorService.
param(
    [string]$Ip = "192.168.1.86",
    [int]$Port = 34567,
    [int]$TimeoutMs = 800
)

$awake = $null
$since = Get-Date
Write-Host "Vigilando ${Ip}:${Port} (Ctrl+C para salir)"

while ($true) {
    $client = [System.Net.Sockets.TcpClient]::new()
    try {
        $open = $client.ConnectAsync($Ip, $Port).Wait($TimeoutMs)
    } catch {
        $open = $false
    } finally {
        $client.Dispose()
    }

    if ($open -ne $awake) {
        $now = Get-Date
        $previous = if ($null -eq $awake) { "" } else { " (estuvo {0:N1} s {1})" -f ($now - $since).TotalSeconds, $(if ($awake) { "despierta" } else { "dormida" }) }
        $state = if ($open) { "DESPIERTA" } else { "dormida" }
        Write-Host ("{0:HH:mm:ss.fff}  {1}{2}" -f $now, $state, $previous)
        $awake = $open
        $since = $now
    }

    Start-Sleep -Milliseconds 1000
}
