param(
    [string]$Runtime = "win-x64"
)

$ErrorActionPreference = "Stop"
$PSNativeCommandUseErrorActionPreference = $true
$projectRoot = $PSScriptRoot
$publishRoot = Join-Path $projectRoot "publish"
$hostOutput = Join-Path $publishRoot "AnalyzerService"
$hostOnlineOutput = Join-Path $hostOutput "drivers\SysmexCS2000HostOnline"

if (Test-Path -LiteralPath $publishRoot) {
    $resolvedProject = (Resolve-Path -LiteralPath $projectRoot).Path.TrimEnd('\')
    $resolvedPublish = (Resolve-Path -LiteralPath $publishRoot).Path
    if (-not $resolvedPublish.StartsWith($resolvedProject + '\', [System.StringComparison]::OrdinalIgnoreCase)) {
        throw "Небезопасный путь публикации: $resolvedPublish"
    }
    Remove-Item -LiteralPath $publishRoot -Recurse -Force
}

dotnet publish (Join-Path $projectRoot "src\AnalyzerService.Host\AnalyzerService.Host.csproj") -c Release -r $Runtime --self-contained false -p:PublishSingleFile=true -p:PublishTrimmed=false -o $hostOutput --disable-build-servers --maxcpucount:1
if ($LASTEXITCODE -ne 0) { throw "Ошибка публикации службы: $LASTEXITCODE" }
# Общие библиотеки и их зависимости публикуются службой; ссылки драйвера исключают runtime-активы.
dotnet publish (Join-Path $projectRoot "src\SysmexCS2000.HostOnline.Driver\SysmexCS2000.HostOnline.Driver.csproj") -c Release -r $Runtime --self-contained false -o $hostOnlineOutput --disable-build-servers --maxcpucount:1
if ($LASTEXITCODE -ne 0) { throw "Ошибка публикации драйвера: $LASTEXITCODE" }
Copy-Item -LiteralPath (Join-Path $projectRoot "configs\SysmexCS2000.json") -Destination (Join-Path $hostOutput "configs\SysmexCS2000.json") -Force

Write-Host "Готовый каталог службы: $hostOutput"
