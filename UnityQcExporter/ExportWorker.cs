namespace UnityQcExporter;

/// <summary>Периодически проверяет каждую включённую папку в отдельном рабочем цикле.</summary>
internal sealed class ExportWorker(ExportSettings settings, ILogger<ExportWorker> logger) : BackgroundService
{
    /// <summary>Возвращает задачу всех циклов. Медленное сетевое обращение одного источника не занимает цикл другого.</summary>
    protected override Task ExecuteAsync(CancellationToken token)
    {
        logger.LogInformation("Активных источников Unity: {Count}", settings.Sources.Length);
        return Task.WhenAll(settings.Sources.Select(s => Task.Run(() => MonitorAsync(s, token), token)));
    }

    /// <summary>Сканирует каталог сразу при запуске. Ожидает следующий опрос с поддержкой отмены.</summary>
    private async Task MonitorAsync(Source source, CancellationToken token)
    {
        var queue = new DeliveryQueue(source, settings.UnityOutputFolder, logger);
        while (!token.IsCancellationRequested)
        {
            try { queue.Scan(token); }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                logger.LogError(ex, "Источник {Id}: ошибка доступа. Следующая проверка через {Seconds} с.", source.Id, settings.PollSeconds);
            }
            await Task.Delay(TimeSpan.FromSeconds(settings.PollSeconds), token);
        }
    }
}
