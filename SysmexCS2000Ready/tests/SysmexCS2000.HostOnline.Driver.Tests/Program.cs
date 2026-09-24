using AnalyzerService.LisDatabase;
using AnalyzerService.Contracts;
using AnalyzerService.Transport;
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
            QualityControlCreatesSeparateFiles();
            QualityControlRoutesFromSocket().GetAwaiter().GetResult();
            TcpHostReportsStatus().GetAwaiter().GetResult();
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

    /// <summary>Формирует 58-символьный тестовый заголовок.</summary>
    private static string Header(char kind, char subtype, string sample, string patient, char sampleType = 'U') =>
        $"{kind}{subtype}210101{sampleType}2609201234RACK01" + "01" + sample.PadLeft(15) + "B" + patient.PadRight(15)[..15];

    /// <summary>Проверяет сохранение контроля в отдельную папку без обращения к БД ЛИС.</summary>
    private static void QualityControlCreatesSeparateFiles()
    {
        string root = Path.Combine(Path.GetTempPath(), "SysmexQC-" + Guid.NewGuid().ToString("N"));
        try
        {
            AnalyzerSettings settings = new()
            {
                AnalyzerName = "QC test", ConnectionType = "TCPIP", ResultsFolder = root,
                OutputFolder = Path.Combine(root, "Patient"), ConnectionString = "unused", AnalyzerCode = "915"
            };
            HostOnlineResult result = new HostOnlineCodec().ParseResult(Header('D', '1', "CONTROL1", "QC", 'C') + "010 1234+");
            Equal('C', result.Header.SampleType, "QC marker");
            new HostOnlineQualityControlHandler(settings, new TestLogger()).Handle(result);
            string qc = Path.Combine(root, "QualityControl");
            Equal(1, Directory.GetFiles(qc, "*.res").Length, "QC result files");
            Equal(1, Directory.GetFiles(qc, "*.ok").Length, "QC marker files");
            if (!File.ReadAllText(Directory.GetFiles(qc, "*.res")[0]).Contains("^^^010"))
                throw new InvalidOperationException("QC result code was not saved.");
            Equal(false, Directory.Exists(settings.OutputFolder), "patient output untouched");
        }
        finally
        {
            string qc = Path.Combine(root, "QualityControl");
            if (Directory.Exists(qc))
            {
                foreach (string file in Directory.GetFiles(qc)) File.Delete(file);
                Directory.Delete(qc);
            }
            if (Directory.Exists(root)) Directory.Delete(root);
        }
    }

    /// <summary>Проверяет путь от TCP-текста с признаком C до отдельного QC-файла.</summary>
    private static async Task QualityControlRoutesFromSocket()
    {
        string root = Path.Combine(Path.GetTempPath(), "SysmexQCRoute-" + Guid.NewGuid().ToString("N"));
        TestLogger logger = new();
        AnalyzerSettings settings = new()
        {
            AnalyzerName = "QC route", ConnectionType = "TCPIP", IPaddress = "127.0.0.1", Port = 0,
            ResultsFolder = root, OutputFolder = Path.Combine(root, "Patient"),
            ConnectionString = "unused", AnalyzerCode = "915", ResultHandlerStatus = true
        };
        using AnalyzerSysmexCS2000HostOnline analyzer = new(logger, settings);
        using CancellationTokenSource stop = new(TimeSpan.FromSeconds(5));
        Task run = analyzer.RunAsync(stop.Token);
        try
        {
            string status = logger.TransportMessages.First(m => m.Contains("endpoint=127.0.0.1:"));
            int port = int.Parse(status.Split("endpoint=127.0.0.1:")[1].Split(',')[0]);
            using TcpClient sender = new();
            await sender.ConnectAsync(IPAddress.Loopback, port);
            string body = Header('D', '1', "CONTROL2", "QC", 'C') + "010 5678+";
            byte[] frame = [0x02, .. Encoding.ASCII.GetBytes(body), 0x03];
            await sender.GetStream().WriteAsync(frame);
            string qc = Path.Combine(root, "QualityControl");
            for (int attempt = 0; attempt < 100 && (!Directory.Exists(qc) || Directory.GetFiles(qc, "*.ok").Length == 0); attempt++)
                await Task.Delay(20);
            if (!Directory.Exists(qc))
                throw new InvalidOperationException("QC каталог не создан: " + string.Join(" | ", logger.TransportMessages) + " | " + string.Join(" | ", logger.ProtocolMessages) + " | " + string.Join(" | ", logger.ErrorMessages));
            Equal(1, Directory.GetFiles(qc, "*.res").Length, "routed QC result");
            Equal(false, Directory.Exists(settings.OutputFolder), "routed patient output untouched");
        }
        finally
        {
            await analyzer.StopAsync(CancellationToken.None);
            await run.WaitAsync(TimeSpan.FromSeconds(5));
            string qc = Path.Combine(root, "QualityControl");
            if (Directory.Exists(qc))
            {
                foreach (string file in Directory.GetFiles(qc)) File.Delete(file);
                Directory.Delete(qc);
            }
            if (Directory.Exists(root)) Directory.Delete(root);
        }
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
