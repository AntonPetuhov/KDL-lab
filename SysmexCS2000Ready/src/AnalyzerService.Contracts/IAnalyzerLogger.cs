namespace AnalyzerService.Contracts;

/// <summary>
/// структурированное логирование операций одного анализатора.
/// </summary>
public interface IAnalyzerLogger
{
    // Записывает событие жизненного цикла.
    void Service(string message);
    // Записывает сетевое событие.
    void Transport(string message);
    // Записывает событие протокола, обмен данными с анализатором.
    void Protocol(string message);
    // Записывает событие обработки результата.
    void Result(string message);
    // Записывает ошибку с контекстом.
    void Error(string message, Exception exception);
}
