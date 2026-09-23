using System.Net;
using System.Net.Sockets;
using AnalyzerService.Contracts;

namespace AnalyzerService.Transport;

/// <summary>
/// Общая реализация TCP-сервера для подключаемых DLL анализаторов, работающих в роли TCP-клиента.
/// Управляет только транспортом и не зависит от протокола.
/// </summary>
public sealed class TcpHost : ITcpHost
{
    private readonly IAnalyzerLogger logger;
    private readonly string connectionName;
    private readonly object stateLock = new();
    private TcpListener? listener; // общее состояние, которое видят все потоки
    private TcpClient? activeClient;
    private bool disposed;

    /// <summary>
    /// Создаёт общий TCP-host. Конструктор синхронный, поскольку только сохраняет зависимости.
    /// </summary>
    public TcpHost(IAnalyzerLogger logger, string connectionName)
    {
        this.logger = logger ?? throw new ArgumentNullException(nameof(logger));
        this.connectionName = string.IsNullOrWhiteSpace(connectionName)
            ? throw new ArgumentException("Имя подключения не задано.", nameof(connectionName))
            : connectionName;
    }

    /// <summary>
    /// Запуск TCP сервера
    /// </summary>
    public void Start(IPAddress address, int port)
    {
        ArgumentNullException.ThrowIfNull(address);
        lock (stateLock)
        {
            ObjectDisposedException.ThrowIf(disposed, this);
            if (listener is not null) 
                throw new InvalidOperationException("TCP host уже запущен.");

            TcpListener newListener = new(address, port);
            newListener.Start();
            listener = newListener;

            // не стоило использовать listener т.к. на момент запуска listener был бы не null, но Start выдал бы исключение (например порт мог быть занят)
            // Следующий вызов Start() увидит listener is not null и скажет «уже запущен». Запуск больше невозможен, хотя на деле ничего не работает.
        }

        logger.Transport($"{connectionName}: TCP host запущен на {address}:{port}. Ожидание подключений...");
    }


    /// <summary>
    /// получает подключения клиента (прибора), если tcp сервер запущен
    /// </summary>
    public async Task<TcpClient> AcceptAsync(CancellationToken cancellationToken)
    {
        TcpListener currentListener;
        lock (stateLock)
        {
            ObjectDisposedException.ThrowIf(disposed, this);

            //if(listener is null)
            //    throw new InvalidOperationException("TCP host не запущен.");
            currentListener = listener ?? throw new InvalidOperationException("TCP host не запущен.");
        }

        //TcpClient connected = await currentListener.AcceptTcpClientAsync(cancellationToken).ConfigureAwait(false);
        TcpClient connectedClient = await currentListener.AcceptTcpClientAsync(cancellationToken);
        connectedClient.NoDelay = true; // нужно ли?????

        lock (stateLock)
        {
            // После await нужно убедиться, что мы всё ещё работаем с тем же listener'ом, поэтому сравниваем через локальную переменную
            // в теории хост могли остановить и быстро заново запустить, тогда listener уже будет указывать на новый объект
            if (disposed || listener != currentListener)
            {
                connectedClient.Dispose();
                throw new OperationCanceledException("TCP host остановлен.", cancellationToken);
            }

            activeClient?.Dispose();
            activeClient = connectedClient;
        }

        logger.Transport($"{connectionName}: подключён клиент {connectedClient.Client.RemoteEndPoint}.");
        return connectedClient;
    }

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
