namespace AnalyzerService.Contracts;

/// <summary>
/// Определяет синхронное структурированное логирование операций одного анализатора.
/// Короткие файловые записи синхронны, чтобы гарантировать порядок и запись при остановке.
/// </summary>
public interface IAnalyzerLogger
{
    /// <summary>Записывает событие жизненного цикла.</summary><param name="message">Текст события.</param>
    void Service(string message);
    /// <summary>Записывает сетевое событие.</summary><param name="message">Текст события.</param>
    void Transport(string message);
    /// <summary>Записывает событие протокола.</summary><param name="message">Текст события.</param>
    void Protocol(string message);
    /// <summary>Записывает событие обработки результата.</summary><param name="message">Текст события.</param>
    void Result(string message);
    /// <summary>Записывает ошибку с контекстом.</summary><param name="message">Контекст.</param><param name="exception">Исключение.</param>
    void Error(string message, Exception exception);
}
