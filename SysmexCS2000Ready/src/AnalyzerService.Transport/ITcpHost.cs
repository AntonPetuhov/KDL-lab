using System.Net;
using System.Net.Sockets;

namespace AnalyzerService.Transport;

/// <summary>
/// Определяет общий жизненный цикл TCP-сервера, к которому анализатор подключается как клиент.
/// Реализации протоколов используют этот контракт и не создают собственные <see cref="TcpListener"/>.
/// </summary>
public interface ITcpHost : IDisposable
{
    /// <summary>
    /// Синхронно открывает локальный endpoint до запуска рабочего цикла драйвера.
    /// </summary>
    /// <param name="address">Адрес локального сетевого интерфейса из JSON-конфигурации.</param>
    /// <param name="port">Порт из JSON-конфигурации.</param>
    /// <exception cref="InvalidOperationException">Сервер уже запущен.</exception>
    /// <exception cref="SocketException">Не удалось привязать адрес или открыть порт.</exception>
    void Start(IPAddress address, int port);

    /// <summary>
    /// Асинхронно ожидает подключение прибора, поскольку блокирующее ожидание заняло бы поток службы.
    /// </summary>
    /// <param name="cancellationToken">Сигнал остановки службы или драйвера.</param>
    /// <returns>Подключённый TCP-клиент, которым владеет сервер до следующего подключения или остановки.</returns>
    /// <exception cref="InvalidOperationException">Сервер не запущен.</exception>
    /// <exception cref="OperationCanceledException">Ожидание отменено.</exception>
    Task<TcpClient> AcceptAsync(CancellationToken cancellationToken);

    /// <summary>
    /// Синхронно закрывает активное подключение и listener; сетевые объекты не требуют асинхронного освобождения.
    /// </summary>
    void Stop();
}
