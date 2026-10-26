using System.Collections.Concurrent;
using System.Net;
using System.Net.Sockets;
using System.Reflection;
using System.Runtime.Loader;
using System.Text;
using AnalyzerService.Contracts;
using AnalyzerService.Host.Drivers;
using AnalyzerService.LisDatabase;
using Maglumi.Snibe.Driver;
using Maglumi.Snibe.Driver.Lis;
using Maglumi.Snibe.Driver.Protocol;

internal static class Program
{
    private static int assertions;
    private const string End = "20191021212250";
    private static readonly DateTime Now = new(2026, 10, 26, 12, 0, 0);

    private static async Task<int> Main()
    {
        try
        {
            Framing();
            Orders();
            Results();
            SharedLibraries();
            await TcpAndFiles();
            await StopAndCancellation();
            Console.WriteLine($"All MAGLUMI X8 checks passed ({assertions} assertions).");
            return 0;
        }
        catch (Exception ex) { Console.Error.WriteLine(ex); return 1; }
    }

    private static string Header(string type = "OUL^R22") =>
        $"MSH|^~\\&|X8||LIS||20191025105016||{type}|201910251050161|P|2.5|||NE|NE||UTF-8\r";

    private static string Patient(string id = "20251122553", string role = "P") =>
        Header() + Hl7Text.Segment("SPM", (1, "1"), (2, id + "^1556^1001^N0002^2"), (4, "1"), (11, role)) +
        "ORC|NW||||||0\r";

    private static string Item(string code = "FT3", string value = "3.5600^+", string flag = "N", string status = "F", int n = 1) =>
        Hl7Text.Segment("OBR", (1, n.ToString()), (4, code)) +
        Hl7Text.Segment("OBX", (1, n.ToString()), (3, code), (5, value), (6, "pg/mL"), (8, flag),
            (11, status), (14, "20191021112250"), (18, "Module1^X8"), (19, End)) + $"NTE|{n}\r";

    private static string Query(string id = "20251122553", string priority = "0") =>
        Header("TSREQ") + $"QPD|TSREQ||{id}^1556^N0002^2|{priority}\rRCP|I||R\r";

    private static void Framing()
    {
        byte[] raw = MllpCodec.Encode(Patient() + Item());
        MllpCodec codec = new();
        List<byte[]> decoded = [];
        foreach (byte value in raw) decoded.AddRange(codec.Feed([value], 1));
        Equal(1, decoded.Count, "one-byte fragmentation");
        Check(raw.SequenceEqual(decoded[0]), "exact MLLP bytes");
        Equal(false, codec.HasIncompleteFrame, "parser reset");
        byte[] joined = [.. raw, .. raw];
        Equal(2, codec.Feed(joined, joined.Length).Count(), "coalesced frames");
        Throws(() => new MllpCodec().Feed([0x02], 1).ToArray(), "invalid start");
        Throws(() => new MllpCodec().Feed([0x0B, 0x1C, 0x0A], 3).ToArray(), "invalid end");
        Throws(() => new MllpCodec(8).Feed(MllpCodec.Encode("123456789"), 12).ToArray(), "size limit");
        Throws(() => MllpCodec.Decode([0x0B, 0xC0, 0xFF, 0x1C, 0x0D]), "invalid UTF-8");
        Throws(() => Hl7Message.Parse(Header().Replace("2.5", "2.3")), "version mismatch");
        Throws(() => Hl7Message.Parse(Header().TrimEnd('\r')), "missing CR");
        Equal(2, Hl7Message.Parse(Header() + Header()).Count, "multiple MSH");
        Equal("Иванов|A^B\\C&~", Hl7Text.Unescape(Hl7Text.Escape("Иванов|A^B\\C&~")), "escaping");
        Throws(() => Hl7Text.Unescape("abc\\X00\\"), "unknown escape");
        // Валидный кадр не должен теряться, если за ним пришли повреждённые байты.
        int accepted = 0;
        Throws(() => { foreach (byte[] frame in new MllpCodec().Feed([.. raw, 0xFF], raw.Length + 1)) accepted++; }, "bad trailing bytes");
        Equal(1, accepted, "preceding frame delivered");
    }

    private static LisOrder Order => new("20251122553", "10011012", "Иванов|Тест", "Иван", "19960222", "M", ["FT3", "TG", "FT3"]);

    private static void Orders()
    {
        X8Query query = X8OrderCodec.ParseQuery(Hl7Message.Parse(Query()).Single());
        Equal("N0002", query.Rack, "rack from QPD");
        Hl7Message response = Hl7Message.Parse(X8OrderCodec.BuildResponse(query, Order, Now)).Single();
        Equal("OML^O33", response.Type, "order message");
        Equal("20251122553^1556^^N0002^2", response.Single("SPM").Field(2), "QPD to SPM positions");
        Equal("1", response.Single("PID").Field(8), "male");
        Equal("30^1", response.Single("PID").Field(38), "age");
        Equal("Иванов|Тест Иван", Hl7Text.Unescape(response.Single("PID").Field(5)), "UTF-8 patient escaping");
        Equal(2, response.Segments.Count(s => s.Name == "OBR"), "unique assays");
        Equal("0", response.Single("ORC").Field(7), "regular priority");
        Check(response.Header.Field(10).Length <= 20, "control ID length");
        Equal("2", Hl7Message.Parse(X8OrderCodec.BuildResponse(query, Order with { Sex = "F" }, Now)).Single().Single("PID").Field(8), "female");
        foreach (LisOrder? order in new LisOrder?[] { null, Order with { Parameters = [] } })
        {
            Hl7Message empty = Hl7Message.Parse(X8OrderCodec.BuildResponse(query, order, Now)).Single();
            Equal("MSH,SPM", string.Join(',', empty.Segments.Select(s => s.Name)), "no order");
            Equal("20251122553^1556^^N0002^2", empty.Single("SPM").Field(2), "empty response identity");
        }
        Throws(() => X8OrderCodec.ParseQuery(Hl7Message.Parse(Query("")).Single()), "no barcode");
        Throws(() => X8OrderCodec.ParseQuery(Hl7Message.Parse(Query(priority: "2")).Single()), "ambiguous example priority rejected");
        Throws(() => X8OrderCodec.ParseQuery(Hl7Message.Parse(Query().Replace("1556^N0002^2", "123456^N0002^2")).Single()), "sequence length");
        Throws(() => X8OrderCodec.BuildResponse(query, Order with { SampleId = "OTHER" }, Now), "sample mismatch");
        Throws(() => X8OrderCodec.BuildResponse(query, Order with { Parameters = ["TOO_LONG_CODE"] }, Now), "assay length");
    }

    private static void Results()
    {
        var parsed = X8ResultCodec.Parse(Hl7Message.Parse(Patient() + Item() + Item("TG", "20^", "L", n: 2)));
        Equal(2, parsed[0].Items.Count, "multiple results");
        Equal("+", parsed[0].Items[0].Qualitative, "qualitative component retained");
        Equal("3,5600", X8ResultHandler.FormatValue(parsed[0].Items[0].Quantitative), "precision retained");
        Equal("-0,001", X8ResultHandler.FormatValue("-0.001"), "negative small number");
        Equal("0", X8ResultHandler.FormatValue("0"), "zero retained");
        foreach (string value in new[] { "", "<0.1", ">100", "POS", "NaN", "1.2.3", "1|R", "1\n", "١٢" })
            Throws(() => X8ResultHandler.FormatValue(value), "unsupported result " + value);
        Throws(() => X8ResultCodec.Parse(Hl7Message.Parse(Patient() + Item().Replace("OBX|1||FT3", "OBX|1||TG"))), "manual example OBR/OBX mismatch");
        Throws(() => X8ResultCodec.Parse(Hl7Message.Parse(Patient() + Item() + "OBR|2|||TG\r")), "missing OBX");
        Equal(true, X8ResultCodec.Parse(Hl7Message.Parse(Patient("Bole", "Q") + Item()))[0].QualityControl, "QC role");
        Equal("1001", X8ResultCodec.Parse(Hl7Message.Parse(Patient("Bole", "Q") + Item()))[0].SampleId, "QC material ID");
        Equal(2, X8ResultCodec.Parse(Hl7Message.Parse(Patient() + Item() + Patient("222") + Item())).Count, "multiple specimens/messages");
        using TestDirectory directory = new();
        var settings = Settings(directory.Path);
        X8ResultHandler handler = new(settings, new TestLogger(), code => code == "FT3" ? "100" : "200");
        handler.Handle(parsed, "original");
        string file = System.IO.Path.Combine(settings.OutputFolder!, "original_0001.res");
        string content = File.ReadAllText(file, Encoding.GetEncoding(1251));
        Check(content.Contains("|3,5600|||N||F||MAGLUMI^||" + End + "|X8"), "LIS layout/value/status/time");
        Check(content.Contains("|20|||L||F||"), "abnormal flag retained");
        Check(content.Split("\r\n").Length == 4, "CRLF output");
        handler.Handle(parsed, "original");
        Equal(1, Directory.GetFiles(settings.OutputFolder!, "*.ok").Length, "retry idempotence");
        foreach (string invalid in new[] { Item(status: "P"), Item(value: "^+"), Item(flag: "ERROR"),
            Item() + "NTE|1||*^^Analyzer fault\r", Item() + "NTE|1||*^^Analyzer fault\rNTE|1\r" })
            Throws(() => handler.Handle(X8ResultCodec.Parse(Hl7Message.Parse(Patient() + invalid)), "invalid"), "invalid result to errors");
        X8ResultHandler unmapped = new(settings, new TestLogger(), _ => null);
        Throws(() => unmapped.Handle(parsed, "unmapped"), "missing PSMV2 mapping");
        Check(!File.Exists(System.IO.Path.Combine(settings.OutputFolder!, "unmapped_0001.ok")), "no ok on mapping failure");
        handler.Handle(X8ResultCodec.Parse(Hl7Message.Parse(Patient("Bole", "Q") + Item())), "qc");
        Equal(1, Directory.GetFiles(settings.OutputFolder!, "*.ok").Length, "QC excluded from LIS");
    }

    private static void SharedLibraries()
    {
        using var loaded = new DriverLoader().Load(typeof(MaglumiSnibeDriver).Assembly.Location);
        AssemblyLoadContext context = AssemblyLoadContext.GetLoadContext(loaded.Instance.GetType().Assembly)!;
        Check(context != AssemblyLoadContext.Default, "isolated DLL context");
        foreach (string name in new[] { "Contracts", "Transport", "LisDatabase", "ResultFiles" })
        {
            AssemblyName identity = new("AnalyzerService." + name);
            Check(ReferenceEquals(context.LoadFromAssemblyName(identity), AssemblyLoadContext.Default.LoadFromAssemblyName(identity)), "shared identity " + name);
        }
        using TestDirectory directory = new();
        using MaglumiSnibeDriver driver = new();
        Throws(() => driver.RunAsync(CancellationToken.None), "run before initialize");
        AnalyzerSettings settings = Settings(directory.Path);
        settings.Protocol = "ASTM";
        Throws(() => driver.Initialize(new TestLogger(), settings), "wrong protocol");
        settings.Protocol = "MAGLUMI_X8_HL7";
        driver.Initialize(new TestLogger(), settings);
        Throws(() => driver.Initialize(new TestLogger(), settings), "duplicate initialize");
    }

    private static async Task TcpAndFiles()
    {
        using TestDirectory directory = new();
        AnalyzerSettings settings = Settings(directory.Path);
        TestLogger logger = new();
        using AnalyzerMaglumiX8 analyzer = new(logger, settings,
            id => id == "DB_ERROR" ? throw new InvalidOperationException("database unavailable") : id == Order.SampleId ? Order : null,
            code => code == "FT3" ? "100" : null);
        // Проверяем восстановление очереди после предыдущего запуска.
        Directory.CreateDirectory(settings.ResultsFolder);
        File.WriteAllBytes(System.IO.Path.Combine(settings.ResultsFolder, "restart.raw"), MllpCodec.Encode(Patient() + Item()));
        Task running = analyzer.RunAsync(CancellationToken.None);
        using TcpClient client = new();
        await client.ConnectAsync(IPAddress.Loopback, settings.Port);
        NetworkStream stream = client.GetStream();
        byte[] request = MllpCodec.Encode(Query());
        foreach (byte value in request) await stream.WriteAsync(new byte[] { value });
        Hl7Message response = Hl7Message.Parse(MllpCodec.Decode(await ReadFrame(stream))).Single();
        Equal("OML^O33", response.Type, "TCP query response");
        Equal(2, response.Segments.Count(s => s.Name == "OBR"), "TCP ordered assays");
        await stream.WriteAsync(MllpCodec.Encode(Query("NOT_FOUND")));
        Equal(2, Hl7Message.Parse(MllpCodec.Decode(await ReadFrame(stream))).Single().Segments.Count, "TCP no order");
        await stream.WriteAsync(MllpCodec.Encode(Query("DB_ERROR")));
        await WaitFor(() => logger.Errors.Any(x => x.Contains("database unavailable")));
        await NoReply(stream, "DB error must not produce empty order");
        byte[] raw = MllpCodec.Encode(Patient() + Item());
        byte[] qc = MllpCodec.Encode(Patient("Bole", "Q") + Item());
        byte[] bad = MllpCodec.Encode(Patient() + Item("UNKNOWN"));
        byte[] invalid = MllpCodec.Encode("INVALID\r");
        await stream.WriteAsync(new byte[] { }.Concat(raw).Concat(qc).Concat(bad).Concat(invalid).ToArray());
        await WaitFor(() => Directory.Exists(System.IO.Path.Combine(settings.ResultsFolder, "archive")) &&
            Directory.GetFiles(System.IO.Path.Combine(settings.ResultsFolder, "archive"), "*.raw").Length == 2 &&
            Directory.GetFiles(System.IO.Path.Combine(settings.ResultsFolder, "errors"), "*.raw").Length == 2);
        Check(Directory.GetFiles(System.IO.Path.Combine(settings.ResultsFolder, "QualityControl"), "*.raw").Any(p => File.ReadAllBytes(p).SequenceEqual(qc)), "exact QC persistence");
        Check(Directory.GetFiles(System.IO.Path.Combine(settings.ResultsFolder, "archive"), "*.raw").Any(p => File.ReadAllBytes(p).SequenceEqual(raw)), "exact patient persistence");
        Equal(2, Directory.GetFiles(settings.OutputFolder!, "*.ok").Length, "queue creates LIS files including restart");
        await NoReply(stream, "no ACK for OUL results");
        client.Dispose();
        using (TcpClient reconnect = new())
        {
            await reconnect.ConnectAsync(IPAddress.Loopback, settings.Port);
            await reconnect.GetStream().WriteAsync(MllpCodec.Encode(Query()));
            Equal("OML^O33", Hl7Message.Parse(MllpCodec.Decode(await ReadFrame(reconnect.GetStream()))).Single().Type, "reconnection");
        }
        analyzer.Stop();
        await running.WaitAsync(TimeSpan.FromSeconds(5));
        Check(running.IsCompletedSuccessfully, "Stop unblocks listener");
    }

    private static async Task StopAndCancellation()
    {
        foreach (bool partial in new[] { false, true })
        {
            using TestDirectory directory = new();
            AnalyzerSettings settings = Settings(directory.Path);
            settings.ResultHandlerStatus = false;
            using AnalyzerMaglumiX8 analyzer = new(new TestLogger(), settings, _ => null, _ => null);
            using CancellationTokenSource cancel = new();
            Task running = analyzer.RunAsync(cancel.Token);
            using TcpClient client = new();
            await client.ConnectAsync(IPAddress.Loopback, settings.Port);
            if (partial) await client.GetStream().WriteAsync(new byte[] { 0x0B, (byte)'M' });
            cancel.Cancel();
            await running.WaitAsync(TimeSpan.FromSeconds(5));
            Check(running.IsCompletedSuccessfully, "cancellation with " + (partial ? "partial" : "idle") + " client");
        }
    }

    private static async Task<byte[]> ReadFrame(NetworkStream stream)
    {
        using CancellationTokenSource timeout = new(TimeSpan.FromSeconds(5));
        MllpCodec codec = new();
        byte[] one = new byte[1];
        while (await stream.ReadAsync(one, timeout.Token) != 0)
        {
            byte[]? result = codec.Feed(one, 1).SingleOrDefault();
            if (result is not null) return result;
        }
        throw new Exception("Unexpected TCP EOF");
    }

    private static async Task NoReply(NetworkStream stream, string label)
    {
        using CancellationTokenSource timeout = new(TimeSpan.FromMilliseconds(150));
        try { int count = await stream.ReadAsync(new byte[1], timeout.Token); throw new Exception(label + " (bytes=" + count + ")"); }
        catch (OperationCanceledException) { assertions++; }
    }

    private static async Task WaitFor(Func<bool> predicate)
    {
        DateTime deadline = DateTime.UtcNow.AddSeconds(10);
        while (!predicate())
        {
            if (DateTime.UtcNow > deadline) throw new Exception("Condition timed out.");
            await Task.Delay(25);
        }
    }

    private static AnalyzerSettings Settings(string root)
    {
        TcpListener probe = new(IPAddress.Loopback, 0);
        probe.Start();
        int port = ((IPEndPoint)probe.LocalEndpoint).Port;
        probe.Stop();
        return new()
        {
            AnalyzerName = "X8Test", ConnectionType = "TCPIP", IPaddress = "127.0.0.1", Port = port,
            Protocol = "MAGLUMI_X8_HL7", ResultsFolder = System.IO.Path.Combine(root, "Results"),
            OutputFolder = System.IO.Path.Combine(root, "Output"), ConnectionString = "NotUsedByTests",
            AnalyzerCode = "X8", AnalyzerConfigurationCode = "MAGLUMI", ResultHandlerStatus = true
        };
    }

    private static void Check(bool condition, string label)
    { assertions++; if (!condition) throw new Exception("Assertion failed: " + label); }
    private static void Equal<T>(T expected, T actual, string label) => Check(EqualityComparer<T>.Default.Equals(expected, actual), $"{label}: expected {expected}, got {actual}");
    private static void Throws(Action action, string label)
    {
        try { action(); }
        catch (Exception ex) when (ex is InvalidDataException or DecoderFallbackException or InvalidOperationException) { assertions++; return; }
        throw new Exception("Expected failure: " + label);
    }

    private sealed class TestLogger : IAnalyzerLogger
    {
        public ConcurrentQueue<string> Errors { get; } = new();
        public void Service(string message) { }
        public void Transport(string message) { }
        public void Protocol(string message) { }
        public void Result(string message) { }
        public void Error(string message, Exception exception) => Errors.Enqueue(message + " " + exception);
    }

    private sealed class TestDirectory : IDisposable
    {
        public string Path { get; } = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "MaglumiX8-" + Guid.NewGuid().ToString("N"));
        public TestDirectory() => Directory.CreateDirectory(Path);
        public void Dispose() => Directory.Delete(Path, recursive: true);
    }
}
