param(
    [string]$Runtime = "win-x64"
)

$ErrorActionPreference = "Stop"
$PSNativeCommandUseErrorActionPreference = $true
$projectRoot = $PSScriptRoot
$publishRoot = Join-Path $projectRoot "publish"
$hostOutput = Join-Path $publishRoot "AnalyzerService"
$hostOnlineOutput = Join-Path $hostOutput "drivers\SysmexCS2000HostOnline"
$driverStaging = Join-Path $publishRoot ".driver-staging"

if (Test-Path -LiteralPath $publishRoot) {
    $resolvedProject = (Resolve-Path -LiteralPath $projectRoot).Path.TrimEnd('\')
    $resolvedPublish = (Resolve-Path -LiteralPath $publishRoot).Path
    if (-not $resolvedPublish.StartsWith($resolvedProject + '\', [System.StringComparison]::OrdinalIgnoreCase)) {
        throw "Небезопасный путь публикации: $resolvedPublish"
    }
    Remove-Item -LiteralPath $publishRoot -Recurse -Force
}

dotnet publish (Join-Path $projectRoot "src\AnalyzerService.Host\AnalyzerService.Host.csproj") -c Release -r $Runtime --self-contained false -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -p:PublishTrimmed=false -o $hostOutput --disable-build-servers --maxcpucount:1
if ($LASTEXITCODE -ne 0) { throw "Ошибка публикации службы: $LASTEXITCODE" }
# Все общие библиотеки включены в однофайловый Host. Каталог драйвера
# получает только собственную DLL и манифест зависимостей для resolver.
dotnet publish (Join-Path $projectRoot "src\SysmexCS2000.HostOnline.Driver\SysmexCS2000.HostOnline.Driver.csproj") -c Release -r $Runtime --self-contained false -o $driverStaging --disable-build-servers --maxcpucount:1
if ($LASTEXITCODE -ne 0) { throw "Ошибка публикации драйвера: $LASTEXITCODE" }
New-Item -ItemType Directory -Path $hostOnlineOutput -Force | Out-Null
foreach ($fileName in @('SysmexCS2000.HostOnline.Driver.dll', 'SysmexCS2000.HostOnline.Driver.deps.json')) {
    $source = Join-Path $driverStaging $fileName
    if (-not (Test-Path -LiteralPath $source)) { throw "Файл драйвера не найден: $source" }
    Copy-Item -LiteralPath $source -Destination (Join-Path $hostOnlineOutput $fileName)
}
$resolvedProject = (Resolve-Path -LiteralPath $projectRoot).Path.TrimEnd('\')
$resolvedStaging = (Resolve-Path -LiteralPath $driverStaging).Path
if (-not $resolvedStaging.StartsWith($resolvedProject + '\', [System.StringComparison]::OrdinalIgnoreCase)) {
    throw "Небезопасный путь временного каталога: $resolvedStaging"
}
Remove-Item -LiteralPath $resolvedStaging -Recurse -Force
Copy-Item -LiteralPath (Join-Path $projectRoot "configs\SysmexCS2000.json") -Destination (Join-Path $hostOutput "configs\SysmexCS2000.json") -Force

Write-Host "Готовый каталог службы: $hostOutput"
