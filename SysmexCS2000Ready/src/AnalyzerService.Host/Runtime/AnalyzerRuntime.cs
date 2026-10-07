using AnalyzerService.Contracts;
using AnalyzerService.Host.Drivers;
using AnalyzerService.Host.Logging;

namespace AnalyzerService.Host.Runtime;

/// <summary>
/// Управляет жизненным циклом одной DLL, не зная её транспорта или протокола.
/// Передаёт настройки и логгер в IAnalyzerDriver, наблюдает RunAsync и подаёт
/// Stop при остановке службы. TCP, COM и файловый обмен принадлежат драйверам.
/// </summary>
public sealed class AnalyzerRuntime(AnalyzerSettings settings, DriverLoader loader, AnalyzerLoggerFactory loggerFactory) : IDisposable
{
    private readonly CancellationTokenSource stop = new();
    private CancellationTokenSource? linkedStop;
    private LoadedDriver? loadedDriver; // загруженный драйвер Dll
    private IAnalyzerLogger? logger;
    private Task? runTask;  // задача анализатора
    private bool disposed; // флаг, означающий, что ресурсы были освобождены

    /// <summary>
    /// Имя анализатора из JSON для диагностики.
    /// </summary>
    public string Name => settings.AnalyzerName;

    /// <summary>
    /// Задача полного рабочего цикла драйвера DLL; 
    /// менеджер наблюдает её ошибки.
    /// </summary>
    public Task Completion => runTask ?? Task.CompletedTask;

    /// <summary>
    /// Загружает и инициализирует DLL, затем отдаёт ей команду RunAsync.
    /// Task возвращается для контракта менеджера; ожидание I/O остаётся внутри DLL.
    /// </summary>
    public Task StartAsync(CancellationToken serviceToken)
    {
        logger.Service($"Запуск работы анализатора {Name}. Загрузка dll и инициализация драйвера...");

        // если объект анализатора уже был освобожден
        if (disposed) 
            throw new ObjectDisposedException(nameof(AnalyzerRuntime));
        // если анализатор уже запущен
        if (runTask is not null)
        {
            InvalidOperationException ex = new($"{Name} уже запущен.");
            logger?.Error($"{Name}: повторный запуск отклонён.", ex);
            throw ex;
        }

        logger = loggerFactory.Create(settings);

        linkedStop = CancellationTokenSource.CreateLinkedTokenSource(stop.Token, serviceToken); // токен отмены

        try
        {
            // загружаем драйвер из Dll
            loadedDriver = loader.Load(settings.DllPath!);
            // инициализируем драйвер, то есть создаем экземпляр класса 
            loadedDriver.Instance.Initialize(logger, settings);
            // вызываем метод RunAsync
            // если запуск не удался loadedDriver.Instance.RunAsync(linkedStop.Token) = null, то исключение
            runTask = loadedDriver.Instance.RunAsync(linkedStop.Token)
                ?? throw new InvalidOperationException($"{Name}: драйвер вернул null вместо задачи RunAsync.");

            if (runTask.IsFaulted) 
                runTask.GetAwaiter().GetResult();
            logger.Service($"{Name}: рабочий цикл DLL запущен.");
            return Task.CompletedTask;
        }
        catch (Exception ex)
        {
            logger.Error($"{Name}: исключение загрузки или запуска DLL; передаётся менеджеру.", ex);

            try 
            { 
                loadedDriver?.Instance.Stop(); 
            }
            catch (Exception stopError) 
            { 
                logger.Error($"{Name}: ошибка отката запуска драйвера.", stopError); 
            }

            try 
            { 
                loadedDriver?.Dispose(); 
            }
            catch (Exception cleanupError) 
            { 
                logger.Error($"{Name}: ошибка освобождения DLL после сбоя запуска.", cleanupError); 
            }
            loadedDriver = null;
            runTask = null;
            linkedStop.Dispose();
            linkedStop = null;
            throw;
        }
    }

    /// <summary>
    /// Сначала отменяет рабочий цикл и вызывает синхронный Stop драйвера, чтобы
    /// разблокировать его I/O, затем асинхронно ожидает RunAsync. Тип I/O неизвестен хосту.
    /// </summary>
    /// <param name="cancellationToken">Ограничение ожидания при остановке службы.</param>
    /// <returns>Задача завершения DLL.</returns>
    /// <exception cref="AggregateException">Ошибки Stop или рабочего цикла, уже записанные в журнал.</exception>
    public async Task StopAsync(CancellationToken cancellationToken)
    {
        if (loadedDriver is null) return;
        List<Exception> errors = [];
        try { stop.Cancel(); }
        catch (Exception ex) { logger?.Error($"{Name}: исключение отмены рабочего цикла.", ex); errors.Add(ex); }
        try { loadedDriver.Instance.Stop(); }
        catch (Exception ex) { logger?.Error($"{Name}: исключение сигнала остановки драйвера.", ex); errors.Add(ex); }
        try { if (runTask is not null) await runTask.WaitAsync(cancellationToken).ConfigureAwait(false); }
        catch (OperationCanceledException) when (stop.IsCancellationRequested && !cancellationToken.IsCancellationRequested)
        {
            logger?.Service($"{Name}: рабочая задача DLL завершилась по штатной отмене.");
        }
        catch (Exception ex) { logger?.Error($"{Name}: исключение ожидания рабочего цикла DLL.", ex); errors.Add(ex); }
        if (errors.Count != 0) throw new AggregateException($"{Name}: ошибки остановки.", errors);
    }

    /// <summary>
    /// Синхронно и повторно безопасно освобождает экземпляр DLL и токены после StopAsync.
    /// Исключение освобождения не подавляется и логируется вызывающим менеджером.
    /// </summary>
    public void Dispose()
    {
        if (disposed) return;
        disposed = true;
        try { loadedDriver?.Dispose(); }
        finally
        {
            linkedStop?.Dispose();
            stop.Dispose();
        }
    }
}
