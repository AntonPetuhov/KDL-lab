using AnalyzerService.Contracts;

namespace SysmexCS2000.HostOnline.Driver;

/// <summary>
/// Точка входа DLL собственного протокола Sysmex Host Online.
/// </summary>
public sealed class SysmexCS2000HostOnlineDriver : IAnalyzerDriver
{
    private AnalyzerSysmexCS2000HostOnline? analyzer;

    /// <summary>
    /// Метод будет вызван из Analyzer после загрузки DLL. Инициализирует драйвер анализатора.
    /// </summary>
    public void Initialize(IAnalyzerLogger logger, AnalyzerSettings settings)
    {
        ArgumentNullException.ThrowIfNull(logger);
        ArgumentNullException.ThrowIfNull(settings);

        if (!string.Equals(settings.Protocol, "SYSMEX_HOST_ONLINE", StringComparison.OrdinalIgnoreCase))
            throw new ArgumentException("Драйвер требует Protocol = SYSMEX_HOST_ONLINE.", nameof(settings));

        analyzer = new AnalyzerSysmexCS2000HostOnline(logger, settings);

        logger.Service($"Инициализация драйвера анализатора {settings.AnalyzerName} выполнена.");
    }

    /// <summary>Синхронно запускает файловую очередь после загрузки DLL сервис-хостом.</summary>
    public void Start() => (analyzer ?? throw new InvalidOperationException("Драйвер не инициализирован.")).Start();

    /// <summary>Асинхронно обрабатывает поток прибора; ожидание требуется только для чтения/записи.</summary>
    /// <param name="connection">Открытое сервис-хостом соединение.</param>
    /// <param name="cancellationToken">Сигнал остановки.</param>
    /// <returns>Задача протокольного сеанса.</returns>
    public Task HandleConnectionAsync(IAnalyzerConnection connection, CancellationToken cancellationToken) =>
        (analyzer ?? throw new InvalidOperationException("Драйвер не инициализирован.")).HandleConnectionAsync(connection, cancellationToken);

    /// <summary>Синхронно останавливает фоновую очередь после закрытия сокета сервисом.</summary>
    public void Stop() => analyzer?.Stop();

    /// <summary>Синхронно освобождает обработчик и файловую очередь.</summary>
    public void Dispose() => analyzer?.Dispose();
}
