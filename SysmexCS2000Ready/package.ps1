param(
    [string]$Runtime = "win-x64"
)

$ErrorActionPreference = "Stop"
$PSNativeCommandUseErrorActionPreference = $true
$projectRoot = $PSScriptRoot
$publishRoot = Join-Path $projectRoot "publish"
$hostOutput = Join-Path $publishRoot "AnalyzerService"
$driverOutput = Join-Path $hostOutput "drivers\SysmexCS2000"
$hostOnlineOutput = Join-Path $hostOutput "drivers\SysmexCS2000HostOnline"

if (Test-Path -LiteralPath $publishRoot) {
    $resolvedProject = (Resolve-Path -LiteralPath $projectRoot).Path.TrimEnd('\')
    $resolvedPublish = (Resolve-Path -LiteralPath $publishRoot).Path
    if (-not $resolvedPublish.StartsWith($resolvedProject + '\', [System.StringComparison]::OrdinalIgnoreCase)) {
        throw "Небезопасный путь публикации: $resolvedPublish"
    }
    Remove-Item -LiteralPath $publishRoot -Recurse -Force
}

dotnet publish (Join-Path $projectRoot "src\AnalyzerService.Host\AnalyzerService.Host.csproj") -c Release -r $Runtime --self-contained false -o $hostOutput --disable-build-servers --maxcpucount:1
dotnet publish (Join-Path $projectRoot "src\SysmexCS2000.Driver\SysmexCS2000.Driver.csproj") -c Release -r $Runtime --self-contained false -o $driverOutput --disable-build-servers --maxcpucount:1
dotnet publish (Join-Path $projectRoot "src\SysmexCS2000.HostOnline.Driver\SysmexCS2000.HostOnline.Driver.csproj") -c Release -r $Runtime --self-contained false -o $hostOnlineOutput --disable-build-servers --maxcpucount:1
Copy-Item -LiteralPath (Join-Path $projectRoot "configs\SysmexCS2000i.json") -Destination (Join-Path $hostOutput "configs\SysmexCS2000i.json") -Force

Write-Host "Готовый каталог службы: $hostOutput"
