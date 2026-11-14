# Detecta sprints terminados sin revision de seguridad.
#
# Un sprint esta "terminado" cuando ya hay otro plan mas nuevo: el sprint en
# curso es el plan.md con la fecha de modificacion mas reciente (o un plan
# suelto en .claude/plans/, recien aprobado y aun sin mover). Cada sprint
# terminado debe tener .claude/plans/Sprint<N>_<Nombre>/seguridad.md, que
# escribe el comando /cerrar-sprint.
#
# Lo ejecuta el hook PostToolUse de ExitPlanMode (al aprobar un plan nuevo, el
# anterior termino) y avisa a Claude para que revise antes de implementar.
# Tambien se puede correr a mano:
#   .\.claude\hooks\check-sprint-security.ps1 -List    # texto plano, para /cerrar-sprint
param(
    [switch]$List
)

$ErrorActionPreference = 'Stop'

# El hook recibe JSON por stdin; no se usa, pero se consume para no bloquear.
if (-not $List -and [Console]::IsInputRedirected) { [void][Console]::In.ReadToEnd() }

$root = (Resolve-Path (Join-Path $PSScriptRoot '..\..')).Path
$plansDir = Join-Path $root '.claude\plans'
if (-not (Test-Path $plansDir)) { exit 0 }

$baselinePath = Join-Path $PSScriptRoot 'security-baseline.json'
$baseline = if (Test-Path $baselinePath) { Get-Content $baselinePath -Raw -Encoding UTF8 | ConvertFrom-Json } else { $null }
$exempt = if ($baseline) { @($baseline.exemptSprints) } else { @() }

$sprints = @(Get-ChildItem -Path $plansDir -Directory |
    Where-Object { $_.Name -match '^Sprint(\d+)_' -and (Test-Path (Join-Path $_.FullName 'plan.md')) } |
    ForEach-Object {
        $n = [int]($_.Name -replace '^Sprint(\d+)_.*$', '$1')
        [pscustomobject]@{
            Number   = $n
            Folder   = $_.Name
            PlanTime = (Get-Item (Join-Path $_.FullName 'plan.md')).LastWriteTime
            Reviewed = Test-Path (Join-Path $_.FullName 'seguridad.md')
        }
    })

# El sprint en curso no se revisa todavia. Si hay un plan suelto (el hook que lo
# mueve corre en paralelo), ese es el en curso y todos los de carpeta terminaron.
$hasLoose = @(Get-ChildItem -Path $plansDir -File -Filter '*.md').Count -gt 0
$current = if ($hasLoose) { $null } else { $sprints | Sort-Object PlanTime -Descending | Select-Object -First 1 }

$pending = @($sprints |
    Where-Object { -not $_.Reviewed -and ($exempt -notcontains $_.Number) -and (-not $current -or $_.Folder -ne $current.Folder) } |
    Sort-Object Number)

# Commit base de la proxima revision: el ultimo revisado (frontmatter "commit:" de
# los seguridad.md), o el de la linea base si aun no hay revisiones.
$reviewed = @($sprints | Where-Object Reviewed | ForEach-Object {
        $line = Get-Content (Join-Path $plansDir "$($_.Folder)\seguridad.md") -Encoding UTF8 |
            Where-Object { $_ -match '^commit:\s*(\S+)' } | Select-Object -First 1
        if ($line) { $line -replace '^commit:\s*(\S+).*$', '$1' }
    })
$base = $null
foreach ($c in $reviewed) {
    # Se queda con el mas reciente: el que no es ancestro de otro.
    if (-not $base) { $base = $c; continue }
    git -C $root merge-base --is-ancestor $base $c 2>$null
    if ($LASTEXITCODE -eq 0) { $base = $c }
}
if (-not $base -and $baseline) { $base = $baseline.baseCommit }

if ($List) {
    "base: $base"
    "en_curso: $(if ($current) { $current.Folder } else { '(plan recien aprobado)' })"
    if ($pending.Count -eq 0) { 'pendientes: (ninguno)' }
    else { 'pendientes: ' + (($pending | ForEach-Object Folder) -join ', ') }
    exit 0
}

if ($pending.Count -eq 0) { exit 0 }

$names = ($pending | ForEach-Object Folder) -join ', '
@{
    systemMessage      = "Sprints terminados sin revision de seguridad: $names"
    hookSpecificOutput = @{
        hookEventName     = 'PostToolUse'
        additionalContext = "Antes de implementar el plan aprobado, ejecuta la revision de seguridad de los sprints terminados ($names) siguiendo .claude/commands/cerrar-sprint.md (base $base). Al terminarla, informa los hallazgos al usuario y sigue con el plan."
    }
} | ConvertTo-Json -Compress
