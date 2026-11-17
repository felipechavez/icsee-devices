# Averigua qué expone en la LAN una cámara Imou (base Dahua), p. ej. la Cue 2:
#   1. RTSP (puerto 554): DESCRIBE con autenticación Digest sobre el stream principal y el substream.
#   2. CGI HTTP de Dahua: modelo, firmware y configuración de detección de movimiento.
#   3. Eventos de movimiento en vivo (eventManager.cgi attach): muévete frente a la cámara.
#   4. ONVIF: si responde el servicio de dispositivo.
# La contraseña es el "código de seguridad" (Safety Code) de la etiqueta de la cámara; usuario admin.
# Guarda las respuestas crudas en poc/imou/out/ (sin la contraseña).
param(
    [string]$Ip = '192.168.1.102',
    [string]$User = 'admin',
    [string]$Password,
    [int]$RtspPort = 554,
    [int]$HttpPort = 80,
    [int]$EventSeconds = 30
)

$ErrorActionPreference = 'Stop'
if (-not $Password) {
    $secure = Read-Host "Código de seguridad de la cámara (etiqueta)" -AsSecureString
    $Password = [Runtime.InteropServices.Marshal]::PtrToStringAuto([Runtime.InteropServices.Marshal]::SecureStringToBSTR($secure))
}
$raw = [ordered]@{ ip = $Ip }

function Show-Section([string]$Title) { Write-Host ""; Write-Host "== $Title ==" -ForegroundColor Cyan }
function Show-Ok([string]$Text) { Write-Host "  $Text" -ForegroundColor Green }
function Show-Warn([string]$Text) { Write-Host "  $Text" -ForegroundColor Yellow }

function Get-Md5([string]$Text) {
    $md5 = [Security.Cryptography.MD5]::Create()
    -join ($md5.ComputeHash([Text.Encoding]::UTF8.GetBytes($Text)) | ForEach-Object { $_.ToString('x2') })
}

# Una petición RTSP sobre una conexión ya abierta; devuelve el código, los encabezados y el cuerpo.
function Invoke-Rtsp($Stream, [string]$Method, [string]$Url, [int]$CSeq, [string]$Auth) {
    $req = "$Method $Url RTSP/1.0`r`nCSeq: $CSeq`r`nUser-Agent: CamGuard-probe`r`nAccept: application/sdp`r`n"
    if ($Auth) { $req += "Authorization: $Auth`r`n" }
    $req += "`r`n"
    $bytes = [Text.Encoding]::ASCII.GetBytes($req)
    $Stream.Write($bytes, 0, $bytes.Length)

    $buffer = New-Object byte[] 16384
    $text = ''
    do {
        $n = $Stream.Read($buffer, 0, $buffer.Length)
        if ($n -le 0) { break }
        $text += [Text.Encoding]::ASCII.GetString($buffer, 0, $n)
        $headerEnd = $text.IndexOf("`r`n`r`n")
        $complete = $false
        if ($headerEnd -ge 0) {
            $len = if ($text -match '(?im)^Content-Length:\s*(\d+)') { [int]$Matches[1] } else { 0 }
            $complete = $text.Length -ge ($headerEnd + 4 + $len)
        }
    } while (-not $complete)

    $headerEnd = $text.IndexOf("`r`n`r`n")
    $code = if ($text -match '^RTSP/1\.0 (\d+)') { [int]$Matches[1] } else { 0 }
    [pscustomobject]@{
        Code    = $code
        Headers = if ($headerEnd -ge 0) { $text.Substring(0, $headerEnd) } else { $text }
        Body    = if ($headerEnd -ge 0) { $text.Substring($headerEnd + 4) } else { '' }
    }
}

function Test-RtspStream([string]$Path) {
    $url = "rtsp://${Ip}:$RtspPort$Path"
    $client = New-Object Net.Sockets.TcpClient
    try {
        $client.ReceiveTimeout = 5000; $client.SendTimeout = 5000
        $client.Connect($Ip, $RtspPort)
        $s = $client.GetStream()
        $r = Invoke-Rtsp $s 'DESCRIBE' $url 1 $null
        if ($r.Code -eq 401) {
            if ($r.Headers -notmatch 'WWW-Authenticate:\s*Digest\s+[^\r\n]*realm="([^"]+)"[^\r\n]*nonce="([^"]+)"' -and
                $r.Headers -notmatch 'WWW-Authenticate:\s*Digest\s+[^\r\n]*nonce="([^"]+)"[^\r\n]*realm="([^"]+)"') {
                return [pscustomobject]@{ Code = 401; Note = 'Pide autenticación no Digest'; Headers = $r.Headers; Body = '' }
            }
            $realm = if ($r.Headers -match 'realm="([^"]+)"') { $Matches[1] }
            $nonce = if ($r.Headers -match 'nonce="([^"]+)"') { $Matches[1] }
            $ha1 = Get-Md5 "${User}:${realm}:$Password"
            $ha2 = Get-Md5 "DESCRIBE:$url"
            $resp = Get-Md5 "${ha1}:${nonce}:$ha2"
            $auth = "Digest username=`"$User`", realm=`"$realm`", nonce=`"$nonce`", uri=`"$url`", response=`"$resp`""
            $r = Invoke-Rtsp $s 'DESCRIBE' $url 2 $auth
        }
        [pscustomobject]@{ Code = $r.Code; Headers = $r.Headers; Body = $r.Body }
    } catch {
        [pscustomobject]@{ Code = 0; Note = $_.Exception.Message; Headers = ''; Body = '' }
    } finally { $client.Dispose() }
}

# CGI de Dahua con Digest (curl.exe lo resuelve). Devuelve código HTTP y cuerpo.
function Invoke-Cgi([string]$PathAndQuery, [int]$MaxSeconds = 8) {
    $tmp = [IO.Path]::GetTempFileName()
    try {
        $code = & curl.exe -s -g --digest -u "${User}:$Password" --max-time $MaxSeconds -o $tmp -w '%{http_code}' "http://${Ip}:$HttpPort$PathAndQuery"
        [pscustomobject]@{ Code = [int]$code; Body = (Get-Content $tmp -Raw -ErrorAction SilentlyContinue) }
    } finally { Remove-Item $tmp -ErrorAction SilentlyContinue }
}

# 1. RTSP
Show-Section "RTSP ($Ip`:$RtspPort)"
foreach ($sub in 0, 1) {
    $path = "/cam/realmonitor?channel=1&subtype=$sub"
    $r = Test-RtspStream $path
    $raw["rtsp_subtype$sub"] = $r
    $label = if ($sub -eq 0) { 'principal' } else { 'substream' }
    if ($r.Code -eq 200) {
        $codecs = ([regex]::Matches($r.Body, 'a=rtpmap:\d+ ([^\r\n]+)') | ForEach-Object { $_.Groups[1].Value }) -join ', '
        Show-Ok "$label OK  rtsp://${User}:***@${Ip}:$RtspPort$path"
        Write-Host "    Códecs: $codecs"
    } elseif ($r.Code -eq 401) {
        Show-Warn "$label rechazó la contraseña (401). ¿Es el código de seguridad de la etiqueta?"
    } else {
        Show-Warn "$label sin respuesta válida: código $($r.Code) $($r.Note)"
    }
}

# 2. CGI de Dahua
Show-Section "CGI HTTP de Dahua ($Ip`:$HttpPort)"
$cgi = [ordered]@{
    deviceType      = '/cgi-bin/magicBox.cgi?action=getDeviceType'
    systemInfo      = '/cgi-bin/magicBox.cgi?action=getSystemInfo'
    softwareVersion = '/cgi-bin/magicBox.cgi?action=getSoftwareVersion'
    motionDetect    = '/cgi-bin/configManager.cgi?action=getConfig&name=MotionDetect'
    smdDetect       = '/cgi-bin/configManager.cgi?action=getConfig&name=SmartMotionDetect'
    eventCodes      = '/cgi-bin/eventManager.cgi?action=getExposureEvents'
}
$cgiWorks = $false
$cgiSilent = $false
foreach ($k in $cgi.Keys) {
    $r = Invoke-Cgi $cgi[$k]
    $raw["cgi_$k"] = $r
    # Sin respuesta HTTP alguna (ni 401): el servidor web no atiende el CGI; no vale la pena seguir.
    if ($r.Code -eq 0) {
        $cgiSilent = $true
        Show-Warn "${k}: sin respuesta HTTP (el CGI de Dahua no parece estar disponible); se omite el resto."
        break
    }
    if ($r.Code -eq 200) {
        $cgiWorks = $true
        Show-Ok "${k}: OK"
        ($r.Body -split "`r?`n" | Where-Object { $_ } | Select-Object -First 8) | ForEach-Object { Write-Host "    $_" }
    } else {
        Show-Warn "${k}: HTTP $($r.Code)"
    }
}

# 3. Eventos de movimiento en vivo
Show-Section "Eventos de movimiento ($EventSeconds s: muévete frente a la cámara)"
$events = if ($cgiSilent) { [pscustomobject]@{ Code = 0; Body = '' } }
          else { Invoke-Cgi '/cgi-bin/eventManager.cgi?action=attach&codes=[All]&heartbeat=5' ($EventSeconds) }
$raw["cgi_eventAttach"] = $events
$lines = @($events.Body -split "`r?`n" | Where-Object { $_ -match '^Code=' })
if ($lines.Count -gt 0) {
    Show-Ok "Llegaron $($lines.Count) eventos:"
    $lines | Select-Object -First 20 | ForEach-Object { Write-Host "    $_" }
    if ($lines -match 'Code=VideoMotion|Code=SmartMotion') { Show-Ok "Hay eventos de movimiento: sirve como fuente local de activación." }
} elseif ($events.Code -eq 200) {
    Show-Warn "La conexión se abrió (HTTP 200) pero no llegó ningún evento en $EventSeconds s."
} else {
    Show-Warn "attach no disponible: HTTP $($events.Code)"
}

# 4. ONVIF (GetSystemDateAndTime no exige autenticación)
Show-Section "ONVIF"
$soap = '<?xml version="1.0" encoding="UTF-8"?><s:Envelope xmlns:s="http://www.w3.org/2003/05/soap-envelope"><s:Body><GetSystemDateAndTime xmlns="http://www.onvif.org/ver10/device/wsdl"/></s:Body></s:Envelope>'
try {
    $resp = Invoke-WebRequest -UseBasicParsing -Method Post -Uri "http://${Ip}:$HttpPort/onvif/device_service" -ContentType 'application/soap+xml' -Body $soap -TimeoutSec 5
    $raw["onvif"] = @{ code = $resp.StatusCode; body = $resp.Content }
    Show-Ok "ONVIF responde (HTTP $($resp.StatusCode))"
} catch {
    $code = if ($_.Exception.Response) { [int]$_.Exception.Response.StatusCode } else { 0 }
    $raw["onvif"] = @{ code = $code; error = $_.Exception.Message }
    Show-Warn "ONVIF no disponible: $($_.Exception.Message)"
}

# Resumen
Show-Section "Resumen"
$rtspOk = $raw["rtsp_subtype0"].Code -eq 200
Write-Host ("  RTSP:              {0}" -f $(if ($rtspOk) { 'sí' } else { 'no' }))
Write-Host ("  CGI Dahua:         {0}" -f $(if ($cgiWorks) { 'sí' } else { 'no' }))
Write-Host ("  Eventos en vivo:   {0}" -f $(if ($lines.Count -gt 0) { 'sí' } else { 'no' }))
Write-Host ("  ONVIF:             {0}" -f $(if ($raw["onvif"].code -eq 200) { 'sí' } else { 'no' }))

# Respuestas crudas (sin contraseña)
$outDir = Join-Path $PSScriptRoot "out"
New-Item -ItemType Directory -Force $outDir | Out-Null
$outFile = Join-Path $outDir ("probe-{0}.json" -f (Get-Date).ToString("yyyyMMdd-HHmmss", [Globalization.CultureInfo]::InvariantCulture))
$raw | ConvertTo-Json -Depth 8 | Out-File $outFile -Encoding utf8
Write-Host ""
Write-Host "Respuestas crudas: $outFile"
