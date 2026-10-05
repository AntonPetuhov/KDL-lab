"""Create the transport-neutral AnalyzerService architecture guide."""

from pathlib import Path
from xml.sax.saxutils import escape
from reportlab.lib import colors
from reportlab.lib.pagesizes import A4
from reportlab.lib.styles import ParagraphStyle, getSampleStyleSheet
from reportlab.lib.units import mm
from reportlab.pdfbase import pdfmetrics
from reportlab.pdfbase.ttfonts import TTFont
from reportlab.platypus import BaseDocTemplate, Frame, PageTemplate, Paragraph, Spacer, Table, TableStyle, PageBreak

ROOT = Path(__file__).resolve().parents[1]
OUT = ROOT / "output" / "pdf" / "AnalyzerService_driver_lifecycle_guide.pdf"
OUT.parent.mkdir(parents=True, exist_ok=True)

pdfmetrics.registerFont(TTFont("GuideArial", r"C:\Windows\Fonts\arial.ttf"))
pdfmetrics.registerFont(TTFont("GuideArialBold", r"C:\Windows\Fonts\arialbd.ttf"))
pdfmetrics.registerFontFamily("GuideArial", normal="GuideArial", bold="GuideArialBold")

NAVY = colors.HexColor("#17324D")
BLUE = colors.HexColor("#216A84")
PALE = colors.HexColor("#EEF5F8")
LINE = colors.HexColor("#D2E1E8")
MUTED = colors.HexColor("#526575")

styles = getSampleStyleSheet()
styles.add(ParagraphStyle(name="TitleX", fontName="GuideArialBold", fontSize=19, leading=23,
                          textColor=NAVY, spaceAfter=8))
styles.add(ParagraphStyle(name="LeadX", fontName="GuideArial", fontSize=9.5, leading=14,
                          textColor=MUTED, spaceAfter=10))
styles.add(ParagraphStyle(name="H1X", fontName="GuideArialBold", fontSize=12.2, leading=15.4,
                          textColor=NAVY, spaceBefore=10, spaceAfter=5, keepWithNext=True))
styles.add(ParagraphStyle(name="H2X", fontName="GuideArialBold", fontSize=9.6, leading=12.5,
                          textColor=BLUE, spaceBefore=7, spaceAfter=3, keepWithNext=True))
styles.add(ParagraphStyle(name="BodyX", fontName="GuideArial", fontSize=8.55, leading=12.4,
                          spaceAfter=5))
styles.add(ParagraphStyle(name="SmallX", fontName="GuideArial", fontSize=7.65, leading=11,
                          spaceAfter=3))
styles.add(ParagraphStyle(name="MonoX", fontName="GuideArial", fontSize=7.8, leading=11.2,
                          spaceAfter=2))
styles.add(ParagraphStyle(name="TableHeadX", fontName="GuideArialBold", fontSize=7.7,
                          leading=10.8, textColor=colors.white))

story = []


def p(value, style="BodyX"):
    return Paragraph(value, styles[style])


def body(value):
    story.append(p(value))


def h1(value):
    story.append(p(value, "H1X"))


def h2(value):
    story.append(p(value, "H2X"))


def step(number, value):
    body(f"<b>{number}.</b> {value}")


def panel(lines):
    table = Table([[p(escape(line), "MonoX")] for line in lines],
                  colWidths=[174 * mm], hAlign="LEFT")
    table.setStyle(TableStyle([
        ("BACKGROUND", (0, 0), (-1, -1), PALE),
        ("BOX", (0, 0), (-1, -1), .5, LINE),
        ("LEFTPADDING", (0, 0), (-1, -1), 8),
        ("RIGHTPADDING", (0, 0), (-1, -1), 8),
        ("TOPPADDING", (0, 0), (-1, -1), 2.7),
        ("BOTTOMPADDING", (0, 0), (-1, -1), 2.7),
    ]))
    story.extend([table, Spacer(1, 4)])


def matrix(rows, widths):
    table = Table([[p(cell, "TableHeadX" if i == 0 else "SmallX") for cell in row]
                   for i, row in enumerate(rows)], colWidths=widths, repeatRows=1, hAlign="LEFT")
    table.setStyle(TableStyle([
        ("BACKGROUND", (0, 0), (-1, 0), NAVY),
        ("ROWBACKGROUNDS", (0, 1), (-1, -1), [colors.white, PALE]),
        ("GRID", (0, 0), (-1, -1), .35, LINE),
        ("VALIGN", (0, 0), (-1, -1), "TOP"),
        ("LEFTPADDING", (0, 0), (-1, -1), 5),
        ("RIGHTPADDING", (0, 0), (-1, -1), 5),
        ("TOPPADDING", (0, 0), (-1, -1), 4),
        ("BOTTOMPADDING", (0, 0), (-1, -1), 4),
    ]))
    story.extend([table, Spacer(1, 5)])


story.append(p("AnalyzerService: независимый от транспорта хост", "TitleX"))
story.append(p("Техническое руководство по версии проекта от 05.10.2026. Жизненный цикл DLL, "
               "последовательность вызовов, работа Sysmex CS-2000i, остановка, исключения, "
               "расширение и публикация для Windows Service.", "LeadX"))

h1("1. Главный принцип архитектуры")
body("Служба не решает, каким способом общается прибор. Она читает конфигурацию, создаёт "
     "файловый логгер, загружает управляемую DLL, передаёт ей настройки и командует работать. "
     "DLL владеет своим соединением, протоколом и рабочими потоками. Текущий Sysmex-драйвер "
     "выбирает TCP; другой плагин может читать COM-порт или папку обмена без изменения Host.")
panel([
    "Windows Service / Worker -> AnalyzerManager -> AnalyzerRuntime",
    "AnalyzerRuntime -> IAnalyzerDriver: Initialize -> RunAsync -> Stop -> Dispose",
    "Sysmex DLL -> AnalyzerService.Transport.TcpHost -> TCP client / Host Online",
    "Sysmex DLL -> LisDBProvider + RawResultQueue -> SQL / .raw / .res / .ok",
    "Будущий COM/File DLL -> собственный порт или каталог; тот же IAnalyzerDriver",
])
matrix([
    ["Проект", "Ответственность", "Что он не знает"],
    ["AnalyzerService.Host", "Windows Service, JSON, логирование, загрузка DLL, жизненный цикл", "TCP, COM, Host Online, SQL-формат прибора"],
    ["AnalyzerService.Contracts", "IAnalyzerDriver, IAnalyzerLogger, AnalyzerSettings", "Конкретный транспорт"],
    ["SysmexCS2000.HostOnline.Driver", "TCP-сеансы, R221/S221/D121/D221, очередь и ЛИС", "Жизненный цикл Windows Service"],
    ["AnalyzerService.Transport", "Повторно используемый TCP listener, сеанс и статус сокета", "Sysmex Host Online и Windows Service"],
    ["LisDatabase / ResultFiles", "Запросы ЛИС / накопление и обработка сырых файлов", "Оркестрация всех приборов"],
], [43 * mm, 78 * mm, 53 * mm])

h1("2. Контракт DLL и смысл методов")
matrix([
    ["Вызов", "Кто вызывает", "Ожидаемое поведение"],
    ["Initialize(logger, settings)", "AnalyzerRuntime", "Синхронная проверка собственных полей JSON, создание объектов; I/O не ожидается."],
    ["RunAsync(token)", "AnalyzerRuntime", "Драйвер запускает ресурсы и сразу возвращает Task всего рабочего цикла; Task завершается при остановке или ошибке."],
    ["Stop()", "AnalyzerRuntime", "Синхронный сигнал: закрыть/разблокировать ожидание. Длительное ожидание здесь недопустимо."],
    ["Dispose()", "LoadedDriver", "Освободить ресурсы; затем AssemblyLoadContext помечается к выгрузке."],
], [46 * mm, 34 * mm, 94 * mm])
body("RunAsync возвращает Task потому, что сеть или иной внешний источник могут долго ждать событие. "
     "Хост не вызывает Task.Run вокруг драйвера: плагин сам выбирает эффективную модель. "
     "Для синхронного COM API драйвер может организовать отдельный поток и закрыть порт в Stop; "
     "для файлового обмена - наблюдатель каталога. SQL-запросы и короткие записи .raw в текущем "
     "Sysmex-драйвере остаются синхронными. Рабочая задача нужна хосту также для наблюдения ошибок.")

h1("3. Запуск: точная последовательность вызовов")
step(1, "Program создаёт Generic Host с Windows Service; Worker.ExecuteAsync ищет JSON только "
     "в каталоге configs рядом с EXE и читает каждый верхнеуровневый файл через "
     "JsonAnalyzerSettingsProvider.LoadAll.")
step(2, "AnalyzerSettingsValidator.ValidateAndThrow проверяет только поля хоста: AnalyzerName, "
     "Isdll и DllPath. Он не отвергает File/Serial и не проверяет IP/порт. "
     "Активные конфигурации передаются AnalyzerManager.Add; повторное имя запрещено.")
step(3, "AnalyzerManager.StartAllAsync последовательно вызывает AnalyzerRuntime.StartAsync "
     "для каждого активного прибора. Runtime создаёт отдельный файловый логгер и связанную отмену.")
step(4, "DriverLoader.Load разрешает DllPath относительно каталога EXE, создаёт "
     "DriverLoadContext и через reflection экземпляр IAnalyzerDriver. Только Contracts "
     "разделяется с Host; прочие зависимости ищутся рядом с DLL.")
step(5, "Runtime вызывает DLL.Initialize(logger, settings), затем DLL.RunAsync(token), "
     "сохраняет возвращённый Task как Completion и немедленно возвращается менеджеру. "
     "Sysmex-драйвер в Initialize проверяет Protocol/TCP/IP/порт/папки/БД.")
step(6, "Sysmex.RunAsync запускает RawResultQueue, создаёт TcpHost и синхронно открывает "
     "listener. Затем RunConnectionsAsync асинхронно ждёт подключения. Manager ожидает "
     "Task.WhenAll(Completion): все приборы могут работать одновременно.")
panel([
    "Worker.ExecuteAsync",
    "  -> JsonAnalyzerSettingsProvider.LoadAll -> AnalyzerSettingsValidator",
    "  -> AnalyzerManager.Add -> AnalyzerManager.StartAllAsync",
    "  -> AnalyzerRuntime.StartAsync -> DriverLoader.Load",
    "  -> IAnalyzerDriver.Initialize -> IAnalyzerDriver.RunAsync",
    "  -> Sysmex.RunConnectionsAsync -> TcpHost.AcceptAsync",
])

h1("4. Пример рабочего стека: один запрос R221")
step(1, "TcpHost.AcceptAsync принимает TCP-клиента и сохраняет удалённый адрес; "
     "Sysmex.RunConnectionsAsync передаёт поток в HandleConnectionAsync.")
step(2, "ReadTextAsync ожидает STX, затем не более 253 ASCII-байт тела и ETX. "
     "В Protocol фиксируется точный RX; TcpHost.RecordRead обновляет состояние сокета. "
     "AddBlock объединяет многоблочный текст, если он получен.")
step(3, "HostOnlineCodec.ParseInquiry выделяет Sample ID, штатив, позицию и коды прибора. "
     "LisDBProvider.GetOrder синхронно читает пациента и незавершённые исследования из SQL; "
     "по каждому кандидату пишет причину включения или исключения.")
step(4, "BuildOrder преобразует код результата, например 392, в код группы задания 390, "
     "формирует S221 с полем пациента до 15 ASCII-символов. Число блоков ограничено "
     "22 группами на текст. WriteTextAsync отправляет STX/S221/ETX и пишет точный TX.")
body("В этом пути AnalyzerRuntime вообще не вызывается между Accept и TX: он только наблюдает "
     "Task рабочего цикла. Документированный формат R221/S221 взят из "
     "2_5197664667366890337.pdf. ACK/NAK здесь не добавлены: этот вопрос был отложен "
     "до отдельной проверки с прибором.")

h1("5. Пример рабочего стека: D121 и результат ЛИС")
step(1, "HandleConnectionAsync получает D121/D221. Точный RX, тип сообщения, штатив, "
     "позиция, образец, коды, значения и флаги записываются в Protocol. Для контроля "
     "качества исходные байты сохраняются в Results/QualityControl без преобразования.")
step(2, "Для пациента RawResultQueue.SaveResult сначала атомарно пишет исходные STX/ETX-байты "
     "в отдельный .raw. После этого TCP-приёмник может читать следующее сообщение, "
     "не ожидая SQL и построения файлов ЛИС.")
step(3, "Выделенный поток RawResultQueue подбирает готовые .raw, вызывает "
     "ProcessStoredResult -> HostOnlineCodec.ParseResult -> HostOnlineResultHandler.Handle. "
     "Кодек восстанавливает десятичный масштаб по таблице кодов PDF; обработчик "
     "округляет числовой выход до одного знака и записывает запятую.")
step(4, "LisDBProvider.TranslateResultCode ищет код PSMV2. После успешной записи .res и .ok "
     "исходный .raw переносится в Results/archive; пустое сообщение, отсутствие "
     "сопоставленных тестов или ошибка - в Results/errors с записью в Error.")
body("Пример: сырое 392  286 означает 28.6 и в выходном файле ЛИС записывается 28,6. "
     "Код 39100781 соответствует 0.781 до округления. Масштабы кодов взяты из таблицы "
     "2_5197664667366890338.pdf, стр. 47/53; реальные Assay Group Settings прибора "
     "необходимо поддерживать согласованными с этой таблицей.")

story.append(PageBreak())
h1("6. Остановка и освобождение ресурсов")
step(1, "Service Control Manager инициирует Worker.StopAsync; тот вызывает "
     "AnalyzerManager.StopAllAsync. Менеджер обходит все драйверы и собирает ошибки "
     "каждого, не оставляя остальные без команды остановки.")
step(2, "AnalyzerRuntime.StopAsync отменяет token, вызывает DLL.Stop и затем ожидает "
     "сохранённый Completion с ограничением cancellationToken. Хост не знает, какой "
     "порт, сокет или наблюдатель каталога остановил плагин.")
step(3, "В Sysmex Stop закрывает TcpHost: AcceptAsync и активное чтение клиента "
     "разблокируются. RunConnectionsAsync выходит и в finally останавливает поток "
     "RawResultQueue после текущего файла. Необработанные .raw остаются до нового старта.")
step(4, "AnalyzerManager вызывает AnalyzerRuntime.Dispose, тот освобождает LoadedDriver; "
     "DLL.Dispose закрывает остаточные ресурсы, DriverLoadContext.Unload помечает "
     "изолированную сборку к выгрузке. Затем освобождаются токены Runtime.")
body("Если RunAsync завершился с ошибкой, Worker получает её через Completion, пишет "
     "файловый Error-журнал и передаёт Generic Host. Ошибки Stop, ожидания и Dispose "
     "тоже логируются; несколько ошибок агрегируются в AggregateException. "
     "Штатная отмена рабочей задачи при остановке ошибкой не считается; отмена "
     "лимита ожидания Stop фиксируется как ошибка.")

h1("7. Конфигурация и размещение DLL")
body("Структура configs/SysmexCS2000.json сохранена. Хост читает общий набор полей, "
     "но интерпретирует лишь имя, активность и путь DLL. Поля ConnectionType, IPaddress, "
     "Port, Protocol, ConnectionString, ResultsFolder и OutputFolder принадлежат "
     "конкретному драйверу. Неактивный JSON в configs/examples не запускается, так как "
     "LoadAll просматривает только верхний уровень configs.")
panel([
    "publish/AnalyzerService/AnalyzerService.Host.exe       один исполняемый Host",
    "publish/AnalyzerService/configs/SysmexCS2000.json",
    "publish/AnalyzerService/drivers/SysmexCS2000HostOnline/",
    "  SysmexCS2000.HostOnline.Driver.dll + .deps.json",
    "  AnalyzerService.Transport.dll + LisDatabase.dll + ResultFiles.dll",
    "  Microsoft.Data.SqlClient.dll и остальные зависимости публикации",
])
body("Только AnalyzerService.Contracts является общей сборкой с Host. Для нового драйвера "
     "создаётся своя подпапка drivers, туда помещаются его DLL, .deps.json и частные "
     "зависимости. DllPath указывает на главную DLL. Если драйвер использует TCP, он может "
     "сослаться на AnalyzerService.Transport; COM/File-драйверу это не требуется. "
     "Никаких новых полей JSON для этого рефакторинга не добавлено.")

h1("8. Сборка и проверки")
step(1, "Откройте AnalyzerService.sln в Visual Studio 2022 с .NET 9 SDK; выполните "
     "Restore NuGet и Build Solution. Для готовой публикации запустите package.ps1 "
     "из корня SysmexCS2000Ready.")
step(2, "Скрипт публикует framework-dependent однофайловый Host для win-x64; "
     "на машине службы нужен .NET 9 Runtime. DLL-плагины остаются внешними для "
     "динамической загрузки. Копируйте весь каталог publish/AnalyzerService, "
     "не только EXE или главную DLL.")
step(3, "Проверьте IPaddress для Sysmex: это должен быть локальный адрес сервера. "
     "Учётной записи службы нужны права чтения configs/drivers, записи в "
     "ResultsFolder, OutputFolder и Logs, а также доступ к SQL Server.")
panel([
    "dotnet restore AnalyzerService.sln",
    "dotnet build AnalyzerService.sln --no-restore --maxcpucount:1",
    "dotnet run --project tests/SysmexCS2000.HostOnline.Driver.Tests --no-build",
    "./package.ps1",
    "./tests/PublishedPackageSmoke.ps1   # loopback, без реальной БД/прибора",
])
body("Тесты проверяют в том числе DLL файлового обмена без IP/порта, отсутствие "
     "ссылок Host на транспорт/БД, Sysmex TCP-сеанс, точные QC-байты, формат чисел, "
     "журналы и остановку. Опубликованный пакет отдельно проверен на loopback. "
     "Проверка на стенде по-прежнему нужна для поведения реального анализатора и ЛИС.")


class Guide(BaseDocTemplate):
    def __init__(self, path):
        super().__init__(str(path), pagesize=A4, leftMargin=18 * mm, rightMargin=18 * mm,
                         topMargin=19 * mm, bottomMargin=18 * mm,
                         title="AnalyzerService - независимый от транспорта хост",
                         author="AnalyzerService project")
        frame = Frame(self.leftMargin, self.bottomMargin, self.width, self.height,
                      leftPadding=0, rightPadding=0, topPadding=0, bottomPadding=0)
        self.addPageTemplates(PageTemplate(id="guide", frames=[frame], onPage=self.footer))

    @staticmethod
    def footer(canvas, doc):
        canvas.saveState()
        canvas.setStrokeColor(LINE)
        canvas.line(18 * mm, 279 * mm, 192 * mm, 279 * mm)
        canvas.setFont("GuideArial", 7.5)
        canvas.setFillColor(MUTED)
        canvas.drawString(18 * mm, 282 * mm, "ANALYZERSERVICE / АРХИТЕКТУРА DLL")
        canvas.drawRightString(192 * mm, 12 * mm, str(doc.page))
        canvas.restoreState()


Guide(OUT).build(story)
print(OUT)
