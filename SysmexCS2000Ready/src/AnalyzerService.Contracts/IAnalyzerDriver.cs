namespace AnalyzerService.Contracts;

/// <summary>
/// Определяет стабильный контракт управляемого DLL-драйвера анализатора.
/// Реализация загружается службой через изолированный AssemblyLoadContext.
/// </summary>
public interface IAnalyzerDriver : IDisposable
{
    /// <summary>
    /// Инициализация драйвера, передаем логгер конкретного анализатора и проверенную JSON-конфигурацию
    /// </summary>
    void Initialize(IAnalyzerLogger logger, AnalyzerSettings settings);

    /// <summary>
    /// Синхронно запускает внутренние ресурсы драйвера после инициализации.
    /// </summary>
    void Start();

    /// <summary>
    /// Асинхронно обрабатывает один поток соединения; TCP listener и принятие клиента
    /// принадлежат сервис-хосту, а здесь остаётся только прикладной протокол прибора.
    /// </summary>
    /// <param name="connection">Поток прибора и счётчики обмена.</param>
    /// <param name="cancellationToken">Сигнал остановки.</param>
    /// <returns>Задача сеанса.</returns>
    Task HandleConnectionAsync(IAnalyzerConnection connection, CancellationToken cancellationToken);

    /// <summary>Синхронно останавливает фоновые ресурсы драйвера после закрытия TCP-хоста.</summary>
    void Stop();
}
