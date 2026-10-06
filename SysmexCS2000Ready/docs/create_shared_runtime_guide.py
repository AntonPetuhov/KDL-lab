"""Build the deployment and call-sequence guide for the current package."""

from pathlib import Path
from xml.sax.saxutils import escape

from reportlab.lib import colors
from reportlab.lib.pagesizes import A4
from reportlab.lib.styles import ParagraphStyle, getSampleStyleSheet
from reportlab.lib.units import mm
from reportlab.pdfbase import pdfmetrics
from reportlab.pdfbase.ttfonts import TTFont
from reportlab.platypus import SimpleDocTemplate, Paragraph, Spacer, Table, TableStyle, KeepTogether


ROOT = Path(__file__).resolve().parents[1]
OUTPUT = ROOT / "output/pdf/AnalyzerService_shared_runtime_guide.pdf"
OUTPUT.parent.mkdir(parents=True, exist_ok=True)
pdfmetrics.registerFont(TTFont("ArialR", r"C:\Windows\Fonts\arial.ttf"))
pdfmetrics.registerFont(TTFont("ArialB", r"C:\Windows\Fonts\arialbd.ttf"))
pdfmetrics.registerFontFamily("ArialR", normal="ArialR", bold="ArialB")

ink = colors.HexColor("#182F44")
blue = colors.HexColor("#1D617D")
light = colors.HexColor("#EEF5F8")
line = colors.HexColor("#CBD9E1")
styles = getSampleStyleSheet()
styles.add(ParagraphStyle(name="title2", fontName="ArialB", fontSize=18, leading=23, textColor=ink, spaceAfter=10))
styles.add(ParagraphStyle(name="lead2", fontName="ArialR", fontSize=9.2, leading=13.7, textColor=blue, spaceAfter=12))
styles.add(ParagraphStyle(name="head2", fontName="ArialB", fontSize=12, leading=16, textColor=ink, spaceBefore=11, spaceAfter=5, keepWithNext=True))
styles.add(ParagraphStyle(name="sub2", fontName="ArialB", fontSize=9.5, leading=13, textColor=blue, spaceBefore=7, spaceAfter=4, keepWithNext=True))
styles.add(ParagraphStyle(name="body2", fontName="ArialR", fontSize=8.6, leading=12.5, spaceAfter=5))
styles.add(ParagraphStyle(name="small2", fontName="ArialR", fontSize=7.6, leading=10.8, spaceAfter=2))
styles.add(ParagraphStyle(name="code2", fontName="ArialR", fontSize=7.5, leading=10.8, spaceAfter=1))
story = []


def para(value, kind="body2"):
    return Paragraph(value, styles[kind])


def text(value):
    story.append(para(value))


def heading(value):
    story.append(para(value, "head2"))


def sub(value):
    story.append(para(value, "sub2"))


def item(n, value):
    text(f"<b>{n}.</b> {value}")


def box(lines):
    rows = [[para(escape(s), "code2")] for s in lines]
    table = Table(rows, colWidths=[171 * mm], hAlign="LEFT")
    table.setStyle(TableStyle([
        ("BACKGROUND", (0, 0), (-1, -1), light),
        ("BOX", (0, 0), (-1, -1), .5, line),
        ("LEFTPADDING", (0, 0), (-1, -1), 7),
        ("RIGHTPADDING", (0, 0), (-1, -1), 7),
        ("TOPPADDING", (0, 0), (-1, -1), 2),
        ("BOTTOMPADDING", (0, 0), (-1, -1), 2),
    ]))
    story.extend([table, Spacer(1, 5)])


def grid(rows, widths):
    data = [[para(escape(v), "small2") for v in row] for row in rows]
    table = Table(data, colWidths=widths, repeatRows=1, hAlign="LEFT")
    table.setStyle(TableStyle([
        ("BACKGROUND", (0, 0), (-1, 0), light),
        ("GRID", (0, 0), (-1, -1), .4, line),
        ("VALIGN", (0, 0), (-1, -1), "TOP"),
        ("LEFTPADDING", (0, 0), (-1, -1), 5),
        ("RIGHTPADDING", (0, 0), (-1, -1), 5),
        ("TOPPADDING", (0, 0), (-1, -1), 4),
        ("BOTTOMPADDING", (0, 0), (-1, -1), 4),
    ]))
    story.extend([table, Spacer(1, 5)])


story.append(para("AnalyzerService / Sysmex CS-2000i", "title2"))
story.append(para("Руководство по работе службы, размещению общих библиотек, обработке результатов и сборке. Версия 06.10.2026; .NET 9 / Windows Service.", "lead2"))

heading("1. Назначение и границы ответственности")
text("<b>AnalyzerService.Host</b> читает JSON, загружает плагины и управляет их жизненным циклом. В <b>AnalyzerRuntime</b> нет знаний о TCP, COM, файловом обмене и формате сообщений. Конкретный <b>SysmexCS2000.HostOnline.Driver</b> открывает TCP listener, принимает кадры Host Online, запрашивает задания в ЛИС и сохраняет результаты. Общий <b>AnalyzerService.Transport</b> предоставляет TCP-host как библиотеку, но решение использовать его принимает только драйвер.")
text("Общие сборки Contracts, Transport, LisDatabase, ResultFiles и их пакетные зависимости включены в однофайловую публикацию Host. Это <b>зависимости упаковки</b>, а не вызовы транспорта из AnalyzerRuntime. Внешняя DLL Sysmex здесь не используется: DLL драйвера - собственный управляемый плагин протокола Host Online.")
grid([
    ["Компонент", "Ответственность"],
    ["Host / Worker / Manager / Runtime", "Конфигурация, загрузка, запуск, наблюдение задачи, Stop и Dispose."],
    ["DriverLoadContext", "Изолирует код плагина, но разделяет четыре общие сборки с Host."],
    ["Sysmex-драйвер", "TCP listener, R221/S221, D121/D221, журнал, вызовы БД и очереди."],
    ["RawResultQueue", "Исходные .raw, отдельный поток, archive/errors, сырые QC-файлы."],
    ["HostOnlineResultHandler", "Сопоставление кодов с ЛИС, числовой формат, .res/.ok."],
], [56 * mm, 115 * mm])

heading("2. Последовательность запуска")
item(1, "Program создаёт Generic Host, регистрирует службы и Worker; Windows Service запускает ExecuteAsync.")
item(2, "Worker читает только JSON из верхнего уровня configs, валидирует общие поля и добавляет активные анализаторы в AnalyzerManager.")
item(3, "Manager вызывает AnalyzerRuntime.StartAsync для каждого прибора. Runtime по DllPath создаёт DriverLoadContext, загружает DLL и экземпляр IAnalyzerDriver.")
item(4, "Runtime вызывает Initialize(logger, settings). Sysmex-драйвер проверяет собственные TCP/SQL/файловые поля и создаёт AnalyzerSysmexCS2000HostOnline, LisDBProvider и RawResultQueue.")
item(5, "Runtime вызывает RunAsync(token). Драйвер запускает поток очереди результатов, открывает TcpHost по IPaddress/Port из JSON и возвращает Task рабочего цикла. Manager наблюдает задачи всех приборов.")
box(["Program -> Worker.ExecuteAsync -> JsonAnalyzerSettingsProvider.LoadAll", "        -> AnalyzerManager.Add / StartAllAsync", "        -> AnalyzerRuntime.StartAsync -> DriverLoader.Load", "        -> IAnalyzerDriver.Initialize -> IAnalyzerDriver.RunAsync", "        -> AnalyzerSysmexCS2000HostOnline.RunConnectionsAsync"])
text("AcceptAsync и чтение TCP-потока асинхронны, чтобы не занимать поток ожиданием сети. Синхронные SQL-запросы, короткая запись сырого кадра и операции выделенного потока RawResultQueue остаются синхронными. Так устроен текущий код, и это важно учитывать при оценке задержек.")

heading("3. Запрос задания R221 и ответ S221")
text("Ниже фрагмент <b>реального стендового журнала</b> от 02.10.2026. Пробелы в 15-символьных полях значимы; при копировании текста из PDF их выравнивание может измениться. Для точного кадра используйте Protocol log.")
box(["RX: R2210101 021026110300000304     9000003160B               400      650      660      870      880"])
item(1, "ReadTextAsync извлекает тело между STX и ETX, записывает RX в Protocol и собирает блоки сообщения.")
item(2, "HostOnlineCodec.ParseInquiry проверяет R221, читает штатив, позицию, Sample ID и группы параметров. Driver пишет отдельную диагностическую запись с этими полями.")
item(3, "LisDBProvider.GetOrder находит пациента и незавершённые тесты по Sample ID. Каждому кандидату записывает причину включения или исключения; коды групп не дублируются.")
item(4, "HostOnlineCodec.BuildOrder формирует один или несколько S221 (до 22 кодов на блок), переносит предусмотренное поле имени пациента и параметры. WriteTextAsync добавляет STX/ETX и записывает TX в Protocol.")
text("Если SQL не нашёл пациента/задание, код ответа формируется существующей логикой драйвера. Дата рождения и пол не имеют отдельного поля в используемом формате S221. ACK/NAK поверх TCP в этой версии не добавлены: стендовая проверка этого поведения была отложена.")

heading("4. Получение D121/D221 и поток файлов")
text("Пример пациентского D121 из того же стендового журнала (здесь значения `******` - именно текст, а не число):")
box(["RX: D1210101U021026114700000504     9000003160BTEST MIKHAL IVA391******392******401******402******661******662******"])
item(1, "Driver записывает RX, тип D121, штатив, позицию, образец и перечень разобранных результатов. Признак U направляет пациентское сообщение в RawResultQueue.SaveResult.")
item(2, "SaveResult атомарно сохраняет <b>точные байты STX...ETX</b> в отдельный .raw файл по образцу и будит выделенный поток. Приём следующих сообщений не ждёт преобразования в ЛИС.")
item(3, "Выделенный поток подбирает .raw, вызывает ProcessStoredResult -> HostOnlineCodec.ParseResult -> HostOnlineResultHandler.Handle. Кодек применяет масштаб из таблицы Host Online; итог ЛИС округляется до одного десятичного знака и содержит запятую.")
item(4, "Обработчик ищет сопоставление с PSMV2 через LisDBProvider, создаёт .res и затем .ok. Успешный .raw переносится в Results/archive; при ошибке, отсутствии результатов или сопоставленных тестов - в Results/errors. Ошибка журналируется вместе с исключением.")
text("Если в поле типа стоит C, сообщение является контролем качества. SaveQualityControl записывает его байты без преобразования в Results/QualityControl; отдельный обработчик QC пока не реализован. Например, на стенде принят D121 с признаком C и тестами 402, 652, 882.")

heading("5. Повторные low-результаты 872 и 662")
text("Настройка прибора позволяет после med-измерения отправить low-измерение того же теста. При создании .res код low заменяется <b>только для поиска сопоставления в ЛИС</b>. Исходный D121/D221, .raw, код и величина результата прибора остаются прежними.")
grid([
    ["Код в D-сообщении", "Код для поиска PSMV2", "Результат"],
    ["882 (med)", "882", "Обычная строка .res"],
    ["872 (low)", "882", "Дополнительная строка .res того же теста"],
    ["652 (med)", "652", "Обычная строка .res"],
    ["662 (low)", "652", "Дополнительная строка .res того же теста"],
], [50 * mm, 53 * mm, 68 * mm])
text("Пример, <b>условные данные</b>: блок `872  123 ` кодек интерпретирует как 12.3 по масштабу кода 872; в .res пишется значение `12,3` с кодом PSMV2, найденным по 882. Блок `662  123 ` обрабатывается аналогично через 652. Если med и low находятся в одном сообщении, обе строки сохраняются в исходном порядке, без удаления дубликата. ЛИС должна допускать два результата с одним кодом теста в файле; это следует проверить при следующем стендовом прогоне.")

heading("6. Остановка и исключения")
item(1, "Service Control Manager вызывает Worker.StopAsync -> AnalyzerManager.StopAllAsync. Менеджер останавливает каждый Runtime последовательно и собирает ошибки.")
item(2, "Runtime отменяет token, вызывает синхронный IAnalyzerDriver.Stop и ожидает Completion. Sysmex Stop закрывает TCP listener и активное соединение, разблокируя AcceptAsync/ReadAsync.")
item(3, "RunConnectionsAsync в finally останавливает RawResultQueue после текущего файла; необработанные .raw сохраняются и будут подобраны при следующем запуске.")
item(4, "Manager вызывает Runtime.Dispose -> LoadedDriver.Dispose -> DLL.Dispose и DriverLoadContext.Unload. Затем освобождаются токены. Ошибки Stop/Dispose записываются в файловый Error-журнал и агрегируются.")
text("Исключение в обработчике сеанса сначала журналируется драйвером, после чего `throw;` передаёт тот же объект и стек выше: TCP-цикл фиксирует сбой клиента. Ошибки RunAsync, загрузки и остановки доходят до Worker/Manager и также записываются. Для ошибок файловой очереди файл переносится в errors; исключение не теряется из журнала.")

heading("7. Изоляция зависимостей и состав поставки")
box(["publish/AnalyzerService/AnalyzerService.Host.exe", "publish/AnalyzerService/configs/SysmexCS2000.json", "publish/AnalyzerService/drivers/SysmexCS2000HostOnline/", "    SysmexCS2000.HostOnline.Driver.dll", "    SysmexCS2000.HostOnline.Driver.deps.json"])
text("DriverLoadContext загружает сам Sysmex-плагин из его подпапки. Для Contracts, Transport, LisDatabase и ResultFiles он явно возвращает сборки основного контекста, чтобы не создавать несовместимые копии типов. Остальные специфичные зависимости нового драйвера требуют явного решения по публикации. Host не сканирует папку drivers автоматически: путь главной DLL берётся только из DllPath существующего JSON.")
text("Сборка Host однофайловая, но файл DLL драйвера должен оставаться внешним для динамической загрузки. Публикация framework-dependent: на сервере необходим .NET 9 Runtime. В корне пакета могут быть PDB/XML диагностические файлы; общих DLL рядом с драйвером нет. Native-библиотеки при необходимости извлекаются средой .NET из однофайлового EXE.")

heading("8. Сборка, развёртывание и проверка")
item(1, "В Visual Studio 2022 установите .NET 9 SDK и откройте AnalyzerService.sln. Выполните Restore NuGet и Build Solution в конфигурации Release / x64.")
item(2, "Из PowerShell в корне SysmexCS2000Ready запустите `./package.ps1`. Скрипт публикует Host для win-x64 и копирует в drivers ровно два файла Sysmex-плагина. Запуск повторно очищает только каталог publish проекта.")
item(3, "Перенесите весь каталог publish/AnalyzerService на сервер. Настройте configs/SysmexCS2000.json: локальный IPaddress и Port listener, подключение к SQL, OutputFolder, ResultsFolder и DllPath. Структура JSON не менялась.")
item(4, "Запускайте EXE как консольное приложение для проверки, затем регистрируйте Windows Service обычным способом. Учётной записи нужны права чтения configs/drivers, записи в результаты, выход ЛИС и журналы, а также доступ к SQL Server.")
box(["dotnet build AnalyzerService.sln -c Release --maxcpucount:1", "dotnet run --project tests/SysmexCS2000.HostOnline.Driver.Tests -c Release --no-build", "./package.ps1", "./tests/PublishedPackageSmoke.ps1"])
text("Smoke-тест запускает копию опубликованного Host на loopback, проверяет приём QC и точность сохранённых байтов. Он также отклоняет лишние файлы в папке драйвера. Проверка в реальном контуре всё ещё нужна для SQL-сопоставления 872/662 и поведения ЛИС при двух строках одного теста.")


def footer(canvas, doc):
    canvas.saveState()
    canvas.setStrokeColor(line)
    canvas.line(19 * mm, 279 * mm, 191 * mm, 279 * mm)
    canvas.setFont("ArialR", 7)
    canvas.setFillColor(blue)
    canvas.drawString(19 * mm, 282 * mm, "ANALYZERSERVICE / РУКОВОДСТВО ПО СЛУЖБЕ")
    canvas.drawRightString(191 * mm, 11 * mm, str(doc.page))
    canvas.restoreState()


document = SimpleDocTemplate(str(OUTPUT), pagesize=A4, leftMargin=19 * mm, rightMargin=19 * mm,
                             topMargin=20 * mm, bottomMargin=18 * mm, title="AnalyzerService: руководство",
                             author="AnalyzerService project")
document.build(story, onFirstPage=footer, onLaterPages=footer)
print(OUTPUT)
