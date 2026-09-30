namespace AnalyzerService.Contracts;

/// <summary>
/// Передаёт загруженному DLL-драйверу поток одного уже принятого соединения.
/// Сервис-хост владеет сокетом и счётчиками; драйвер владеет только разбором протокола.
/// </summary>
public interface IAnalyzerConnection
{
    /// <summary>Получает поток байтов для чтения и отправки сообщений.</summary>
    Stream Stream { get; }

    /// <summary>Синхронно отмечает полностью принятое сообщение в журнале состояния TCP.</summary>
    void RecordRead();

    /// <summary>Синхронно отмечает отправленное сообщение в журнале состояния TCP.</summary>
    void RecordWrite();
}
