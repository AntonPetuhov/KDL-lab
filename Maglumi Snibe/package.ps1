param(
    [string]$Runtime = "win-x64",
    [switch]$NoRestore
)

$ErrorActionPreference = "Stop"
$PSNativeCommandUseErrorActionPreference = $true
$projectRoot = $PSScriptRoot
$publishRoot = Join-Path $projectRoot "publish"
$hostOutput = Join-Path $publishRoot "AnalyzerService"
$hostOnlineOutput = Join-Path $hostOutput "drivers\MaglumiSnibe"
$driverStaging = Join-Path $publishRoot ".driver-staging"
$restoreOptions = @()
if ($NoRestore) { $restoreOptions = @('--no-restore') }

if (Test-Path -LiteralPath $publishRoot) {
    $resolvedProject = (Resolve-Path -LiteralPath $projectRoot).Path.TrimEnd('\')
    $resolvedPublish = (Resolve-Path -LiteralPath $publishRoot).Path
    if (-not $resolvedPublish.StartsWith($resolvedProject + '\', [System.StringComparison]::OrdinalIgnoreCase)) {
        throw "Небезопасный путь публикации: $resolvedPublish"
    }
    Remove-Item -LiteralPath $publishRoot -Recurse -Force
}

dotnet publish (Join-Path $projectRoot "src\AnalyzerService.Host\AnalyzerService.Host.csproj") -c Release -r $Runtime --self-contained false -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -p:PublishTrimmed=false -o $hostOutput --disable-build-servers --maxcpucount:1 @restoreOptions
if ($LASTEXITCODE -ne 0) { throw "Ошибка публикации службы: $LASTEXITCODE" }
# Все общие библиотеки включены в однофайловый Host. Каталог драйвера
# получает только собственную DLL и манифест зависимостей для resolver.
dotnet publish (Join-Path $projectRoot "src\Maglumi.Snibe.Driver\Maglumi.Snibe.Driver.csproj") -c Release -r $Runtime --self-contained false -o $driverStaging --disable-build-servers --maxcpucount:1 @restoreOptions
if ($LASTEXITCODE -ne 0) { throw "Ошибка публикации драйвера: $LASTEXITCODE" }
New-Item -ItemType Directory -Path $hostOnlineOutput -Force | Out-Null
foreach ($fileName in @('Maglumi.Snibe.Driver.dll', 'Maglumi.Snibe.Driver.deps.json')) {
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
Copy-Item -LiteralPath (Join-Path $projectRoot "configs\MaglumiSnibe.json") -Destination (Join-Path $hostOutput "configs\MaglumiSnibe.json") -Force

Write-Host "Готовый каталог службы: $hostOutput"
