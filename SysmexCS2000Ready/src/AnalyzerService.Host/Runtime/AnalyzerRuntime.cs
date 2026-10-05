using System.Net;
using System.Net.Sockets;
using AnalyzerService.Contracts;
using AnalyzerService.Host.Drivers;
using AnalyzerService.Host.Logging;
using AnalyzerService.Transport;

namespace AnalyzerService.Host.Runtime;

/// <summary>
/// Владеет TCP listener, подключением прибора и жизненным циклом одной DLL.
/// Драйверу передаётся только IAnalyzerConnection; открыть порт из DLL нельзя.
/// </summary>
public sealed class AnalyzerRuntime(AnalyzerSettings settings, DriverLoader loader, AnalyzerLoggerFactory loggerFactory) : IDisposable
{
    private readonly CancellationTokenSource stop = new();
    private CancellationTokenSource? linkedStop;
    private LoadedDriver? loaded;
    private TcpHost? tcpHost;
    private IAnalyzerLogger? logger;
    private Task? runTask;
    private bool disposed;

    /// <summary>Имя анализатора из JSON для диагностики.</summary>
    public string Name => settings.AnalyzerName;

    /// <summary>Задача сетевого цикла; менеджер наблюдает за её завершением.</summary>
    public Task Completion => runTask ?? Task.CompletedTask;

    /// <summary>
    /// Синхронно загружает DLL, запускает её файловые ресурсы и открывает TCP listener.
    /// Task возвращается для контракта менеджера; ожидания сети здесь нет.
    /// </summary>
    public Task StartAsync(CancellationToken serviceToken)
    {
        if (runTask is not null)
        {
            InvalidOperationException ex = new($"{Name} уже запущен.");
            logger?.Error($"{Name}: повторный запуск отклонён.", ex);
            throw ex;
        }
        logger = loggerFactory.Create(settings);
        linkedStop = CancellationTokenSource.CreateLinkedTokenSource(stop.Token, serviceToken);
        try
        {
            loaded = loader.Load(settings.DllPath!);
            loaded.Instance.Initialize(logger, settings);
            loaded.Instance.Start();
            tcpHost = new TcpHost(logger, Name);
            tcpHost.Start(IPAddress.Parse(settings.IPaddress!), settings.Port);
            runTask = RunConnectionsAsync(linkedStop.Token);
            logger.Service($"{Name}: драйвер и TCP listener запущены.");
        }
        catch (Exception ex)
        {
            logger.Error($"{Name}: исключение при запуске DLL или TCP listener; исключение передаётся менеджеру.", ex);
            try { loaded?.Instance.Stop(); }
            catch (Exception stopError) { logger.Error($"{Name}: ошибка отката запуска драйвера.", stopError); }
            try { tcpHost?.Dispose(); }
            catch (Exception cleanupError) { logger.Error($"{Name}: ошибка освобождения TCP после сбоя запуска.", cleanupError); }
            try { loaded?.Dispose(); }
            catch (Exception cleanupError) { logger.Error($"{Name}: ошибка освобождения DLL после сбоя запуска.", cleanupError); }
            tcpHost = null;
            loaded = null;
            linkedStop.Dispose();
            linkedStop = null;
            throw;
        }
        return Task.CompletedTask;
    }

    /// <summary>
    /// Асинхронно ожидает клиента и передаёт открытый поток DLL. Асинхронность нужна
    /// только для Accept и протокольного чтения/записи; ошибки сеанса логируются с контекстом.
    /// </summary>
    private async Task RunConnectionsAsync(CancellationToken token)
    {
        TcpHost host = tcpHost ?? throw new InvalidOperationException("TCP host не создан.");
        IAnalyzerDriver driver = loaded?.Instance ?? throw new InvalidOperationException("DLL-драйвер не загружен.");
        while (!token.IsCancellationRequested)
        {
            TcpClient? client = null;
            try
            {
                client = await host.AcceptAsync(token).ConfigureAwait(false);
                await driver.HandleConnectionAsync(new TcpAnalyzerConnection(client.GetStream(), host), token)
                    .ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (token.IsCancellationRequested) { break; }
            catch (Exception) when (token.IsCancellationRequested) { break; }
            catch (EndOfStreamException)
            {
                logger?.Transport($"{Name}: прибор закрыл соединение; ожидается новое подключение.");
            }
            catch (Exception ex)
            {
                host.RecordError(ex);
                logger?.Error($"{Name}: исключение обработки TCP-сеанса; клиент будет отключён.", ex);
            }
            finally
            {
                if (client is not null)
                    try { host.ReleaseClient(client); }
                    catch (Exception ex)
                    {
                        logger?.Error($"{Name}: исключение освобождения TCP-клиента; пробрасывается менеджеру.", ex);
                        throw;
                    }
            }
        }
        logger?.Service($"{Name}: TCP-цикл завершён.");
    }

    /// <summary>
    /// Асинхронно ждёт завершения сетевого цикла после синхронного закрытия сокета;
    /// затем останавливает фоновую очередь DLL. Каждое пробрасываемое исключение логируется.
    /// </summary>
    public async Task StopAsync(CancellationToken cancellationToken)
    {
        if (loaded is null) return;
        List<Exception> errors = [];
        try { stop.Cancel(); }
        catch (Exception ex) { logger?.Error($"{Name}: исключение отмены рабочего цикла.", ex); errors.Add(ex); }
        try { tcpHost?.Stop(); }
        catch (Exception ex) { logger?.Error($"{Name}: исключение закрытия TCP host.", ex); errors.Add(ex); }
        try { if (runTask is not null) await runTask.WaitAsync(cancellationToken).ConfigureAwait(false); }
        catch (Exception ex) { logger?.Error($"{Name}: исключение ожидания TCP-цикла.", ex); errors.Add(ex); }
        try { loaded.Instance.Stop(); }
        catch (Exception ex) { logger?.Error($"{Name}: исключение остановки драйвера.", ex); errors.Add(ex); }
        if (errors.Count != 0) throw new AggregateException($"{Name}: ошибки остановки.", errors);
    }

    /// <summary>Синхронно и повторно безопасно освобождает DLL, listener и токены.</summary>
    public void Dispose()
    {
        if (disposed) return;
        disposed = true;
        tcpHost?.Dispose();
        loaded?.Dispose();
        linkedStop?.Dispose();
        stop.Dispose();
    }
}
