using System.Net;
using System.Net.Sockets;
using AnalyzerService.Contracts;

namespace SysmexCS2000.Driver.Transport;

/// <summary>Слушает TCP как host; Sysmex IPU подключается к нему как клиент.</summary>
public sealed class TcpHost(IAnalyzerLogger logger) : IDisposable
{
    private TcpListener? listener;
    private TcpClient? activeClient;

    /// <summary>Синхронно запускает listener на адресе и порту из JSON.</summary><param name="address">Локальный адрес.</param><param name="port">Локальный порт.</param>
    public void Start(IPAddress address, int port)
    {
        listener = new TcpListener(address, port);
        listener.Start();
        logger.Transport($"TCP host запущен на {address}:{port}.");
    }

    /// <summary>Асинхронно ожидает IPU без блокировки потока службы.</summary><param name="token">Сигнал остановки.</param><returns>Подключённый клиент.</returns>
    public async Task<TcpClient> AcceptAsync(CancellationToken token)
    {
        if (listener is null) throw new InvalidOperationException("TCP host не запущен.");
        activeClient = await listener.AcceptTcpClientAsync(token).ConfigureAwait(false);
        activeClient.NoDelay = true;
        logger.Transport($"Подключён IPU: {activeClient.Client.RemoteEndPoint}.");
        return activeClient;
    }

    /// <summary>Синхронно прекращает listener и активное соединение.</summary>
    public void Stop()
    {
        activeClient?.Dispose();
        activeClient = null;
        listener?.Stop();
        listener = null;
    }

    /// <summary>Освобождает сетевые ресурсы.</summary>
    public void Dispose() => Stop();
}
