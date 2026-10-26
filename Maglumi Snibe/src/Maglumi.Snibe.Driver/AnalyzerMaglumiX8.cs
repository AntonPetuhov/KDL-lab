using System.Net;
using System.Net.Sockets;
using AnalyzerService.Contracts;
using AnalyzerService.LisDatabase;
using AnalyzerService.ResultFiles;
using AnalyzerService.Transport;
using Maglumi.Snibe.Driver.Lis;
using Maglumi.Snibe.Driver.Protocol;

namespace Maglumi.Snibe.Driver;

/// <summary>Владеет TCP listener, сеансом HL7, запросами ЛИС и общей очередью файлов.</summary>
public sealed class AnalyzerMaglumiX8 : IDisposable
{
    private readonly IAnalyzerLogger logger;
    private readonly AnalyzerSettings settings;
    private readonly Func<string, LisOrder?> getOrder;
    private readonly RawResultQueue queue;
    private readonly CancellationTokenSource stopping = new();
    private TcpHost? host;
    private Task? runTask;
    private bool disposed;

    public AnalyzerMaglumiX8(IAnalyzerLogger logger, AnalyzerSettings settings)
        : this(logger, settings, new LisDBProvider(settings, logger)) { }

    private AnalyzerMaglumiX8(IAnalyzerLogger logger, AnalyzerSettings settings, LisDBProvider repository)
        : this(logger, settings, repository.GetOrder, repository.TranslateResultCode) { }

    /// <summary>Позволяет проверять полный TCP/файловый цикл без изменения общего LisDBProvider.</summary>
    public AnalyzerMaglumiX8(IAnalyzerLogger logger, AnalyzerSettings settings,
        Func<string, LisOrder?> getOrder, Func<string, string?> translate)
    {
        this.logger = logger;
        this.settings = settings;
        this.getOrder = getOrder;
        X8ResultHandler handler = new(settings, logger, translate);
        queue = new(settings, logger, (raw, source) =>
            handler.Handle(X8ResultCodec.Parse(Hl7Message.Parse(MllpCodec.Decode(raw))), source));
    }

    public Task RunAsync(CancellationToken token)
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        if (runTask is not null) throw new InvalidOperationException("Рабочий цикл уже запущен.");
        stopping.Token.ThrowIfCancellationRequested();
        if (settings.ResultHandlerStatus) queue.Start();
        TcpHost started = new(logger, settings.AnalyzerName);
        try
        {
            started.Start(IPAddress.Parse(settings.IPaddress!), settings.Port);
            host = started;
            runTask = RunConnectionsAsync(started, token);
            return runTask;
        }
        catch
        {
            started.Dispose();
            queue.Stop();
            throw;
        }
    }

    private async Task RunConnectionsAsync(TcpHost tcpHost, CancellationToken token)
    {
        using CancellationTokenSource linked = CancellationTokenSource.CreateLinkedTokenSource(token, stopping.Token);
        using CancellationTokenRegistration registration = linked.Token.Register(tcpHost.Stop);
        try
        {
            while (!linked.IsCancellationRequested)
            {
                using TcpClient client = await tcpHost.AcceptAsync(linked.Token).ConfigureAwait(false);
                try { await RunSessionAsync(new TcpAnalyzerConnection(client.GetStream(), tcpHost), linked.Token).ConfigureAwait(false); }
                catch (OperationCanceledException) when (linked.IsCancellationRequested) { break; }
                catch (Exception ex) when (!linked.IsCancellationRequested)
                {
                    tcpHost.RecordError(ex);
                    logger.Error("MAGLUMI X8: сеанс завершён с ошибкой; ожидается переподключение.", ex);
                }
                finally { tcpHost.ReleaseClient(client); }
            }
        }
        catch (Exception) when (linked.IsCancellationRequested) { }
        finally
        {
            tcpHost.Stop();
            queue.Stop();
            logger.Service("MAGLUMI X8: рабочий цикл завершён.");
        }
    }

    private async Task RunSessionAsync(ITcpConnection connection, CancellationToken token)
    {
        MllpCodec codec = new();
        byte[] buffer = new byte[8192];
        while (!token.IsCancellationRequested)
        {
            // Простой TCP без сообщений может оставаться открытым. Таймаут только для начатого кадра.
            using CancellationTokenSource readTimeout = CancellationTokenSource.CreateLinkedTokenSource(token);
            if (codec.HasIncompleteFrame) readTimeout.CancelAfter(TimeSpan.FromSeconds(30));
            int count;
            try { count = await connection.Stream.ReadAsync(buffer, readTimeout.Token).ConfigureAwait(false); }
            catch (OperationCanceledException) when (!token.IsCancellationRequested)
            { throw new InvalidDataException("X8: таймаут незавершённого MLLP-кадра (30 секунд)."); }
            if (count == 0)
            {
                if (codec.HasIncompleteFrame) throw new InvalidDataException("X8 закрыл соединение с незавершённым MLLP-кадром.");
                return;
            }
            connection.RecordRead();
            foreach (byte[] raw in codec.Feed(buffer, count))
                await HandleFrameAsync(connection, raw, token).ConfigureAwait(false);
        }
    }

    private async Task HandleFrameAsync(ITcpConnection connection, byte[] raw, CancellationToken token)
    {
        IReadOnlyList<Hl7Message> messages;
        try
        {
            string text = MllpCodec.Decode(raw);
            logger.Protocol("RX: <VT>" + text.Replace("\r", "<CR>") + "<FS><CR>");
            messages = Hl7Message.Parse(text);
        }
        catch (Exception ex) when (ex is InvalidDataException or System.Text.DecoderFallbackException)
        {
            // Полный, но непонятный кадр сохраняем для разбора, а не теряем вместе с TCP-сеансом.
            queue.SaveResult("INVALID_HL7", raw);
            logger.Error("X8: некорректный HL7 сохранён в очереди для диагностики.", ex);
            return;
        }
        bool hasResults = messages.Any(m => m.Type == "OUL^R22");
        if (hasResults)
        {
            var specimens = messages.SelectMany(m => m.Segments).Where(s => s.Name == "SPM").ToArray();
            bool hasQc = specimens.Any(s => s.Field(11) == "Q");
            bool onlyQc = specimens.Length != 0 && specimens.All(s => s.Field(11) == "Q") && messages.All(m => m.Type == "OUL^R22");
            string Identifier(string role, int component, string fallback)
            {
                string encoded = specimens.FirstOrDefault(s => s.Field(11) == role)?.Component(2, component) ?? "";
                try { return string.IsNullOrWhiteSpace(encoded) ? fallback : Hl7Text.Unescape(encoded); }
                catch (InvalidDataException) { return fallback; }
            }
            if (hasQc) queue.SaveQualityControl(Identifier("Q", 3, "X8_QC"), raw);
            if (!onlyQc) queue.SaveResult(Identifier("P", 1, "X8_RESULT"), raw);
            // Квитанции X8 для OUL^R22 не предусмотрены (MSH-15/16=NE).
            return;
        }
        foreach (Hl7Message message in messages)
        {
            if (message.Type != "TSREQ")
            {
                logger.Protocol($"X8: тип '{message.Type}' не поддерживается входным интерфейсом драйвера.");
                continue;
            }
            try
            {
                X8Query query = X8OrderCodec.ParseQuery(message);
                // SQL синхронен в общей библиотеке; ошибка не подменяется ответом «заказа нет».
                LisOrder? order = getOrder(query.SampleId);
                string response = X8OrderCodec.BuildResponse(query, order, DateTime.Now);
                byte[] frame = MllpCodec.Encode(response);
                await connection.Stream.WriteAsync(frame, token).ConfigureAwait(false);
                await connection.Stream.FlushAsync(token).ConfigureAwait(false);
                connection.RecordWrite();
                logger.Protocol("TX: <VT>" + response.Replace("\r", "<CR>") + "<FS><CR>");
            }
            catch (Exception ex) when (ex is not OperationCanceledException && ex is not IOException)
            { logger.Error("X8: задание не отправлено из-за ошибки запроса/БД; повторите запрос после исправления.", ex); }
        }
    }

    public void Stop()
    {
        if (disposed) return;
        stopping.Cancel();
        host?.Stop();
    }

    public void Dispose()
    {
        if (disposed) return;
        Stop();
        // Host по контракту ожидает RunAsync перед Dispose.
        host?.Dispose();
        queue.Dispose();
        stopping.Dispose();
        disposed = true;
    }
}
