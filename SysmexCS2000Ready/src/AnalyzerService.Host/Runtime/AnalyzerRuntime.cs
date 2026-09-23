using AnalyzerService.Contracts;
using AnalyzerService.Host.Drivers;
using AnalyzerService.Host.Logging;

namespace AnalyzerService.Host.Runtime;

/// <summary>
/// Управляет загрузкой, запуском и остановкой одного DLL-драйвера. Здесь не важно как именно реализована работа анализатора.
/// </summary>
public class AnalyzerRuntime(AnalyzerSettings settings, DriverLoader loader, AnalyzerLoggerFactory loggerFactory) : IDisposable
{
    private readonly CancellationTokenSource stop = new(); // объект, кот. управляет и посылает уведомление об отмене токену
    private CancellationTokenSource? linkedStop;
    private LoadedDriver? loaded;
    private Task? runTask;

    /// <summary>Получает имя анализатора.</summary>
    public string Name => settings.AnalyzerName;
    /// <summary>Получает задачу рабочего цикла для наблюдения за аварийным завершением.</summary>
    public Task Completion => runTask ?? Task.CompletedTask;

    #region запуск анализатора, запуск соответствующих потоков

    /// <summary>
    /// Инициализирует DLL синхронно и запускает асинхронный сетевой цикл.
    /// </summary>
    public Task StartAsync(CancellationToken serviceToken)
    {
        if (runTask is not null) throw new InvalidOperationException($"{Name} уже запущен.");

        linkedStop = CancellationTokenSource.CreateLinkedTokenSource(stop.Token, serviceToken);

        if (settings.Isdll)
        {
            // загрузка dll
            loaded = loader.Load(settings.DllPath!);
            loaded.Instance.Initialize(loggerFactory.Create(settings), settings);

            // нет await, драйвер работает долго, и текущий метод лишь запускает его, но не блокируется. 
            runTask = loaded.Instance.RunAsync(linkedStop.Token);
        }
        else
        {
            // Работа без DLL не реализована.
            linkedStop.Dispose();
            linkedStop = null;
        }

        return Task.CompletedTask;
    }
    #endregion

    #region Остановка работы анализатора

    /// <summary>
    /// Асинхронно прекращает сеть и ожидает драйвер.
    /// </summary>
    public async Task StopAsync(CancellationToken cancellationToken)
    {
        // TODO добавить логер сюда
        // анализатор уже остановлен
        if (loaded is null) return;

        // Остановка анализатора
        // TODO добавить логер сюда
        stop.Cancel();

        //await loaded.Instance.StopAsync(cancellationToken).ConfigureAwait(false);
        await loaded.Instance.StopAsync(cancellationToken);
        if (runTask is not null) 
            //await runTask.WaitAsync(cancellationToken).ConfigureAwait(false);
            await runTask.WaitAsync(cancellationToken);
    }
    #endregion

    /// <summary>
    /// Освобождает драйвер и token source.
    /// </summary>
    public void Dispose() 
    { 
        loaded?.Dispose(); 
        linkedStop?.Dispose(); 
        stop.Dispose(); 
    }
}
