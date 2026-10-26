param(
    [string]$ReferenceRoot = (Join-Path $PSScriptRoot '..\..\SysmexCS2000Ready')
)
$ErrorActionPreference = 'Stop'
$projectRoot = (Resolve-Path -LiteralPath (Join-Path $PSScriptRoot '..')).Path
$manifest = @(Get-Content -LiteralPath (Join-Path $PSScriptRoot 'shared-components.sha256.json') -Raw | ConvertFrom-Json)
$actual = @()
foreach ($component in @('Host', 'Contracts', 'Transport', 'LisDatabase', 'ResultFiles')) {
    $directory = Join-Path $projectRoot ('src\AnalyzerService.' + $component)
    $actual += @(Get-ChildItem -LiteralPath $directory -File -Recurse | Where-Object { $_.FullName -notmatch '\\(bin|obj)\\' })
}
if ($actual.Count -ne $manifest.Count) { throw 'Shared component file count changed.' }
foreach ($entry in $manifest) {
    $target = Join-Path $projectRoot $entry.Path
    if ((Get-FileHash -LiteralPath $target -Algorithm SHA256).Hash -ne $entry.SHA256) {
        throw "Shared component changed: $($entry.Path)"
    }
    if (Test-Path -LiteralPath $ReferenceRoot) {
        $reference = Join-Path $ReferenceRoot $entry.Path
        if ((Get-FileHash -LiteralPath $reference -Algorithm SHA256).Hash -ne $entry.SHA256) {
            throw "Sysmex baseline changed: $($entry.Path)"
        }
    }
}
Write-Host "Shared components unchanged: $($manifest.Count) files verified by SHA-256."
