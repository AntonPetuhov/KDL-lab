# Analyzer Configuration Service — Sysmex CS-2000i

Готовое решение под .NET 9.0 состоит из Windows Service host, общего контракта и двух загружаемых DLL-драйверов Sysmex:

- `SysmexCS2000.Driver.dll` — ASTM E1381-02/E1394-97;
- `SysmexCS2000.HostOnline.Driver.dll` — собственный Sysmex Host Online с текстами R221, S221, D121/D221.

## Сборка и проверка

```powershell
dotnet build .\AnalyzerService.sln --maxcpucount:1
dotnet run --project .\tests\SysmexCS2000.Driver.Tests
dotnet run --project .\tests\SysmexCS2000.HostOnline.Driver.Tests
.\package.ps1
```

`package.ps1` создаёт `publish\AnalyzerService`, включая обе DLL и их зависимости в отдельных подпапках `drivers`.

## Настройка

Отредактируйте `configs\SysmexCS2000i.json`. `IPaddress` — адрес локального интерфейса сервера ЛИС. IPU анализатора должен подключаться к этому адресу и порту. `Protocol` добавлен как документированное расширение исходного JSON, потому что Sysmex поддерживает несколько протоколов поверх TCP/IP.

Для собственного протокола используйте пример `configs\examples\SysmexCS2000i.HostOnline.json`, перенесите его в `configs` и удалите либо отключите конфликтующую конфигурацию ASTM на том же адресе и порту.

## Запуск в консоли

Запустите `AnalyzerService.Host.exe` из опубликованного каталога. Перед установкой службы это позволяет проверить доступность IP-адреса, порта, БД и выходной папки.

## Установка Windows Service

Из PowerShell с правами администратора:

```powershell
sc.exe create AnalyzerConfigurationService binPath= "C:\Services\AnalyzerService\AnalyzerService.Host.exe" start= auto
sc.exe start AnalyzerConfigurationService
```

Служебные журналы создаются в `<каталог службы>\Sysmex cs-2000i\Logs`.

## Ограничение

PDF описывает сетевой протокол, но не API нативной фирменной DLL Sysmex. Поэтому DLL в этом решении является управляемым подключаемым драйвером `IAnalyzerDriver`; вызовы несуществующего vendor API не выдумываются.
