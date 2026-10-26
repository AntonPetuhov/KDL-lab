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
    void Start(IPAddress address, int port);

    /// <summary>
    /// Асинхронно ожидает подключение прибора, поскольку блокирующее ожидание заняло бы поток службы.
    /// </summary>
    Task<TcpClient> AcceptAsync(CancellationToken cancellationToken);

    /// <summary>Синхронно отмечает успешное чтение сообщения.</summary>
    void RecordRead();
    /// <summary>Синхронно отмечает успешную запись сообщения.</summary>
    void RecordWrite();
    /// <summary>Синхронно сохраняет ошибку для журнала состояния.</summary>
    /// <param name="exception">Ошибка.</param>
    void RecordError(Exception exception);
    /// <summary>Синхронно закрывает завершённый сеанс, не останавливая listener.</summary>
    /// <param name="connected">Принятый клиент.</param>
    void ReleaseClient(TcpClient connected);

    /// <summary>
    /// Синхронно закрывает активное подключение и listener; сетевые объекты не требуют асинхронного освобождения.
    /// </summary>
    void Stop();
}
