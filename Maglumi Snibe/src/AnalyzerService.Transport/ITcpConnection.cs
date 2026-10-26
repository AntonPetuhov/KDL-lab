namespace AnalyzerService.Transport;

/// <summary>
/// Представляет один TCP-сеанс и его счётчики RX/TX. Контракт принадлежит
/// библиотеке транспорта, поэтому сервис-хост не зависит от сетевых деталей.
/// </summary>
public interface ITcpConnection
{
    /// <summary>Открытый поток текущего TCP-клиента.</summary>
    Stream Stream { get; }

    /// <summary>Синхронно отмечает успешное получение кадра.</summary>
    void RecordRead();

    /// <summary>Синхронно отмечает успешную передачу кадра.</summary>
    void RecordWrite();
}
