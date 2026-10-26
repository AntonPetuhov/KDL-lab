using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using AnalyzerService.Contracts;

namespace AnalyzerService.Transport;

/// <summary>
/// Общий TCP-сервер для DLL-драйверов
/// </summary>
public class TcpHost : ITcpHost
{
    private static readonly TimeSpan StatusInterval = TimeSpan.FromSeconds(30); // мониторинг состояния каждые 30 сек
    private readonly IAnalyzerLogger logger;
    private readonly string name;
    private readonly object gate = new();
    private TcpListener? listener;
    private TcpClient? client;
    private Timer? timer;
    private string? endpoint, remote, error;
    private DateTime? lastAccept, lastRead, lastWrite;
    private bool accepting, disposed;

    /// <summary>
    /// Синхронно сохраняет журнал и имя подключения; сокет пока не открывается.
    /// </summary>
    public TcpHost(IAnalyzerLogger logger, string connectionName, TimeSpan? statusInterval = null)
    {
        this.logger = logger ?? throw new ArgumentNullException(nameof(logger));
        name = string.IsNullOrWhiteSpace(connectionName) ? throw new ArgumentException("Имя не задано.", nameof(connectionName)) : connectionName;

        if (statusInterval.HasValue && statusInterval.Value <= TimeSpan.Zero)
            throw new ArgumentOutOfRangeException(nameof(statusInterval));
        interval = statusInterval ?? StatusInterval;
    }

    private readonly TimeSpan interval;

    /// <summary>
    /// Синхронно открывает listener и запускает журнал состояния раз в 30 секунд.
    /// </summary>
    public void Start(IPAddress address, int port)
    {
        ArgumentNullException.ThrowIfNull(address);
        lock (gate)
        {
            // Если объект уже был освобождён (disposed == true), выбрасывается ObjectDisposedException. То есть запускать уже уничтоженный хост нельзя
            ObjectDisposedException.ThrowIf(disposed, this);

            if (listener is not null) 
                throw new InvalidOperationException("TCP host уже запущен.");

            TcpListener startedlistener = new(address, port);
            startedlistener.Start();
            listener = startedlistener;
            endpoint = startedlistener.LocalEndpoint.ToString();

            error = null; // сбрасываем ошибку

            // логгируем состояние хоста
            timer = new Timer(_ => LogStatus(), null, interval, interval);
        }

        LogStatus();
    }

    /// <summary>
    /// Асинхронно ждёт подключения, чтобы не блокировать поток службы
    /// </summary>
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
                lastAccept = DateTime.Now;
            }

            logger.Transport($"{name}: подключён клиент {remote}.");
            return accepted;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            RecordError(ex);
            throw;
        }
        finally 
        { 
            lock (gate) accepting = false; 
        }
    }

    // Отмечаем время успешного чтения сообщения для диагностики.
    public void RecordRead() 
    {
        lock (gate)
        {
            lastRead = DateTime.Now;
        } 
    }

    // Отмечаем время успешной отправки сообщения для диагностики.
    public void RecordWrite() 
    {
        lock (gate) 
        {
            lastWrite = DateTime.Now;
        }
    }

    // Запоминаем последнюю ошибку соединения.
    public void RecordError(Exception exception)
    {
        ArgumentNullException.ThrowIfNull(exception);
        lock (gate) 
        { 
            error = $"{exception.GetType().Name}: {exception.Message}"; 
        }
    }

    /// <summary>
    /// Закрывает завершённый сеанс, сохраняя работающий listener.
    /// </summary>
    public void ReleaseClient(TcpClient connected)
    {
        logger.Transport($"Завершеаем сеанс, Listener продолжает работу.");

        ArgumentNullException.ThrowIfNull(connected);
        bool isListening;
        lock (gate)
        {
            if (ReferenceEquals(client, connected)) 
            { 
                client = null; 
                remote = null; 
            }
            isListening = listener is not null;
        }

        connected.Dispose();
        logger.Transport($"{name}: сеанс клиента завершён; listener {(isListening ? "продолжает работу" : "остановлен")}.");
    }

    /// <summary>
    /// Закрывает сокеты и таймер; повторный вызов безопасен.
    /// </summary>
    public void Stop()
    {
        TcpClient? oldClient; 
        TcpListener? oldListener; 
        Timer? oldTimer;

        lock (gate)
        {
            oldClient = client; 
            oldListener = listener; 
            oldTimer = timer;
            client = null; 
            listener = null; 
            timer = null; 
            remote = null; 
            accepting = false;
        }
        oldTimer?.Dispose(); 
        oldClient?.Dispose(); 
        oldListener?.Stop();

        if (oldListener is not null) 
            logger.Transport($"{name}: TCP host остановлен ({endpoint}).");
    }

    /// <summary> 
    /// Освобождает TCP-ресурсы.
    /// </summary>
    public void Dispose()
    {
        lock (gate) 
        { 
            if (disposed) 
                return; 
            disposed = true; 
        }
        Stop();
    }

    /// <summary>
    /// Пишет снимок наблюдаемого состояния хоста; молчание прибора не считается доказательством отказа.
    /// </summary>
    private void LogStatus()
    {
        string status;
        lock (gate)
        {
            status = $"{name}: listener = {(listener is null ? "остановлен" : "работает")}, endpoint = {endpoint ?? "нет"}, " +
                            $"ожидание Accept = {accepting}, клиент = {remote ?? "нет"}, последний клиент = {Format(lastAccept)}, " +
                            $"RX = {Format(lastRead)}, TX = {Format(lastWrite)}, ошибка = {error ?? "нет"}.";
        }
            
        try 
        { 
            logger.Transport(status); 
        }
        catch (Exception ex) 
        { 
            Trace.TraceError($"Не удалось записать состояние TCP host: {ex}"); 
        }
    }

    // форматирует время события для журнала.
    private static string Format(DateTime? value) => value?.ToString("O") ?? "нет";
}
