# Comprueba si la cámara entrega eventos de movimiento por ONVIF (PullPoint):
#   1. GetCapabilities: dirección del servicio de eventos.
#   2. GetEventProperties: tópicos que declara (se resaltan los de movimiento).
#   3. CreatePullPointSubscription + PullMessages durante -Seconds: muévete frente a la cámara.
# Autenticación WS-Security UsernameToken (PasswordDigest); la hora se ajusta a la de la cámara.
# Guarda las respuestas crudas en poc/imou/out/ (sin la contraseña).
param(
    [string]$Ip = '192.168.1.102',
    [int]$Port = 80,
    [string]$User = 'admin',
    [string]$Password,
    [int]$Seconds = 60
)

$ErrorActionPreference = 'Stop'
if (-not $Password) {
    $secure = Read-Host "Código de seguridad de la cámara (etiqueta)" -AsSecureString
    $Password = [Runtime.InteropServices.Marshal]::PtrToStringAuto([Runtime.InteropServices.Marshal]::SecureStringToBSTR($secure))
}
$deviceUrl = "http://${Ip}:$Port/onvif/device_service"
$raw = [ordered]@{ ip = $Ip }
$inv = [Globalization.CultureInfo]::InvariantCulture

function Show-Section([string]$Title) { Write-Host ""; Write-Host "== $Title ==" -ForegroundColor Cyan }
function Show-Ok([string]$Text) { Write-Host "  $Text" -ForegroundColor Green }
function Show-Warn([string]$Text) { Write-Host "  $Text" -ForegroundColor Yellow }

# Diferencia entre la hora de la cámara y la local: WS-Security rechaza un Created muy desfasado.
$script:clockOffset = [TimeSpan]::Zero

function New-SecurityHeader {
    $nonce = New-Object byte[] 16
    [Security.Cryptography.RandomNumberGenerator]::Create().GetBytes($nonce)
    $created = ([DateTime]::UtcNow + $script:clockOffset).ToString("yyyy-MM-ddTHH:mm:ss.fffZ", $inv)
    $bytes = $nonce + [Text.Encoding]::UTF8.GetBytes($created + $Password)
    $digest = [Convert]::ToBase64String([Security.Cryptography.SHA1]::Create().ComputeHash($bytes))
    @"
<wsse:Security s:mustUnderstand="1" xmlns:wsse="http://docs.oasis-open.org/wss/2004/01/oasis-200401-wss-wssecurity-secext-1.0.xsd" xmlns:wsu="http://docs.oasis-open.org/wss/2004/01/oasis-200401-wss-wssecurity-utility-1.0.xsd">
<wsse:UsernameToken><wsse:Username>$User</wsse:Username>
<wsse:Password Type="http://docs.oasis-open.org/wss/2004/01/oasis-200401-wss-username-token-profile-1.0#PasswordDigest">$digest</wsse:Password>
<wsse:Nonce EncodingType="http://docs.oasis-open.org/wss/2004/01/oasis-200401-wss-soap-message-security-1.0#Base64Binary">$([Convert]::ToBase64String($nonce))</wsse:Nonce>
<wsu:Created>$created</wsu:Created></wsse:UsernameToken></wsse:Security>
"@
}

# Envía un SOAP 1.2 y devuelve el XML de respuesta (o lanza con el cuerpo del fault).
function Invoke-Onvif([string]$Url, [string]$Action, [string]$Body, [switch]$NoAuth, [int]$TimeoutSec = 10) {
    $security = if ($NoAuth) { '' } else { New-SecurityHeader }
    $envelope = @"
<?xml version="1.0" encoding="UTF-8"?>
<s:Envelope xmlns:s="http://www.w3.org/2003/05/soap-envelope" xmlns:wsa="http://www.w3.org/2005/08/addressing">
<s:Header>$security<wsa:Action>$Action</wsa:Action><wsa:To>$Url</wsa:To></s:Header>
<s:Body>$Body</s:Body></s:Envelope>
"@
    try {
        $resp = Invoke-WebRequest -UseBasicParsing -Method Post -Uri $Url -Body $envelope -TimeoutSec $TimeoutSec `
            -ContentType "application/soap+xml; charset=utf-8; action=`"$Action`""
        [xml]$resp.Content
    } catch {
        $detail = $_.Exception.Message
        if ($_.Exception.Response) {
            $reader = New-Object IO.StreamReader($_.Exception.Response.GetResponseStream())
            $text = $reader.ReadToEnd()
            if ($text -match '<[^>]*Text[^>]*>([^<]+)<') { $detail += " — $($Matches[1])" }
        }
        throw $detail
    }
}

function Select-Local([xml]$Doc, [string]$Name) { $Doc.SelectNodes("//*[local-name()='$Name']") }

# 0. Hora de la cámara (sin autenticación)
$time = Invoke-Onvif $deviceUrl 'http://www.onvif.org/ver10/device/wsdl/GetSystemDateAndTime' `
    '<GetSystemDateAndTime xmlns="http://www.onvif.org/ver10/device/wsdl"/>' -NoAuth
$utc = (Select-Local $time 'UTCDateTime')[0]
if ($utc) {
    function Get-Part([string]$Name) { [int]$utc.SelectSingleNode(".//*[local-name()='$Name']").InnerText }
    $camNow = New-Object DateTime (Get-Part 'Year'), (Get-Part 'Month'), (Get-Part 'Day'),
        (Get-Part 'Hour'), (Get-Part 'Minute'), (Get-Part 'Second'), ([DateTimeKind]::Utc)
    $script:clockOffset = $camNow - [DateTime]::UtcNow
    Write-Host ("Desfase de reloj con la cámara: {0:N0} s" -f $script:clockOffset.TotalSeconds)
}

# 1. Capacidades
Show-Section "Capacidades"
try {
    $caps = Invoke-Onvif $deviceUrl 'http://www.onvif.org/ver10/device/wsdl/GetCapabilities' `
        '<GetCapabilities xmlns="http://www.onvif.org/ver10/device/wsdl"><Category>All</Category></GetCapabilities>'
} catch {
    Show-Warn "GetCapabilities falló: $_"
    Show-Warn "Si dice 'not authorized', revisa el código de seguridad (cuidado: varios intentos fallidos bloquean la cuenta)."
    exit 1
}
$raw["capabilities"] = $caps.OuterXml
$eventsNode = Select-Local $caps 'Events' | Where-Object { $_.SelectSingleNode("*[local-name()='XAddr']") } | Select-Object -First 1
foreach ($svc in 'Device', 'Media', 'Events', 'Analytics', 'PTZ', 'Imaging') {
    $node = Select-Local $caps $svc | Where-Object { $_.SelectSingleNode("*[local-name()='XAddr']") } | Select-Object -First 1
    $addr = if ($node) { $node.SelectSingleNode("*[local-name()='XAddr']").InnerText } else { '(no)' }
    Write-Host ("  {0,-10} {1}" -f $svc, $addr)
}
if (-not $eventsNode) { Show-Warn "La cámara no declara servicio de eventos ONVIF."; exit 1 }
$eventsUrl = $eventsNode.SelectSingleNode("*[local-name()='XAddr']").InnerText
$pullSupported = $eventsNode.SelectSingleNode("*[local-name()='WSPullPointSupport']")
Write-Host "  PullPoint:  $(if ($pullSupported) { $pullSupported.InnerText } else { '(no declarado)' })"

# 2. Tópicos declarados
Show-Section "Tópicos de eventos"
try {
    $props = Invoke-Onvif $eventsUrl 'http://www.onvif.org/ver10/events/wsdl/EventPortType/GetEventPropertiesRequest' `
        '<GetEventProperties xmlns="http://www.onvif.org/ver10/events/wsdl"/>'
    $raw["eventProperties"] = $props.OuterXml
    # Un tópico es la ruta de elementos hasta uno con wstop:topic="true".
    $topics = $props.SelectNodes("//*[@*[local-name()='topic']='true']") | ForEach-Object {
        $parts = @(); $n = $_
        while ($n -and $n.LocalName -ne 'TopicSet') { $parts = ,$n.LocalName + $parts; $n = $n.ParentNode }
        $parts -join '/'
    }
    foreach ($topic in $topics) {
        if ($topic -match '(?i)motion|human|person|tamper|alarm') { Show-Ok $topic } else { Write-Host "  $topic" }
    }
} catch { Show-Warn "GetEventProperties falló: $_" }

# 3. Suscripción PullPoint y eventos en vivo
Show-Section "Eventos en vivo ($Seconds s: muévete frente a la cámara)"
try {
    $sub = Invoke-Onvif $eventsUrl 'http://www.onvif.org/ver10/events/wsdl/EventPortType/CreatePullPointSubscriptionRequest' `
        "<CreatePullPointSubscription xmlns=`"http://www.onvif.org/ver10/events/wsdl`"><InitialTerminationTime>PT$($Seconds + 30)S</InitialTerminationTime></CreatePullPointSubscription>"
} catch { Show-Warn "CreatePullPointSubscription falló: $_"; exit 1 }
$raw["subscription"] = $sub.OuterXml
$addrNode = $sub.SelectSingleNode("//*[local-name()='SubscriptionReference']/*[local-name()='Address']")
$subUrl = if ($addrNode) { $addrNode.InnerText.Trim() } else { '' }
# Algunas Dahua devuelven la dirección vacía, relativa o con un host inválido (0.0.0.0): se usa la IP conocida.
$parsed = $null
if (-not [Uri]::TryCreate($subUrl, [UriKind]::Absolute, [ref]$parsed) -or $parsed.Host -in @('0.0.0.0', 'localhost', '')) {
    if ($subUrl -match '^/') { $subUrl = "http://${Ip}:$Port$subUrl" }
    elseif ($parsed) { $subUrl = "http://${Ip}:$Port$($parsed.PathAndQuery)" }
    else { $subUrl = $eventsUrl }
    Show-Warn "La cámara no devolvió una dirección de suscripción usable; se usa $subUrl"
}
Write-Host "  Suscripción: $subUrl"

$events = @()
$deadline = (Get-Date).AddSeconds($Seconds)
while ((Get-Date) -lt $deadline) {
    try {
        $msgs = Invoke-Onvif $subUrl 'http://www.onvif.org/ver10/events/wsdl/PullPointSubscription/PullMessagesRequest' `
            '<PullMessages xmlns="http://www.onvif.org/ver10/events/wsdl"><Timeout>PT5S</Timeout><MessageLimit>20</MessageLimit></PullMessages>' -TimeoutSec 15
    } catch { Show-Warn "PullMessages falló: $_"; break }
    foreach ($m in Select-Local $msgs 'NotificationMessage') {
        $topic = $m.SelectSingleNode(".//*[local-name()='Topic']").InnerText.Trim()
        $msg = $m.SelectSingleNode(".//*[local-name()='Message']/*[local-name()='Message']")
        $when = if ($msg) { $msg.GetAttribute('UtcTime') } else { '' }
        $op = if ($msg) { $msg.GetAttribute('PropertyOperation') } else { '' }
        $items = ($m.SelectNodes(".//*[local-name()='SimpleItem']") | ForEach-Object { "$($_.GetAttribute('Name'))=$($_.GetAttribute('Value'))" }) -join ' '
        $line = "{0}  {1}  [{2}]  {3}" -f $when, $topic, $op, $items
        $events += $line
        if ($items -match '(?i)=true') { Show-Ok $line } else { Write-Host "  $line" }
    }
}
$raw["events"] = $events

try {
    Invoke-Onvif $subUrl 'http://docs.oasis-open.org/wsn/bw-2/SubscriptionManager/UnsubscribeRequest' `
        '<Unsubscribe xmlns="http://docs.oasis-open.org/wsn/b-2"/>' | Out-Null
} catch { }

Show-Section "Resumen"
$motion = @($events | Where-Object { $_ -match '(?i)motion' -and $_ -match '(?i)=true' })
if ($motion.Count -gt 0) { Show-Ok "Llegaron $($motion.Count) eventos de movimiento activos: ONVIF sirve como fuente de activación." }
elseif ($events.Count -gt 0) { Show-Warn "Llegaron $($events.Count) eventos, pero ninguno de movimiento activo. Revisa los tópicos." }
else { Show-Warn "No llegó ningún evento. ¿Está activada la detección de movimiento en la app Imou?" }

$outDir = Join-Path $PSScriptRoot "out"
New-Item -ItemType Directory -Force $outDir | Out-Null
$outFile = Join-Path $outDir ("onvif-{0}.json" -f (Get-Date).ToString("yyyyMMdd-HHmmss", $inv))
$raw | ConvertTo-Json -Depth 6 | Out-File $outFile -Encoding utf8
Write-Host ""
Write-Host "Respuestas crudas: $outFile"
