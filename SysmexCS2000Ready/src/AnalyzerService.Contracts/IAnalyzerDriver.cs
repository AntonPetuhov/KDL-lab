namespace AnalyzerService.Contracts;

/// <summary>
/// Определяет стабильный контракт управляемого DLL-драйвера анализатора.
/// Реализация загружается службой через изолированный AssemblyLoadContext.
/// </summary>
public interface IAnalyzerDriver : IDisposable
{
    /// <summary>
    /// Синхронно проверяет и сохраняет зависимости до запуска сетевого цикла.
    /// </summary>
    /// <param name="logger">Логгер конкретного анализатора.</param>
    /// <param name="settings">Проверенная JSON-конфигурация.</param>
    /// <exception cref="ArgumentNullException">Зависимость не передана.</exception>
    /// <exception cref="ArgumentException">Настройки не поддерживаются драйвером.</exception>
    void Initialize(IAnalyzerLogger logger, AnalyzerSettings settings);

    /// <summary>
    /// Асинхронно выполняет обмен, поскольку ожидание подключения и TCP-данных
    /// имеет неопределённую длительность и не должно блокировать поток службы.
    /// </summary>
    /// <param name="cancellationToken">Сигнал штатной остановки.</param>
    /// <returns>Задача полного жизненного цикла обмена.</returns>
    Task RunAsync(CancellationToken cancellationToken);

    /// <summary>
    /// Асинхронно останавливает транспорт и ожидает завершение активного обмена.
    /// </summary>
    /// <param name="cancellationToken">Ограничение времени остановки.</param>
    /// <returns>Задача остановки.</returns>
    Task StopAsync(CancellationToken cancellationToken);
}
