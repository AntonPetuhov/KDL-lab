using AnalyzerService.LisDatabase;
using AnalyzerService.Contracts;
using AnalyzerService.Transport;
using AnalyzerService.ResultFiles;
using AnalyzerService.Host.Drivers;
using AnalyzerService.Host.Logging;
using AnalyzerService.Host.Runtime;
using SysmexCS2000.HostOnline.Driver.Lis;
using System.Net;
using System.Net.Sockets;
using System.Text;
using SysmexCS2000.HostOnline.Driver;
using SysmexCS2000.HostOnline.Driver.Protocol;

/// <summary>Выполняет автономные проверки фиксированного формата Sysmex Host Online.</summary>
internal static class Program
{
    /// <summary>Запускает проверки и возвращает код процесса.</summary>
    private static int Main()
    {
        try
        {
            ParseInquiry();
            ParseResult();
            BuildOrder();
            RawQueueRoutesFiles();
            OrderCodesAndPatientName();
            QualityControlRoutesFromConnection().GetAwaiter().GetResult();
            EmptyResultGoesToErrors().GetAwaiter().GetResult();
            TcpHostReportsStatus().GetAwaiter().GetResult();
            LoaderExceptionIsWrittenToFile();
            ServiceOwnsTcpHost().GetAwaiter().GetResult();
            Console.WriteLine("All Host Online protocol tests passed.");
            return 0;
        }
        catch (Exception ex) { Console.Error.WriteLine(ex); return 1; }
    }

    /// <summary>Проверяет позиции Sample ID и кода параметра R221.</summary>
    private static void ParseInquiry()
    {
        string body = Header('R', '2', "123456", new string(' ', 15)) + "010" + new string(' ', 6);
        HostOnlineInquiry inquiry = new HostOnlineCodec().ParseInquiry(body);
        Equal("123456", inquiry.Header.SampleId, "Sample ID");
        Equal("010", inquiry.ExistingParameters.Single(), "parameter");
    }

    /// <summary>Проверяет разбор D121: код, данные и флаг.</summary>
    private static void ParseResult()
    {
        string body = Header('D', '1', "123456", "IVANOV") + "010 1234+";
        HostOnlineResult result = new HostOnlineCodec().ParseResult(body);
        Equal("010", result.Items.Single().ParameterCode, "result code");
        Equal("1234", result.Items.Single().Data, "result value");
        Equal('+', result.Items.Single().Flag, "result flag");
    }

    /// <summary>Проверяет S221, фиксированную длину и специальный код 000.</summary>
    private static void BuildOrder()
    {
        HostOnlineCodec codec = new();
        HostOnlineInquiry inquiry = codec.ParseInquiry(Header('R', '2', "123456", new string(' ', 15)));
        var emptyOrder = new LisOrder("123456", "", "", "", "", "", []);
        string response = codec.BuildOrder(inquiry, emptyOrder, "000").Single();
        Equal('S', response[0], "response kind");
        Equal("000", response.Substring(58, 3), "empty code");
        Equal(67, response.Length, "response length");
    }

    /// <summary>Регрессия стенда: два кода задания и транслитерация ФИО в ASCII.</summary>
    private static void OrderCodesAndPatientName()
    {
        HostOnlineCodec codec = new();
        string loggedR221 = "R2210101 300926151200000304     9000003160B               " +
                            "400      650      660      870      880      ";
        HostOnlineInquiry inquiry = codec.ParseInquiry(loggedR221);
        LisOrder order = new("9000003160", "P1", "ТЕСТ", "МИХАЛ ИВАН", "", "", ["392", "051"]);
        string response = codec.BuildOrder(inquiry, order, "000").Single();
        Equal("390", response.Substring(58, 3), "first order group");
        Equal("050", response.Substring(67, 3), "second order group");
        Equal("TEST MIKHAL IVA", response.Substring(43, 15), "ASCII patient name");
        Equal('B', response[42], "barcode ID information preserved");
        Equal(76, response.Length, "two order blocks length");
        Equal(true, response.All(c => c <= 0x7F), "ASCII response");
    }

    /// <summary>Формирует 58-символьный тестовый заголовок.</summary>
    private static string Header(char kind, char subtype, string sample, string patient, char sampleType = 'U') =>
        $"{kind}{subtype}210101{sampleType}2609201234RACK01" + "01" + sample.PadLeft(15) + "B" + patient.PadRight(15)[..15];

    /// <summary>Проверяет архив, ошибки и неизменность исходных QC-байтов.</summary>
    private static void RawQueueRoutesFiles()
    {
        string root = Path.Combine(Path.GetTempPath(), "SysmexQC-" + Guid.NewGuid().ToString("N"));
        try
        {
            AnalyzerSettings settings = new()
            {
                AnalyzerName = "QC test", ConnectionType = "TCPIP", ResultsFolder = root,
                OutputFolder = Path.Combine(root, "Patient"), ConnectionString = "unused", AnalyzerCode = "915"
            };
            using RawResultQueue queue = new(settings, new TestLogger(), (bytes, _) =>
            {
                if (bytes[0] == 0) throw new InvalidDataException("No LIS tests");
            });
            byte[] good = [0x02, 0x41, 0x03];
            byte[] bad = [0x00];
            byte[] control = [0x02, 0x51, 0x43, 0x03];
            // Файл, накопленный до запуска рабочего потока, должен быть поднят при старте.
            queue.SaveResult("PATIENT1", good);
            queue.Start();
            queue.SaveResult("PATIENT2", bad);
            queue.SaveQualityControl("CONTROL1", control);
            for (int attempt = 0; attempt < 100 &&
                 (Directory.GetFiles(Path.Combine(root, "archive"), "*.raw").Length != 1 ||
                  Directory.GetFiles(Path.Combine(root, "errors"), "*.raw").Length != 1); attempt++)
                Thread.Sleep(20);
            queue.Stop();
            string qc = Path.Combine(root, "QualityControl");
            Equal(1, Directory.GetFiles(Path.Combine(root, "archive"), "*.raw").Length, "archived results");
            Equal(1, Directory.GetFiles(Path.Combine(root, "errors"), "*.raw").Length, "failed results");
            Equal(1, Directory.GetFiles(qc, "*.raw").Length, "raw QC files");
            Equal(true, File.ReadAllBytes(Directory.GetFiles(qc, "*.raw")[0]).SequenceEqual(control), "QC bytes unchanged");
            Equal(false, Directory.Exists(settings.OutputFolder), "patient output untouched");
        }
        finally
        {
            foreach (string folder in new[] { "QualityControl", "archive", "errors" })
            {
                string path = Path.Combine(root, folder);
                if (!Directory.Exists(path)) continue;
                foreach (string file in Directory.GetFiles(path)) File.Delete(file);
                Directory.Delete(path);
            }
            if (Directory.Exists(root)) Directory.Delete(root);
        }
    }

    /// <summary>Проверяет, что DLL работает с переданным потоком без собственного TCP host.</summary>
    private static async Task QualityControlRoutesFromConnection()
    {
        string root = Path.Combine(Path.GetTempPath(), "SysmexQCRoute-" + Guid.NewGuid().ToString("N"));
        TestLogger logger = new();
        AnalyzerSettings settings = new()
        {
            AnalyzerName = "QC route", ConnectionType = "TCPIP",
            ResultsFolder = root, OutputFolder = Path.Combine(root, "Patient"),
            ConnectionString = "unused", AnalyzerCode = "915", ResultHandlerStatus = true
        };
        using AnalyzerSysmexCS2000HostOnline analyzer = new(logger, settings);
        analyzer.Start();
        try
        {
            string body = Header('D', '1', "CONTROL2", "QC", 'C') + "010 5678+";
            byte[] frame = [0x02, .. Encoding.ASCII.GetBytes(body), 0x03];
            await FeedAsync(analyzer, frame);
            string qc = Path.Combine(root, "QualityControl");
            for (int attempt = 0; attempt < 100 && (!Directory.Exists(qc) || Directory.GetFiles(qc, "*.raw").Length == 0); attempt++)
                await Task.Delay(20);
            if (!Directory.Exists(qc))
                throw new InvalidOperationException("QC каталог не создан: " + string.Join(" | ", logger.TransportMessages) + " | " + string.Join(" | ", logger.ProtocolMessages) + " | " + string.Join(" | ", logger.ErrorMessages));
            Equal(1, Directory.GetFiles(qc, "*.raw").Length, "routed QC result");
            Equal(true, File.ReadAllBytes(Directory.GetFiles(qc, "*.raw")[0]).SequenceEqual(frame), "routed QC raw bytes");
            Equal(false, Directory.Exists(settings.OutputFolder), "routed patient output untouched");
        }
        finally
        {
            analyzer.Stop();
            string qc = Path.Combine(root, "QualityControl");
            if (Directory.Exists(qc))
            {
                foreach (string file in Directory.GetFiles(qc)) File.Delete(file);
                Directory.Delete(qc);
            }
            foreach (string folder in new[] { "archive", "errors", "Patient" })
            {
                string path = Path.Combine(root, folder);
                if (!Directory.Exists(path)) continue;
                foreach (string file in Directory.GetFiles(path)) File.Delete(file);
                Directory.Delete(path);
            }
            if (Directory.Exists(root)) Directory.Delete(root);
        }
    }

    /// <summary>Проверяет, что D-текст без тестов сохраняется и перемещается в errors.</summary>
    private static async Task EmptyResultGoesToErrors()
    {
        string root = Path.Combine(Path.GetTempPath(), "SysmexEmpty-" + Guid.NewGuid().ToString("N"));
        TestLogger logger = new();
        AnalyzerSettings settings = new()
        {
            AnalyzerName = "Empty result", ConnectionType = "TCPIP",
            ResultsFolder = root, OutputFolder = Path.Combine(root, "Patient"),
            ConnectionString = "unused", AnalyzerCode = "915", ResultHandlerStatus = true
        };
        using AnalyzerSysmexCS2000HostOnline analyzer = new(logger, settings);
        analyzer.Start();
        try
        {
            byte[] frame = [0x02, .. Encoding.ASCII.GetBytes(Header('D', '1', "EMPTY1", "PATIENT")), 0x03];
            await FeedAsync(analyzer, frame);
            string errors = Path.Combine(root, "errors");
            for (int attempt = 0; attempt < 100 && Directory.GetFiles(errors, "*.raw").Length == 0; attempt++)
                await Task.Delay(20);
            Equal(1, Directory.GetFiles(errors, "*.raw").Length, "empty result in errors");
            Equal(true, File.ReadAllBytes(Directory.GetFiles(errors, "*.raw")[0]).SequenceEqual(frame), "empty result raw bytes");
        }
        finally
        {
            analyzer.Stop();
            foreach (string folder in new[] { "QualityControl", "archive", "errors", "Patient" })
            {
                string path = Path.Combine(root, folder);
                if (!Directory.Exists(path)) continue;
                foreach (string file in Directory.GetFiles(path)) File.Delete(file);
                Directory.Delete(path);
            }
            if (Directory.Exists(root)) Directory.Delete(root);
        }
    }

    /// <summary>Подаёт один кадр в DLL и ожидает штатного EOF тестового потока.</summary>
    private static async Task FeedAsync(AnalyzerSysmexCS2000HostOnline analyzer, byte[] frame)
    {
        using MemoryStream input = new(frame);
        try { await analyzer.HandleConnectionAsync(new TestConnection(input), CancellationToken.None); }
        catch (EndOfStreamException) { }
    }

    /// <summary>Тестовый адаптер потока без TcpListener и TcpClient.</summary>
    private sealed class TestConnection(Stream stream) : IAnalyzerConnection
    {
        public Stream Stream => stream;
        public void RecordRead() { }
        public void RecordWrite() { }
    }

    /// <summary>Проверяет реальное loopback-подключение и периодические записи состояния.</summary>
    private static async Task TcpHostReportsStatus()
    {
        TestLogger logger = new();
        using TcpHost host = new(logger, "loopback", TimeSpan.FromMilliseconds(50));
        host.Start(IPAddress.Loopback, 0);
        string initial = logger.TransportMessages.Single();
        int port = int.Parse(initial.Split("endpoint=127.0.0.1:")[1].Split(',')[0]);
        using TcpClient peer = new();
        Task<TcpClient> accepting = host.AcceptAsync(CancellationToken.None);
        await peer.ConnectAsync(IPAddress.Loopback, port);
        using TcpClient accepted = await accepting.WaitAsync(TimeSpan.FromSeconds(5));
        host.RecordRead(); host.RecordWrite();
        await Task.Delay(120);
        if (!logger.TransportMessages.Any(m => m.Contains("RX=") && m.Contains("TX=") && !m.Contains("RX=нет")))
            throw new InvalidOperationException("Periodic TCP status was not logged.");
        host.ReleaseClient(accepted);
        host.Stop();
    }

    /// <summary>Проверяет файловое логирование исключения, которое затем пробрасывается.</summary>
    private static void LoaderExceptionIsWrittenToFile()
    {
        string name = "MissingDriver_" + Guid.NewGuid().ToString("N");
        string logRoot = Path.Combine(AppContext.BaseDirectory, name);
        AnalyzerSettings settings = new()
        {
            AnalyzerName = name, ConnectionType = "TCPIP", IPaddress = "127.0.0.1", Port = 12345,
            DllPath = "missing-driver.dll", ResultsFolder = "Results", ConnectionString = "unused",
            Protocol = "SYSMEX_HOST_ONLINE"
        };
        try
        {
            using AnalyzerRuntime runtime = new(settings, new DriverLoader(), new AnalyzerLoggerFactory());
            try { runtime.StartAsync(CancellationToken.None).GetAwaiter().GetResult(); }
            catch (FileNotFoundException) { }
            string errorDir = Path.Combine(logRoot, "Logs", "Error");
            Equal(1, Directory.GetFiles(errorDir, "*.log").Length, "file error log");
            if (!File.ReadAllText(Directory.GetFiles(errorDir, "*.log")[0]).Contains("FileNotFoundException"))
                throw new InvalidOperationException("Thrown loader exception was not logged.");
        }
        finally { DeleteTestDirectory(logRoot, AppContext.BaseDirectory); }
    }

    /// <summary>Проверяет сквозной путь через Host-owned TcpHost и динамически загруженный DLL.</summary>
    private static async Task ServiceOwnsTcpHost()
    {
        string name = "HostTcp_" + Guid.NewGuid().ToString("N");
        string logRoot = Path.Combine(AppContext.BaseDirectory, name);
        string results = Path.Combine(Path.GetTempPath(), name);
        using TcpListener probe = new(IPAddress.Loopback, 0);
        probe.Start();
        int port = ((IPEndPoint)probe.LocalEndpoint).Port;
        probe.Stop();
        AnalyzerSettings settings = new()
        {
            AnalyzerName = name, ConnectionType = "TCPIP", IPaddress = "127.0.0.1", Port = port,
            DllPath = Path.Combine(AppContext.BaseDirectory, "SysmexCS2000.HostOnline.Driver.dll"),
            ResultsFolder = results, OutputFolder = Path.Combine(results, "Patient"),
            ConnectionString = "unused", Protocol = "SYSMEX_HOST_ONLINE", ResultHandlerStatus = true
        };
        try
        {
            using AnalyzerRuntime runtime = new(settings, new DriverLoader(), new AnalyzerLoggerFactory());
            await runtime.StartAsync(CancellationToken.None);
            using TcpClient client = new();
            await client.ConnectAsync(IPAddress.Loopback, port);
            byte[] frame = [0x02, .. Encoding.ASCII.GetBytes(Header('D', '1', "CONTROL3", "QC", 'C') + "010 9999+"), 0x03];
            await client.GetStream().WriteAsync(frame);
            string qc = Path.Combine(results, "QualityControl");
            for (int i = 0; i < 100 && Directory.GetFiles(qc, "*.raw").Length == 0; i++) await Task.Delay(20);
            Equal(1, Directory.GetFiles(qc, "*.raw").Length, "hosted QC raw file");
            await runtime.StopAsync(CancellationToken.None);
        }
        finally
        {
            DeleteTestDirectory(logRoot, AppContext.BaseDirectory);
            DeleteTestDirectory(results, Path.GetTempPath());
        }
    }

    /// <summary>Удаляет только проверенный тестовый каталог внутри явного родителя.</summary>
    private static void DeleteTestDirectory(string directory, string parent)
    {
        string full = Path.GetFullPath(directory);
        string root = Path.GetFullPath(parent).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
        if (!full.StartsWith(root, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Небезопасный путь удаления теста: " + full);
        if (Directory.Exists(full)) Directory.Delete(full, true);
    }

    /// <summary>Собирает сообщения общего транспорта без файловых побочных эффектов.</summary>
    private sealed class TestLogger : IAnalyzerLogger
    {
        private readonly System.Collections.Concurrent.ConcurrentQueue<string> transportMessages = new();
        private readonly System.Collections.Concurrent.ConcurrentQueue<string> protocolMessages = new();
        private readonly System.Collections.Concurrent.ConcurrentQueue<string> errors = new();
        public IReadOnlyCollection<string> TransportMessages => transportMessages.ToArray();
        public IReadOnlyCollection<string> ProtocolMessages => protocolMessages.ToArray();
        public IReadOnlyCollection<string> ErrorMessages => errors.ToArray();
        public void Service(string message) { }
        public void Transport(string message) => transportMessages.Enqueue(message);
        public void Protocol(string message) => protocolMessages.Enqueue(message);
        public void Result(string message) { }
        public void Error(string message, Exception exception) => errors.Enqueue(message + " " + exception);
    }

    /// <summary>Сравнивает ожидаемое и фактическое значение.</summary>
    private static void Equal<T>(T expected, T actual, string name)
    {
        if (!EqualityComparer<T>.Default.Equals(expected, actual)) throw new InvalidOperationException($"{name}: expected {expected}, actual {actual}");
    }
}
