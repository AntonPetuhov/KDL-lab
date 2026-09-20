using AnalyzerService.Contracts;
using AnalyzerService.Host.Drivers;
using AnalyzerService.Host.Logging;

namespace AnalyzerService.Host.Runtime;

/// <summary>Управляет загрузкой, запуском и остановкой одного DLL-драйвера.</summary>
public sealed class AnalyzerRuntime(
    AnalyzerSettings settings,
    DriverLoader loader,
    AnalyzerLoggerFactory loggerFactory) : IDisposable
{
    private readonly CancellationTokenSource stop = new();
    private CancellationTokenSource? linkedStop;
    private LoadedDriver? loaded;
    private Task? runTask;

    /// <summary>Получает имя анализатора.</summary>
    public string Name => settings.AnalyzerName;
    /// <summary>Получает задачу рабочего цикла для наблюдения за аварийным завершением.</summary>
    public Task Completion => runTask ?? Task.CompletedTask;

    /// <summary>
    /// Инициализирует DLL синхронно и запускает асинхронный сетевой цикл.
    /// </summary>
    /// <param name="serviceToken">Сигнал остановки службы.</param>
    /// <returns>Завершённая задача после запуска.</returns>
    public Task StartAsync(CancellationToken serviceToken)
    {
        if (runTask is not null) throw new InvalidOperationException($"{Name} уже запущен.");
        loaded = loader.Load(settings.DllPath!);
        loaded.Instance.Initialize(loggerFactory.Create(settings), settings);
        linkedStop = CancellationTokenSource.CreateLinkedTokenSource(stop.Token, serviceToken);
        runTask = loaded.Instance.RunAsync(linkedStop.Token);
        return Task.CompletedTask;
    }

    /// <summary>Асинхронно прекращает сеть и ожидает драйвер.</summary><param name="cancellationToken">Ограничение остановки.</param><returns>Задача остановки.</returns>
    public async Task StopAsync(CancellationToken cancellationToken)
    {
        if (loaded is null) return;
        stop.Cancel();
        await loaded.Instance.StopAsync(cancellationToken).ConfigureAwait(false);
        if (runTask is not null) await runTask.WaitAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Освобождает драйвер и token source.</summary>
    public void Dispose() { loaded?.Dispose(); linkedStop?.Dispose(); stop.Dispose(); }
}
