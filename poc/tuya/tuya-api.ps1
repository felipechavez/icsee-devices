# Cliente mínimo de la Tuya Cloud API (OpenAPI) con firma HMAC-SHA256.
# Se carga con: . "$PSScriptRoot/tuya-api.ps1"
# Credenciales en poc/tuya/tuya.poc.json (ignorado por git; ver tuya.poc.example.json).

[Net.ServicePointManager]::SecurityProtocol = [Net.SecurityProtocolType]::Tls12

$script:TuyaConfig = $null
$script:TuyaToken = $null
$script:TuyaTokenExpires = [DateTime]::MinValue

function Import-TuyaConfig {
    $path = Join-Path $PSScriptRoot "tuya.poc.json"
    if (-not (Test-Path $path)) {
        throw "Falta $path. Copia tuya.poc.example.json a tuya.poc.json y completa los valores."
    }
    $script:TuyaConfig = Get-Content $path -Raw -Encoding UTF8 | ConvertFrom-Json
    foreach ($key in "endpoint", "clientId", "clientSecret", "deviceId") {
        if (-not $script:TuyaConfig.$key) { throw "tuya.poc.json: falta '$key'." }
    }
    $script:TuyaConfig.endpoint = $script:TuyaConfig.endpoint.TrimEnd("/")
    return $script:TuyaConfig
}

function Get-Sha256Hex([byte[]]$Bytes) {
    if ($null -eq $Bytes) { $Bytes = [byte[]]::new(0) }
    $sha = [Security.Cryptography.SHA256]::Create()
    try { return (($sha.ComputeHash($Bytes) | ForEach-Object { $_.ToString("x2") }) -join "") }
    finally { $sha.Dispose() }
}

function Get-HmacHex([string]$Secret, [string]$Message) {
    $hmac = [Security.Cryptography.HMACSHA256]::new([Text.Encoding]::UTF8.GetBytes($Secret))
    try {
        $hash = $hmac.ComputeHash([Text.Encoding]::UTF8.GetBytes($Message))
        return (($hash | ForEach-Object { $_.ToString("X2") }) -join "")
    }
    finally { $hmac.Dispose() }
}

# Ruta + query ordenada por clave: así la firma Tuya espera la URL.
function Format-TuyaUrl([string]$Path, [hashtable]$Query) {
    if (-not $Query -or $Query.Count -eq 0) { return $Path }
    $pairs = $Query.Keys | Sort-Object | ForEach-Object { "$_=$($Query[$_])" }
    return "${Path}?$($pairs -join '&')"
}

function Send-TuyaRequest([string]$Method, [string]$Url, [byte[]]$Body, [string]$AccessToken) {
    $cfg = $script:TuyaConfig
    $t = [DateTimeOffset]::UtcNow.ToUnixTimeMilliseconds().ToString()
    $nonce = [guid]::NewGuid().ToString("N")
    $contentSha = Get-Sha256Hex $Body
    $stringToSign = "$Method`n$contentSha`n`n$Url"
    $sign = Get-HmacHex $cfg.clientSecret ($cfg.clientId + $AccessToken + $t + $nonce + $stringToSign)

    $headers = @{
        "client_id"   = $cfg.clientId
        "sign"        = $sign
        "t"           = $t
        "nonce"       = $nonce
        "sign_method" = "HMAC-SHA256"
    }
    if ($AccessToken) { $headers["access_token"] = $AccessToken }

    $params = @{
        Method      = $Method
        Uri         = "$($cfg.endpoint)$Url"
        Headers     = $headers
        ContentType = "application/json"
    }
    if ($Body) { $params["Body"] = $Body }
    return Invoke-RestMethod @params
}

function Get-TuyaToken {
    if ($script:TuyaToken -and [DateTime]::UtcNow -lt $script:TuyaTokenExpires) { return $script:TuyaToken }
    $resp = Send-TuyaRequest "GET" "/v1.0/token?grant_type=1" $null ""
    if (-not $resp.success) {
        throw "No se obtuvo token de Tuya: [$($resp.code)] $($resp.msg). Revisa clientId/clientSecret y el endpoint (región)."
    }
    $script:TuyaToken = $resp.result.access_token
    # Renueva un minuto antes de que caduque.
    $script:TuyaTokenExpires = [DateTime]::UtcNow.AddSeconds([int]$resp.result.expire_time - 60)
    return $script:TuyaToken
}

# Devuelve la respuesta tal cual ({ success, result, code, msg }); no lanza si success = false.
function Invoke-TuyaApi {
    param(
        [ValidateSet("GET", "POST", "PUT", "DELETE")] [string]$Method = "GET",
        [Parameter(Mandatory)] [string]$Path,
        [hashtable]$Query,
        $Body
    )
    $url = Format-TuyaUrl $Path $Query
    $bytes = if ($null -ne $Body) { [Text.Encoding]::UTF8.GetBytes(($Body | ConvertTo-Json -Depth 10 -Compress)) } else { $null }
    return Send-TuyaRequest $Method $url $bytes (Get-TuyaToken)
}

function Get-UnixMs([DateTime]$Date) {
    return [DateTimeOffset]::new($Date.ToUniversalTime()).ToUnixTimeMilliseconds()
}

function Format-UnixMs([long]$Ms) {
    return [DateTimeOffset]::FromUnixTimeMilliseconds($Ms).LocalDateTime.ToString("HH:mm:ss.fff", [Globalization.CultureInfo]::InvariantCulture)
}
