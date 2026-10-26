$ErrorActionPreference = 'Stop'
$project = (Resolve-Path -LiteralPath (Join-Path $PSScriptRoot '..')).Path.TrimEnd('\')
$source = Join-Path $project 'publish\AnalyzerService'
$driverFiles = @(Get-ChildItem -LiteralPath (Join-Path $source 'drivers\MaglumiSnibe') -File -Recurse)
$expected = @('Maglumi.Snibe.Driver.dll', 'Maglumi.Snibe.Driver.deps.json')
if ($driverFiles.Count -ne 2 -or @($driverFiles | Where-Object { $_.Name -notin $expected }).Count -ne 0) {
    throw 'Unexpected driver package contents.'
}
$target = Join-Path $PSScriptRoot ('smoke-x8-' + [Guid]::NewGuid().ToString('N'))
Copy-Item -LiteralPath $source -Destination $target -Recurse
$probe = [Net.Sockets.TcpListener]::new([Net.IPAddress]::Loopback, 0)
$probe.Start()
$port = ([Net.IPEndPoint]$probe.LocalEndpoint).Port
$probe.Stop()
$configPath = Join-Path $target 'configs\MaglumiSnibe.json'
$config = Get-Content -LiteralPath $configPath -Raw | ConvertFrom-Json
$config.AnalyzerName = 'SmokeMaglumiX8'
$config.IPaddress = '127.0.0.1'
$config.Port = $port
$config.ActiveStatus = $true
$config.ResultHandlerStatus = $false
$config | ConvertTo-Json -Depth 10 | Set-Content -LiteralPath $configPath -Encoding utf8
$process = $null
$client = $null
try {
    $process = Start-Process -FilePath (Join-Path $target 'AnalyzerService.Host.exe') `
        -WorkingDirectory $target -WindowStyle Hidden -PassThru `
        -RedirectStandardOutput (Join-Path $target 'stdout.log') `
        -RedirectStandardError (Join-Path $target 'stderr.log')
    for ($i = 0; $i -lt 100; $i++) {
        if ($process.HasExited) { throw "Published Host exited: $($process.ExitCode). $(Get-Content (Join-Path $target 'stderr.log') -Raw)" }
        try {
            $client = [Net.Sockets.TcpClient]::new()
            $client.Connect([Net.IPAddress]::Loopback, $port)
            break
        }
        catch { $client.Dispose(); $client = $null; Start-Sleep -Milliseconds 100 }
    }
    if ($null -eq $client) { throw 'MAGLUMI DLL did not open TCP listener.' }
    $header = 'MSH|^~\&|X8||LIS||20191025105016||OUL^R22|201910251050161|P|2.5|||NE|NE||UTF-8' + "`r"
    $observation = 'OBR|1|||FT3' + "`r" + 'OBX|1||FT3||3.5600^|pg/mL|||||F|||||||Module1^X8|20191021154122' + "`r" + 'NTE|1' + "`r"
    $patient = $header + 'SPM|1|20251122553^1556^^N0002^2||1|||||||P' + "`r" + $observation
    $qc = $header + 'SPM|1|Bole^65318^1001^N0002^2|||||||||Q' + "`r" + $observation
    $patientFrame = [byte[]]@(11) + [Text.Encoding]::UTF8.GetBytes($patient) + [byte[]]@(28,13)
    $qcFrame = [byte[]]@(11) + [Text.Encoding]::UTF8.GetBytes($qc) + [byte[]]@(28,13)
    # Фрагментация первого кадра и несколько кадров в одном TCP-сеансе.
    $stream = $client.GetStream()
    $stream.Write($patientFrame, 0, 5)
    $stream.Write($patientFrame, 5, $patientFrame.Length - 5)
    $stream.Write($qcFrame, 0, $qcFrame.Length)
    $root = Join-Path $target 'SmokeMaglumiX8\Results'
    $qcRoot = Join-Path $root 'QualityControl'
    for ($i = 0; $i -lt 100; $i++) {
        $patients = @(Get-ChildItem -LiteralPath $root -Filter '*.raw' -ErrorAction SilentlyContinue)
        $controls = @(Get-ChildItem -LiteralPath $qcRoot -Filter '*.raw' -ErrorAction SilentlyContinue)
        if ($patients.Count -eq 1 -and $controls.Count -eq 1) { break }
        Start-Sleep -Milliseconds 100
    }
    if ($patients.Count -ne 1 -or $controls.Count -ne 1) { throw 'Expected one patient .raw and one QC .raw.' }
    if ([Convert]::ToHexString([IO.File]::ReadAllBytes($patients[0].FullName)) -ne [Convert]::ToHexString($patientFrame)) { throw 'Patient bytes changed.' }
    if ([Convert]::ToHexString([IO.File]::ReadAllBytes($controls[0].FullName)) -ne [Convert]::ToHexString($qcFrame)) { throw 'QC bytes changed.' }
    if ($client.Available -ne 0) { throw 'X8 OUL^R22 must not receive an unsolicited ACK.' }
    Write-Host "Published Host + MAGLUMI DLL smoke passed on 127.0.0.1:$port (patient/QC exact bytes)."
}
finally {
    if ($null -ne $client) { $client.Dispose() }
    if ($null -ne $process -and -not $process.HasExited) { $process.Kill(); $process.WaitForExit() }
    $resolved = (Resolve-Path -LiteralPath $target).Path
    if (-not $resolved.StartsWith($project + '\', [StringComparison]::OrdinalIgnoreCase)) { throw "Unsafe cleanup: $resolved" }
    Remove-Item -LiteralPath $resolved -Recurse -Force
}
