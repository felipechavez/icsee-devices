# Mueve los planes sueltos de .claude/plans/ (nombre aleatorio del modo plan)
# a .claude/plans/Sprint<N>_<Nombre>/plan.md.
#
# Lo ejecuta el hook PostToolUse de ExitPlanMode (al aprobar un plan), pero
# tambien se puede correr a mano:
#   .\.claude\hooks\move-plan.ps1                     # sprint y nombre automaticos
#   .\.claude\hooks\move-plan.ps1 -Name Notificaciones
#   .\.claude\hooks\move-plan.ps1 -Sprint 5 -Name Notificaciones
param(
    [int]$Sprint = 0,
    [string]$Name = ''
)

$ErrorActionPreference = 'Stop'

# El hook recibe JSON por stdin; no se usa, pero se consume para no bloquear.
if ([Console]::IsInputRedirected) { [void][Console]::In.ReadToEnd() }

$root = (Resolve-Path (Join-Path $PSScriptRoot '..\..')).Path
$plansDir = Join-Path $root '.claude\plans'
if (-not (Test-Path $plansDir)) { exit 0 }

$loose = @(Get-ChildItem -Path $plansDir -File -Filter '*.md' | Sort-Object LastWriteTime)
if ($loose.Count -eq 0) { exit 0 }

$stopWords = @('a', 'al', 'con', 'de', 'del', 'el', 'en', 'la', 'las', 'lo', 'los',
    'para', 'por', 'un', 'una', 'y', 'o', 'plan', 'sprint', 'camguard')

function Get-SprintName([string]$text) {
    # Quita tildes: "Visualización" -> "Visualizacion"
    $plain = -join ($text.Normalize([Text.NormalizationForm]::FormD).ToCharArray() |
        Where-Object { [Globalization.CharUnicodeInfo]::GetUnicodeCategory($_) -ne 'NonSpacingMark' })
    $words = @($plain -split '[^A-Za-z0-9]+' |
        Where-Object { $_ -and ($stopWords -notcontains $_.ToLowerInvariant()) -and ($_ -notmatch '^\d+$') } |
        Select-Object -First 3)
    if ($words.Count -eq 0) { return 'Plan' }
    return -join ($words | ForEach-Object { $_.Substring(0, 1).ToUpperInvariant() + $_.Substring(1) })
}

function Get-NextSprint {
    $numbers = @(Get-ChildItem -Path $plansDir -Directory |
        Where-Object { $_.Name -match '^Sprint(\d+)_' } |
        ForEach-Object { [int]$Matches[1] })
    if ($numbers.Count -eq 0) { return 1 }
    return ($numbers | Measure-Object -Maximum).Maximum + 1
}

$moved = @()
foreach ($file in $loose) {
    $number = if ($Sprint -gt 0 -and $loose.Count -eq 1) { $Sprint } else { Get-NextSprint }

    $sprintName = $Name
    if (-not $sprintName) {
        $heading = Get-Content -Path $file.FullName -Encoding UTF8 |
            Where-Object { $_ -match '^#\s+' } | Select-Object -First 1
        $sprintName = Get-SprintName ($heading -replace '^#\s+', '')
    }

    $target = Join-Path $plansDir ("Sprint{0}_{1}" -f $number, $sprintName)
    if (Test-Path (Join-Path $target 'plan.md')) {
        $target = Join-Path $plansDir ("Sprint{0}_{1}_{2}" -f $number, $sprintName, $file.BaseName)
    }
    New-Item -ItemType Directory -Force -Path $target | Out-Null
    Move-Item -Path $file.FullName -Destination (Join-Path $target 'plan.md')
    $moved += ('.claude/plans/{0}/plan.md' -f (Split-Path $target -Leaf))
}

$list = $moved -join ', '
@{
    systemMessage      = "Plan guardado en $list"
    hookSpecificOutput = @{
        hookEventName     = 'PostToolUse'
        additionalContext = "El plan aprobado se movio a $list; edita ese archivo si hay que cambiarlo."
    }
} | ConvertTo-Json -Compress
