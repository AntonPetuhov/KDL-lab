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
    /// Запуск работы анализатора
    /// </summary>
    Task RunAsync(CancellationToken cancellationToken);

    /// <summary>
    /// Остановка работы анализатора
    /// </summary>
    Task StopAsync(CancellationToken cancellationToken);
}
