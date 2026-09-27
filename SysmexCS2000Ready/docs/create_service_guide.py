"""Generate the Russian service guide from the current implementation."""

from pathlib import Path
from xml.sax.saxutils import escape

from reportlab.lib import colors
from reportlab.lib.enums import TA_CENTER, TA_LEFT
from reportlab.lib.pagesizes import A4
from reportlab.lib.styles import ParagraphStyle, getSampleStyleSheet
from reportlab.lib.units import mm
from reportlab.pdfbase import pdfmetrics
from reportlab.pdfbase.ttfonts import TTFont
from reportlab.platypus import (
    BaseDocTemplate, Frame, KeepTogether, PageTemplate, Paragraph, Spacer,
    Table, TableStyle,
)

ROOT = Path(__file__).resolve().parents[1]
DEST = ROOT / "output" / "pdf" / "SysmexCS2000_service_guide.pdf"
DEST.parent.mkdir(parents=True, exist_ok=True)

pdfmetrics.registerFont(TTFont("ArialRU", r"C:\Windows\Fonts\arial.ttf"))
pdfmetrics.registerFont(TTFont("ArialRUBold", r"C:\Windows\Fonts\arialbd.ttf"))
pdfmetrics.registerFontFamily("ArialRU", normal="ArialRU", bold="ArialRUBold")

navy = colors.HexColor("#183250")
teal = colors.HexColor("#167782")
muted = colors.HexColor("#526478")
paper = colors.HexColor("#F2F6F8")

styles = getSampleStyleSheet()
styles.add(ParagraphStyle(name="TitleRU", fontName="ArialRUBold", fontSize=20,
                          leading=25, textColor=navy, spaceAfter=10))
styles.add(ParagraphStyle(name="SubRU", fontName="ArialRU", fontSize=10.2,
                          leading=15, textColor=muted, spaceAfter=16))
styles.add(ParagraphStyle(name="H1RU", fontName="ArialRUBold", fontSize=13.5,
                          leading=17, textColor=navy, spaceBefore=15, spaceAfter=7))
styles.add(ParagraphStyle(name="H2RU", fontName="ArialRUBold", fontSize=10.5,
                          leading=14, textColor=teal, spaceBefore=10, spaceAfter=5))
styles.add(ParagraphStyle(name="BodyRU", fontName="ArialRU", fontSize=9,
                          leading=13.4, spaceAfter=6))
styles.add(ParagraphStyle(name="SmallRU", fontName="ArialRU", fontSize=8,
                          leading=11.3, spaceAfter=4))
styles.add(ParagraphStyle(name="CodeRU", fontName="ArialRU", fontSize=8.2,
                          leading=12, leftIndent=8, rightIndent=5, spaceAfter=2))


def para(text, style="BodyRU"):
    return Paragraph(text, styles[style])


story = []


def title(text, subtitle):
    story.extend([para(text, "TitleRU"), para(subtitle, "SubRU")])


def h1(text):
    story.append(para(text, "H1RU"))


def h2(text):
    story.append(para(text, "H2RU"))


def body(text):
    story.append(para(text))


def step(number, text):
    story.append(para(f"<b>{number}.</b> {text}"))


def box(lines):
    cells = [[para(line, "CodeRU")] for line in lines]
    table = Table(cells, colWidths=[173 * mm], hAlign="LEFT")
    table.setStyle(TableStyle([
        ("BACKGROUND", (0, 0), (-1, -1), paper),
        ("BOX", (0, 0), (-1, -1), 0.5, colors.HexColor("#DAE5EA")),
        ("LEFTPADDING", (0, 0), (-1, -1), 8),
        ("RIGHTPADDING", (0, 0), (-1, -1), 8),
        ("TOPPADDING", (0, 0), (-1, -1), 3),
        ("BOTTOMPADDING", (0, 0), (-1, -1), 3),
    ]))
    story.extend([table, Spacer(1, 6)])


title("Sysmex CS-2000i: руководство по сервису",
      "Архитектура, последовательность вызовов, хранение сырых результатов и эксплуатация. "
      "Состояние реализации: 27 сентября 2026 г.; .NET 9.0.")

h1("1. Назначение и состав решения")
body("Windows Service принимает TCP-подключение анализатора Sysmex CS-2000i. В решение входят "
     "два отдельных протокольных драйвера: ASTM E1381/E1394 и Sysmex Host Online. "
     "Подключаемые DLL реализуют общий контракт IAnalyzerDriver; параметры берутся только из JSON.")
box([
    "AnalyzerService.Host → Windows Service, конфигурация, загрузка DLL, жизненный цикл",
    "AnalyzerService.Contracts → IAnalyzerDriver, AnalyzerSettings, IAnalyzerLogger",
    "AnalyzerService.Transport → общий TCP-host, состояние listener и соединений",
    "AnalyzerService.LisDatabase → LisDBProvider, заказ и сопоставление кодов",
    "AnalyzerService.ResultFiles → RawResultQueue, файлы и выделенный поток",
    "SysmexCS2000.Driver → ASTM-кадры, сессия, разбор и формат ЛИС",
    "SysmexCS2000.HostOnline.Driver → R/S/D-тексты, сборка блоков, формат ЛИС",
])
body("Новая очередь файлов намеренно отделена от TCP-host: транспорт знает только сокеты, "
     "а очередь знает только каталог, байты и функцию преобразования. Такая граница подходит "
     "и для будущих приборов с другим прикладным протоколом.")

h1("2. Запуск службы: точный порядок")
step(1, "Program создаёт Generic Host и регистрирует Worker, поставщик JSON, проверку настроек, "
     "AnalyzerManager и фабрику логгеров.")
step(2, "Worker.ExecuteAsync читает все JSON из каталога configs рядом с исполняемым файлом, "
     "проверяет каждую конфигурацию и добавляет активные приборы в AnalyzerManager.")
step(3, "AnalyzerManager запускает разрешённые приборы. AnalyzerRuntime.Load загружает "
     "указанную DllPath сборку через DriverLoadContext, создаёт IAnalyzerDriver и вызывает Initialize.")
step(4, "RunAsync драйвера создаёт RawResultQueue, при ResultHandlerStatus=true запускает его выделенный поток, затем "
     "вызывает TcpHost.Start(IPaddress, Port). Поток очереди сразу видит .raw от прошлых запусков.")
step(5, "TcpHost.AcceptAsync ожидает клиента без блокирования потока службы. После подключения "
     "драйвер читает сообщения прибора в цикле до отключения или остановки.")
body("Если параметр WorkStatus или ActiveStatus отключает прибор, его запуск определяется "
     "существующей логикой менеджера. Новых ключей конфигурации для очереди не введено.")

h1("3. Приём и маршрутизация сообщений")
h2("Sysmex Host Online")
step(1, "ReadTextAsync принимает кадр между STX и ETX; AddBlock собирает все блоки одного "
     "сообщения и сохраняет исходные кадры без нормализации в Raw.")
step(2, "R221: ParseInquiry → LisDBProvider.GetOrder → BuildOrder → отправка S221 по TCP. "
     "Этот путь остаётся синхронным по отношению к запросу задания.")
step(3, "D121/D221: по Sample Distinction Code в позиции 8 определяется контроль C. "
     "Пациентский результат передаётся SaveResult, контроль - SaveQualityControl. "
     "Приёмник не вызывает TranslateResultCode и не создаёт файлы ЛИС.")
step(4, "DS21 представляет информацию о пробе и не создаёт файла результата.")
h2("ASTM")
step(1, "AstmSession.ReceiveMessageAsync принимает ENQ, проверяет кадры и checksum, "
     "отправляет предусмотренные ASTM ACK/NAK и завершает транзакцию на EOT. "
     "Возвращает текст принятых записей и исходные байты транзакции.")
step(2, "Q-запись: GetQuerySampleId → LisDBProvider.GetOrder → BuildOrder/BuildEmpty → "
     "AstmSession.SendMessageAsync.")
step(3, "O/R-записи: по Sample ID QC... или полю Action Code = Q определяется контроль. "
     "Далее исходные байты идут в SaveQualityControl либо SaveResult.")
body("У Host Online в текущем режиме дополнительный прикладной ACK на D/S не вводился: "
     "решение о нём отложено до проверки с реальным анализатором. ASTM ACK/NAK относится "
     "к уже существующему канальному обмену ASTM и не менялся в этой доработке.")

h1("4. Файловая очередь результатов")
body("Путь вычисляется из существующего ResultsFolder: абсолютный путь используется как есть; "
     "относительный разрешается относительно &lt;каталог службы&gt;/&lt;AnalyzerName&gt;. "
     "Каждая законченная передача результата одного образца создаёт отдельный файл. "
     "При повторном измерении идентификатор образца совпадает, но timestamp и GUID сохраняют оба измерения.")
body("При ResultHandlerStatus=false сырые сообщения продолжают сохраняться, но рабочий поток "
     "не запускается. Они обрабатываются при следующем запуске с включённым обработчиком.")
box([
    "ResultsFolder/",
    "  SYS2000_&lt;SampleId&gt;_&lt;UTC&gt;_&lt;GUID&gt;.raw  ← готовые пациентские сообщения",
    "  archive/  ← успешно преобразованные исходные .raw",
    "  errors/   ← ошибка разбора, нет результатов или кодов ЛИС",
    "  QualityControl/  ← неизменённые QC .raw; будущий обработчик",
    "OutputFolder/  ← выходные .res и .ok для ЛИС",
])
step(1, "SaveResult синхронно пишет байты в .raw.tmp и переименовывает в .raw. "
     "Поток обработки не видит частично записанный файл.")
step(2, "Выделенный Thread запускает Run; он обходит *.raw верхнего уровня при старте "
     "и далее после сигнала записи или каждые две секунды. Папки archive/errors/QC не сканируются.")
step(3, "ProcessFile читает байты; драйвер восстанавливает тело протокола и вызывает "
     "свой ResultHandler. Тот извлекает тесты, сопоставляет их через LisDBProvider и "
     "создаёт .res, а затем маркер .ok в OutputFolder.")
step(4, "После успеха исходный .raw переносится в archive. Любое исключение "
     "обработки логируется с путём файла и ведёт к переносу в errors. "
     "Это касается пустого результата и случая, когда ни один тест не настроен для ЛИС.")
step(5, "Если сам перенос не удался, исходник остаётся на месте и ошибка логируется. "
     "Имя выходных .res/.ok основано на имени .raw; наличие .ok предотвращает повторный "
     "вывод того же результата при следующем проходе.")
body("Важно: перемещение в errors применяется также к временным ошибкам БД ЛИС. "
     "Такие файлы можно проанализировать и после устранения причины вернуть вручную "
     "в корень ResultsFolder для повторной обработки. Перемещение выполнять только после "
     "проверки выходных .ok и журналов.")

h1("5. Контроль качества")
story.append(KeepTogether([story.pop(), para("Контроль сохраняется функцией SaveQualityControl непосредственно при приёме. "
     "В QualityControl лежат точные входящие байты: для Host Online - один или несколько "
     "кадров STX...ETX, для ASTM - ENQ, все входящие кадры и EOT. Поля не переводятся "
     "в формат ЛИС, коды тестов не сопоставляются, .res/.ok не создаются. "
     "Будущий QC-обработчик может читать эти .raw независимо от текущей очереди пациентов.")]))

h1("6. Остановка и восстановление")
step(1, "Service Control Manager вызывает Worker.StopAsync, затем AnalyzerManager.StopAllAsync.")
step(2, "AnalyzerRuntime отменяет token и вызывает IAnalyzerDriver.StopAsync; драйвер "
     "закрывает активный TcpClient и TcpHost, прерывая AcceptAsync/ReadAsync.")
step(3, "RunAsync выходит из сетевого цикла и в finally вызывает RawResultQueue.Stop. "
     "Выделенный поток завершает текущий файл и прекращает обход каталога.")
step(4, "После завершения задачи драйвера AnalyzerRuntime освобождает ресурсы и "
     "контекст DLL. Непереработанные .raw остаются на диске и подбираются при следующем запуске.")
body("Асинхронность используется в ожидании сети и завершения рабочих задач. "
     "Краткие файловые операции и обращение к БД внутри выделенного обработчика выполняются "
     "синхронно; TCP-чтение не ждёт преобразования результата в ЛИС.")

h1("7. Настройка, сборка и проверка")
body("Используется имеющийся JSON из configs. Для этой доработки ни один ключ не добавлен. "
     "Проверьте AnalyzerName, ActiveStatus, WorkStatus, ResultHandlerStatus, Protocol, "
     "DllPath, IPaddress, Port, ResultsFolder, OutputFolder и ConnectionString. "
     "Два драйвера не могут слушать одну пару IP-адрес/порт одновременно.")
box([
    "dotnet build .\\AnalyzerService.sln --maxcpucount:1",
    "dotnet run --project .\\tests\\SysmexCS2000.Driver.Tests",
    "dotnet run --project .\\tests\\SysmexCS2000.HostOnline.Driver.Tests",
    ".\\package.ps1  # Windows: публикует host и DLL с зависимостями",
])
body("После публикации проверьте наличие AnalyzerService.ResultFiles.dll в каталоге "
     "каждого драйвера. Для диагностики смотрите Service/Transport/Protocol/Result/Error логи: "
     "создание .raw, перенос в archive/errors и точное исключение каждого сбоя. "
     "Общий TcpHost периодически пишет состояние listener, endpoint, клиента, RX/TX и "
     "последнюю ошибку. Отсутствие RX при работающем listener не означает блокировку сокета.")

h1("8. Границы и источники")
body("Тексты и канальные правила протоколов берутся из проектных документов "
     "2_5197664667366890337.pdf (Host Online) и 2_5197664667366890338.pdf (ASTM). "
     "Это руководство описывает фактическую реализацию кода; оно не заменяет проверку "
     "обмена и ACK с реальным CS-2000i перед промышленным вводом.")
body("Проверены сборка .NET 9 и консольные тесты обоих драйверов. "
     "Интеграционные испытания с реальным прибором и БД ЛИС требуют соответствующего стенда.")


def page_art(canvas, doc):
    canvas.saveState()
    width, height = A4
    canvas.setStrokeColor(colors.HexColor("#D9E4E9"))
    canvas.line(18 * mm, height - 19 * mm, width - 18 * mm, height - 19 * mm)
    canvas.setFont("ArialRU", 7.5)
    canvas.setFillColor(muted)
    canvas.drawString(18 * mm, height - 15 * mm, "SYSМEX CS-2000i  /  SERVICE GUIDE")
    canvas.drawRightString(width - 18 * mm, 13 * mm, f"{doc.page}")
    canvas.restoreState()


doc = BaseDocTemplate(str(DEST), pagesize=A4, leftMargin=18 * mm,
                      rightMargin=18 * mm, topMargin=25 * mm, bottomMargin=20 * mm,
                      title="Sysmex CS-2000i - руководство по сервису",
                      author="AnalyzerService project")
frame = Frame(18 * mm, 20 * mm, 174 * mm, 252 * mm, leftPadding=0,
              rightPadding=0, topPadding=0, bottomPadding=0)
doc.addPageTemplates(PageTemplate(id="Normal", frames=frame, onPage=page_art))
doc.build(story)
print(DEST)
