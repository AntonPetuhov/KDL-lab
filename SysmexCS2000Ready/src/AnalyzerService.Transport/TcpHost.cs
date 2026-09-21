using System.Net;
using System.Net.Sockets;
using AnalyzerService.Contracts;

namespace AnalyzerService.Transport;

/// <summary>
/// Общая реализация TCP-сервера для подключаемых DLL анализаторов, работающих в роли TCP-клиента.
/// Управляет только транспортом и не зависит от ASTM, Sysmex Host Online или другого прикладного протокола.
/// </summary>
public sealed class TcpHost : ITcpHost
{
    private readonly IAnalyzerLogger logger;
    private readonly string connectionName;
    private readonly object stateLock = new();
    private TcpListener? listener;
    private TcpClient? activeClient;
    private bool disposed;

    /// <summary>
    /// Создаёт общий TCP-host. Конструктор синхронный, поскольку только сохраняет зависимости.
    /// </summary>
    /// <param name="logger">Логгер драйвера, предоставленный службой.</param>
    /// <param name="connectionName">Понятное имя прибора или протокола для сообщений журнала.</param>
    /// <exception cref="ArgumentNullException"><paramref name="logger"/> равен <see langword="null"/>.</exception>
    /// <exception cref="ArgumentException"><paramref name="connectionName"/> пуст.</exception>
    public TcpHost(IAnalyzerLogger logger, string connectionName)
    {
        this.logger = logger ?? throw new ArgumentNullException(nameof(logger));
        this.connectionName = string.IsNullOrWhiteSpace(connectionName)
            ? throw new ArgumentException("Имя подключения не задано.", nameof(connectionName))
            : connectionName;
    }

    /// <inheritdoc />
    public void Start(IPAddress address, int port)
    {
        ArgumentNullException.ThrowIfNull(address);
        lock (stateLock)
        {
            ObjectDisposedException.ThrowIf(disposed, this);
            if (listener is not null) throw new InvalidOperationException("TCP host уже запущен.");

            TcpListener newListener = new(address, port);
            newListener.Start();
            listener = newListener;
        }

        logger.Transport($"{connectionName}: TCP host запущен на {address}:{port}.");
    }

    /// <inheritdoc />
    public async Task<TcpClient> AcceptAsync(CancellationToken cancellationToken)
    {
        TcpListener currentListener;
        lock (stateLock)
        {
            ObjectDisposedException.ThrowIf(disposed, this);
            currentListener = listener ?? throw new InvalidOperationException("TCP host не запущен.");
        }

        TcpClient connected = await currentListener.AcceptTcpClientAsync(cancellationToken).ConfigureAwait(false);
        connected.NoDelay = true;

        lock (stateLock)
        {
            if (disposed || listener != currentListener)
            {
                connected.Dispose();
                throw new OperationCanceledException("TCP host остановлен.", cancellationToken);
            }

            activeClient?.Dispose();
            activeClient = connected;
        }

        logger.Transport($"{connectionName}: подключён клиент {connected.Client.RemoteEndPoint}.");
        return connected;
    }

    /// <inheritdoc />
    public void Stop()
    {
        TcpClient? clientToDispose;
        TcpListener? listenerToStop;
        lock (stateLock)
        {
            clientToDispose = activeClient;
            listenerToStop = listener;
            activeClient = null;
            listener = null;
        }

        clientToDispose?.Dispose();
        listenerToStop?.Stop();
        if (clientToDispose is not null || listenerToStop is not null)
            logger.Transport($"{connectionName}: TCP host остановлен.");
    }

    /// <summary>
    /// Синхронно и идемпотентно освобождает listener и активный сокет.
    /// </summary>
    public void Dispose()
    {
        lock (stateLock)
        {
            if (disposed) return;
            disposed = true;
        }

        Stop();
    }
}
