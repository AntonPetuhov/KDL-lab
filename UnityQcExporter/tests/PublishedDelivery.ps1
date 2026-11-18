param([string]$Package = (Join-Path $PSScriptRoot '..\bin\Release\publish\win-x64'))
$ErrorActionPreference = 'Stop'
$root = Join-Path ([IO.Path]::GetTempPath()) ('unity-qc-check-' + [Guid]::NewGuid().ToString('N'))
$exe = Join-Path $root 'UnityQcExporter.exe'
$example = Join-Path $Package 'Examples\SysmexQC_20261116_1030.csv'
$child = $null

function Start-Exporter {
    Start-Process $exe -WorkingDirectory $root -WindowStyle Hidden -PassThru -RedirectStandardError (Join-Path $root 'stderr.txt') -RedirectStandardOutput (Join-Path $root 'stdout.txt')
}
function Wait-File([string]$name) {
    $limit = (Get-Date).AddSeconds(20)
    while (!(Test-Path -LiteralPath (Join-Path $root $name) -PathType Leaf)) {
        if ($child.HasExited -or (Get-Date) -gt $limit) {
            Get-Content (Join-Path $root 'stdout.txt') -Tail 45
            throw "Timeout: $name. $(Get-Content (Join-Path $root 'stderr.txt') -Raw)"
        }
        Start-Sleep -Milliseconds 150
    }
}
try {
    New-Item -ItemType Directory $root | Out-Null
    Copy-Item (Join-Path $Package 'UnityQcExporter.exe') $exe
    foreach ($id in @('A','B','Off')) {
        New-Item -ItemType Directory (Join-Path $root $id) | Out-Null
        Copy-Item $example (Join-Path $root "$id\same.csv")
    }
    $cfg = @{ PollSeconds = 1; UnityOutputFolder = 'out'; Sources = @(
        @{ Id='A'; Unity=$true; ReadyFolder='A' }
        @{ Id='B'; Unity=$true; ReadyFolder='B' }
        @{ Id='Off'; Unity=$false; ReadyFolder='Off' }
    ) }
    $cfg | ConvertTo-Json | Set-Content (Join-Path $root 'exporter.json') -Encoding utf8
    Copy-Item $example (Join-Path $root 'A\ignored.raw')
    # A regular file where the output directory belongs simulates an unavailable destination.
    New-Item -ItemType File (Join-Path $root 'out') | Out-Null
    $child = Start-Exporter
    Wait-File 'A\.unity-export\queue.lock'
    if (!(Test-Path (Join-Path $root 'A\same.csv'))) { throw 'Input lost during outage' }
    Remove-Item -LiteralPath (Join-Path $root 'out')
    foreach ($id in @('A','B')) {
        Wait-File "$id\.unity-export\Sent\same.csv"
        $target = Join-Path $root "out\$($id)__same.csv"
        if ((Get-FileHash $target).Hash -ne (Get-FileHash $example).Hash) { throw 'CSV bytes changed' }
        Remove-Item -LiteralPath $target
    }
    if (!(Test-Path (Join-Path $root 'A\ignored.raw')) -or !(Test-Path (Join-Path $root 'Off\same.csv'))) { throw 'Unexpected consumption' }
    $child.Kill(); $child.WaitForExit()
    # Restart after Unity consumed a file during the rename/receipt window.
    Copy-Item $example (Join-Path $root 'A\.unity-export\Publishing\uncertain.csv')
    Copy-Item $example (Join-Path $root 'B\new.csv')
    $child = Start-Exporter
    Wait-File 'B\.unity-export\Sent\new.csv'
    if ((Get-ChildItem (Join-Path $root 'out') -File).Count -ne 1 -or !(Test-Path (Join-Path $root 'A\.unity-export\Publishing\uncertain.csv'))) { throw 'Unexpected replay after restart' }
    'OK: two sources, byte integrity, disabled source, raw untouched, retry and restart.'
}
finally {
    if ($child -and !$child.HasExited) { $child.Kill(); $child.WaitForExit() }
    $expected = [IO.Path]::GetFullPath($root)
    $tempRoot = [IO.Path]::GetFullPath([IO.Path]::GetTempPath()).TrimEnd('\')
    if ($expected.StartsWith($tempRoot + '\unity-qc-check-', [StringComparison]::OrdinalIgnoreCase) -and (Test-Path -LiteralPath $expected)) {
        Remove-Item -LiteralPath $expected -Recurse -Force
    }
}
