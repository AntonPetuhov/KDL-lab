namespace AnalyzerService.Contracts;

/// <summary>
/// Определяет синхронное структурированное логирование операций одного анализатора.
/// </summary>
public interface IAnalyzerLogger
{
    /// <summary>Записывает событие жизненного цикла.</summary>
    void Service(string message);
    /// <summary>Записывает сетевое событие.</summary>
    void Transport(string message);
    /// <summary>Записывает событие протокола.</summary>
    void Protocol(string message);
    /// <summary>Записывает событие обработки результата.</summary>
    void Result(string message);
    /// <summary>Записывает ошибку с контекстом.</summary>
    void Error(string message, Exception exception);
}
