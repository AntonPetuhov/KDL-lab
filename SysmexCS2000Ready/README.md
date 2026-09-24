# Analyzer Configuration Service — Sysmex CS-2000i

Готовое решение под .NET 9.0 состоит из Windows Service host, общего контракта, общего TCP-host и двух загружаемых DLL-драйверов Sysmex:

- `SysmexCS2000.Driver.dll` — ASTM E1381-02/E1394-97;
- `SysmexCS2000.HostOnline.Driver.dll` — собственный Sysmex Host Online с текстами R221, S221, D121/D221.

`AnalyzerService.Transport.dll` содержит общий `TcpHost`. Все приборы, которые подключаются как TCP-клиенты к серверу ЛИС, используют этот проект; драйверы содержат только прикладной протокол и обработку данных.

`AnalyzerService.LisDatabase.dll` содержит `LisDBProvider` для запросов к БД ЛИС и сопоставления кодов.

## Добавление нового TCP-анализатора

1. Создайте проект DLL под `net9.0`, реализующий `IAnalyzerDriver`.
2. Добавьте ссылки на `AnalyzerService.Contracts` и `AnalyzerService.Transport`.
3. В анализаторе создайте `TcpHost(logger, "Имя прибора")`, вызовите `Start` с адресом и портом из `AnalyzerSettings`, затем ожидайте клиентов через `AcceptAsync`.
4. Реализуйте в DLL только чтение кадров, кодек и прикладную обработку собственного протокола.
5. Опубликуйте DLL с зависимостями в отдельную подпапку `drivers` и укажите её путь в JSON.

Каждая активная конфигурация получает собственный экземпляр `TcpHost` и должна использовать уникальную пару IP-адрес/порт.

## Контроль качества и состояние TCP

Для Host Online сообщения D121/D221 с `Sample Distinction Code = C` сохраняются как `.res` и `.ok` в `<каталог службы>\<AnalyzerName>\<ResultsFolder>\QualityControl`. В файлах контроля сохраняются исходные коды и значения прибора; они не отправляются в обычный `OutputFolder` результатов пациентов. Поле `ResultsFolder` уже присутствует в JSON, новых полей не требуется. Сообщение DS21 содержит информацию о пробе и не создаёт файл результата.

Общий TCP-host пишет состояние в журнал `Transport` при запуске, подключении, завершении сеанса, остановке и каждые 30 секунд. Запись содержит состояние listener, endpoint, ожидание подключения, адрес клиента, время последних RX/TX и последнюю ошибку. Долгое отсутствие данных само по себе не доказывает отказ сокета или прибора.

Явные ACK/NAK для Host Online поверх TCP сейчас не отправляются и не ожидаются. Этот вопрос требует проверки настройки прибора и протокольного режима на анализаторе.

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
