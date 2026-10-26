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
    /// Запускает работу анализатора после инициализации и возвращает задачу всего
    /// рабочего цикла. Драйвер сам выбирает транспорт и освобождает его при отмене.
    /// </summary>
    Task RunAsync(CancellationToken cancellationToken);

    /// <summary>
    /// Подаёт сигнал остановки внутренних ресурсов драйвера.
    /// Не должен ждать длительных операций: хост отдельно ожидает задачу RunAsync.
    /// </summary>
    void Stop();
}
