# Graba un clip desde el RTSP de go2rtc, con los mismos parámetros que usa la API.
# Si FFmpeg no está instalado en Windows, usa el que trae el contenedor de go2rtc.
param(
    [int]$Seconds = 20,
    [string]$Url = "rtsp://localhost:8554/icsee",
    [string]$Container = "go2rtc-poc"
)

$dir = Join-Path $PSScriptRoot "clips"
New-Item -ItemType Directory -Force $dir | Out-Null
$name = "clip-{0:yyyyMMdd-HHmmss}.mp4" -f (Get-Date)
$out = Join-Path $dir $name

$ffmpegArgs = @(
    "-hide_banner", "-loglevel", "warning",
    "-rtsp_transport", "tcp", "-timeout", "10000000",
    "-i", $Url,
    "-t", $Seconds,
    "-map", "0:v:0", "-map", "0:a:0?",
    "-c:v", "copy", "-c:a", "aac",
    "-movflags", "+faststart"
)

$useDocker = -not (Get-Command ffmpeg -ErrorAction SilentlyContinue)
if ($useDocker) {
    $running = docker ps --filter "name=^$Container$" --format "{{.Names}}" 2>$null
    if (-not $running) {
        Write-Error "No se encontró ffmpeg ni el contenedor '$Container' en ejecución. Arranca go2rtc con Docker o instala FFmpeg (winget install Gyan.FFmpeg)."
        exit 1
    }
    Write-Host "Usando FFmpeg del contenedor $Container"
}

Write-Host "Inicio: $(Get-Date -Format 'HH:mm:ss.fff')"
if ($useDocker) {
    # Dentro del contenedor, localhost:8554 es el propio go2rtc.
    docker exec $Container ffmpeg @ffmpegArgs -y "/tmp/$name"
    if ($LASTEXITCODE -eq 0) {
        docker cp "${Container}:/tmp/$name" $out | Out-Null
        docker exec $Container rm -f "/tmp/$name"
    }
} else {
    ffmpeg @ffmpegArgs -y $out
}
Write-Host "Fin:    $(Get-Date -Format 'HH:mm:ss.fff')"

if (Test-Path $out) {
    Write-Host "Clip:   $out ($([math]::Round((Get-Item $out).Length / 1KB)) KB)"
} else {
    Write-Error "No se generó el clip. ¿La cámara estaba despierta? Revisa: docker logs $Container"
    exit 1
}
