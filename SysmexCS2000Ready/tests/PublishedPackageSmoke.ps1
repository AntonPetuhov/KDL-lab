$ErrorActionPreference = 'Stop'
$project = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path.TrimEnd('\')
$source = Join-Path $project 'publish\AnalyzerService'
$driverFiles = @(Get-ChildItem -LiteralPath (Join-Path $source 'drivers\SysmexCS2000HostOnline') -File -Recurse)
$expectedDriverFiles = @('SysmexCS2000.HostOnline.Driver.dll', 'SysmexCS2000.HostOnline.Driver.deps.json')
if (@($driverFiles | Where-Object { $_.Name -notin $expectedDriverFiles }).Count -ne 0 -or $driverFiles.Count -ne 2) {
    throw "Driver folder contains unexpected files: $($driverFiles.Name -join ', ')"
}
$target = Join-Path $PSScriptRoot 'smoke-driver-owned'
if (Test-Path -LiteralPath $target) { throw "Temporary folder already exists: $target" }
Copy-Item -LiteralPath $source -Destination $target -Recurse
$probe = [System.Net.Sockets.TcpListener]::new([System.Net.IPAddress]::Loopback, 0)
$probe.Start()
$port = ([System.Net.IPEndPoint]$probe.LocalEndpoint).Port
$probe.Stop()
$configPath = Join-Path $target 'configs\SysmexCS2000.json'
$config = Get-Content -LiteralPath $configPath -Raw | ConvertFrom-Json
$config.AnalyzerName = 'SmokeDriverOwned'
$config.IPaddress = '127.0.0.1'
$config.Port = $port
$config.ResultHandlerStatus = $false
$config | ConvertTo-Json -Depth 10 | Set-Content -LiteralPath $configPath -Encoding UTF8

$process = $null
try {
    $process = Start-Process -FilePath (Join-Path $target 'AnalyzerService.Host.exe') `
        -WorkingDirectory $target -WindowStyle Hidden -PassThru `
        -RedirectStandardOutput (Join-Path $target 'stdout.log') `
        -RedirectStandardError (Join-Path $target 'stderr.log')
    $client = $null
    for ($i = 0; $i -lt 50; $i++) {
        if ($process.HasExited) { throw "Host exited: $($process.ExitCode)" }
        try {
            $client = [System.Net.Sockets.TcpClient]::new()
            $client.Connect([System.Net.IPAddress]::Loopback, $port)
            break
        }
        catch {
            $client.Dispose()
            $client = $null
            Start-Sleep -Milliseconds 100
        }
    }
    if ($null -eq $client) { throw 'Driver did not open TCP listener.' }
    try {
        $body = 'D1210101C2610051200RACK0101' + 'CONTROL2'.PadLeft(15) + 'B' + 'QC'.PadRight(15) + '392  286 '
        $frame = [byte[]]@([byte]2) + [System.Text.Encoding]::ASCII.GetBytes($body) + [byte[]]@([byte]3)
        $client.GetStream().Write($frame, 0, $frame.Length)
    }
    finally { $client.Dispose() }
    $qc = Join-Path $target 'SmokeDriverOwned\Results\QualityControl'
    for ($i = 0; $i -lt 50 -and @(Get-ChildItem -LiteralPath $qc -Filter '*.raw' -ErrorAction SilentlyContinue).Count -eq 0; $i++) {
        Start-Sleep -Milliseconds 100
    }
    $files = @(Get-ChildItem -LiteralPath $qc -Filter '*.raw' -ErrorAction SilentlyContinue)
    if ($files.Count -ne 1) { throw "Expected one raw QC file, got $($files.Count)" }
    if ([Convert]::ToHexString([IO.File]::ReadAllBytes($files[0].FullName)) -ne [Convert]::ToHexString($frame)) {
        throw 'Raw QC bytes changed.'
    }
    Write-Host "Published host + driver-owned TCP passed on 127.0.0.1:$port."
}
finally {
    if ($null -ne $process -and -not $process.HasExited) { $process.Kill(); $process.WaitForExit() }
    $resolved = (Resolve-Path -LiteralPath $target).Path
    if (-not $resolved.StartsWith($project + '\', [StringComparison]::OrdinalIgnoreCase)) {
        throw "Unsafe cleanup path: $resolved"
    }
    Remove-Item -LiteralPath $resolved -Recurse -Force
}
