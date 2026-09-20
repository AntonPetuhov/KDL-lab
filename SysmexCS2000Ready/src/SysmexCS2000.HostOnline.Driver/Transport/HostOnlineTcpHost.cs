using System.Net;
using System.Net.Sockets;
using AnalyzerService.Contracts;

namespace SysmexCS2000.HostOnline.Driver.Transport;

/// <summary>Реализует TCP-сервер ЛИС и тексты STX/ETX собственного протокола.</summary>
public sealed class HostOnlineTcpHost(IAnalyzerLogger logger) : IDisposable
{
    private TcpListener? listener;
    private TcpClient? client;

    /// <summary>Синхронно запускает listener.</summary><param name="address">Локальный адрес.</param><param name="port">Порт.</param>
    public void Start(IPAddress address, int port) { listener = new TcpListener(address, port); listener.Start(); logger.Transport($"Host Online TCP запущен на {address}:{port}."); }

    /// <summary>Асинхронно ожидает подключение IPU.</summary><param name="token">Сигнал остановки.</param><returns>Клиент.</returns>
    public async Task<TcpClient> AcceptAsync(CancellationToken token)
    {
        if (listener is null) throw new InvalidOperationException("TCP host не запущен.");
        client = await listener.AcceptTcpClientAsync(token).ConfigureAwait(false);
        client.NoDelay = true;
        return client;
    }

    /// <summary>Синхронно прекращает listener и соединение.</summary>
    public void Stop() { client?.Dispose(); client = null; listener?.Stop(); listener = null; }
    /// <summary>Освобождает TCP-ресурсы.</summary>
    public void Dispose() => Stop();
}
