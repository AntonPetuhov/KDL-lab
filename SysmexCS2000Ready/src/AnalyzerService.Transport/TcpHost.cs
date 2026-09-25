using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using AnalyzerService.Contracts;

namespace AnalyzerService.Transport;

/// <summary>
/// Общий TCP-сервер для DLL-драйверов
/// </summary>
public sealed class TcpHost : ITcpHost
{
    private static readonly TimeSpan StatusInterval = TimeSpan.FromSeconds(30);
    private readonly IAnalyzerLogger logger;
    private readonly string name;
    private readonly object gate = new();
    private TcpListener? listener;
    private TcpClient? client;
    private Timer? timer;
    private string? endpoint, remote, error;
    private DateTimeOffset? lastAccept, lastRead, lastWrite;
    private bool accepting, disposed;

    /// <summary>Синхронно сохраняет журнал и имя подключения; сокет пока не открывается.</summary>
    /// <param name="logger">Журнал анализатора.</param><param name="connectionName">Имя для диагностики.</param>
    /// <param name="statusInterval">Период снимков состояния; стандартно 30 секунд.</param>
    public TcpHost(IAnalyzerLogger logger, string connectionName, TimeSpan? statusInterval = null)
    {
        this.logger = logger ?? throw new ArgumentNullException(nameof(logger));
        name = string.IsNullOrWhiteSpace(connectionName) ? throw new ArgumentException("Имя не задано.", nameof(connectionName)) : connectionName;
        if (statusInterval.HasValue && statusInterval.Value <= TimeSpan.Zero)
            throw new ArgumentOutOfRangeException(nameof(statusInterval));
        interval = statusInterval ?? StatusInterval;
    }

    private readonly TimeSpan interval;

    /// <summary>Синхронно открывает listener и запускает журнал состояния раз в 30 секунд.</summary>
    /// <param name="address">Локальный IP из JSON.</param><param name="port">Порт из JSON.</param>
    public void Start(IPAddress address, int port)
    {
        ArgumentNullException.ThrowIfNull(address);
        lock (gate)
        {
            ObjectDisposedException.ThrowIf(disposed, this);
            if (listener is not null) throw new InvalidOperationException("TCP host уже запущен.");
            TcpListener started = new(address, port);
            started.Start();
            listener = started;
            endpoint = started.LocalEndpoint.ToString();
            error = null;
            timer = new Timer(_ => LogStatus(), null, interval, interval);
        }
        LogStatus();
    }

    /// <summary>Асинхронно ждёт подключения, чтобы не блокировать поток службы.</summary>
    /// <param name="cancellationToken">Сигнал остановки.</param><returns>Принятый клиент.</returns>
    public async Task<TcpClient> AcceptAsync(CancellationToken cancellationToken)
    {
        TcpListener current;
        lock (gate)
        {
            ObjectDisposedException.ThrowIf(disposed, this);
            current = listener ?? throw new InvalidOperationException("TCP host не запущен.");
            accepting = true;
        }
        try
        {
            TcpClient accepted = await current.AcceptTcpClientAsync(cancellationToken).ConfigureAwait(false);
            accepted.NoDelay = true;
            lock (gate)
            {
                if (disposed || current != listener)
                {
                    accepted.Dispose();
                    throw new OperationCanceledException("TCP host остановлен.", cancellationToken);
                }
                client?.Dispose();
                client = accepted;
                remote = accepted.Client.RemoteEndPoint?.ToString();
                lastAccept = DateTimeOffset.Now;
            }
            logger.Transport($"{name}: подключён клиент {remote}.");
            return accepted;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            RecordError(ex);
            throw;
        }
        finally { lock (gate) accepting = false; }
    }

    /// <summary>Отмечает успешное чтение сообщения для диагностики.</summary>
    public void RecordRead() 
    {
        lock (gate)
        {
            lastRead = DateTimeOffset.Now;
        } 
    }

    /// <summary>Синхронно отмечает успешную отправку сообщения для диагностики.</summary>
    public void RecordWrite() { lock (gate) lastWrite = DateTimeOffset.Now; }

    /// <summary>Синхронно запоминает последнюю ошибку соединения.</summary>
    /// <param name="exception">Ошибка для очередного снимка состояния.</param>
    public void RecordError(Exception exception)
    {
        ArgumentNullException.ThrowIfNull(exception);
        lock (gate) error = $"{exception.GetType().Name}: {exception.Message}";
    }

    /// <summary>Синхронно закрывает завершённый сеанс, сохраняя работающий listener.</summary>
    public void ReleaseClient(TcpClient connected)
    {
        ArgumentNullException.ThrowIfNull(connected);
        bool isListening;
        lock (gate)
        {
            if (ReferenceEquals(client, connected)) { client = null; remote = null; }
            isListening = listener is not null;
        }
        connected.Dispose();
        logger.Transport($"{name}: сеанс клиента завершён; listener {(isListening ? "продолжает работу" : "остановлен")}.");
    }

    /// <summary>Синхронно закрывает сокеты и таймер; повторный вызов безопасен.</summary>
    public void Stop()
    {
        TcpClient? oldClient; TcpListener? oldListener; Timer? oldTimer;
        lock (gate)
        {
            oldClient = client; oldListener = listener; oldTimer = timer;
            client = null; listener = null; timer = null; remote = null; accepting = false;
        }
        oldTimer?.Dispose(); oldClient?.Dispose(); oldListener?.Stop();
        if (oldListener is not null) logger.Transport($"{name}: TCP host остановлен ({endpoint}).");
    }

    /// <summary>Синхронно и идемпотентно освобождает TCP-ресурсы.</summary>
    public void Dispose()
    {
        lock (gate) { if (disposed) return; disposed = true; }
        Stop();
    }

    /// <summary>Пишет снимок наблюдаемого состояния; молчание прибора не считается доказательством отказа.</summary>
    private void LogStatus()
    {
        string status;
        lock (gate)
            status = $"{name}: listener={(listener is null ? "остановлен" : "работает")}, endpoint={endpoint ?? "нет"}, " +
                $"ожидание Accept={accepting}, клиент={remote ?? "нет"}, последний клиент={Format(lastAccept)}, " +
                $"RX={Format(lastRead)}, TX={Format(lastWrite)}, ошибка={error ?? "нет"}.";
        try { logger.Transport(status); }
        catch (Exception ex) { Trace.TraceError($"Не удалось записать состояние TCP host: {ex}"); }
    }

    /// <summary>Синхронно форматирует время события для журнала.</summary>
    /// <param name="value">Время или null.</param><returns>ISO-время либо «нет».</returns>
    private static string Format(DateTimeOffset? value) => value?.ToString("O") ?? "нет";
}
