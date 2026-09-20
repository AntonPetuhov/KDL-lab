using AnalyzerService.Host.Configuration;
using AnalyzerService.Host.Runtime;

namespace AnalyzerService.Host;

/// <summary>
/// Реализует жизненный цикл Windows Service: читает JSON, запускает драйверы
/// и завершает их при сигнале Service Control Manager.
/// </summary>
public sealed class Worker(
    JsonAnalyzerSettingsProvider settingsProvider,
    AnalyzerSettingsValidator validator,
    AnalyzerManager manager,
    ILogger<Worker> logger) : BackgroundService
{
    /// <summary>
    /// Асинхронно запускает анализаторы и ожидает остановку. Асинхронность нужна
    /// для длительных сетевых циклов, выполняемых драйверами.
    /// </summary>
    /// <param name="stoppingToken">Сигнал остановки службы.</param>
    /// <returns>Задача жизненного цикла службы.</returns>
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        string directory = Path.Combine(AppContext.BaseDirectory, "configs");
        try
        {
            foreach (var item in settingsProvider.LoadAll(directory))
            {
                validator.ValidateAndThrow(item.Settings, item.SourcePath);
                if (item.Settings.ActiveStatus)
                    manager.Add(item.Settings);
            }

            await manager.StartAllAsync(stoppingToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            logger.LogInformation("Остановка службы запрошена.");
        }
        catch (Exception ex)
        {
            logger.LogCritical(ex, "Служба анализаторов аварийно завершена.");
            throw;
        }
    }

    /// <summary>
    /// Асинхронно останавливает все драйверы, потому что закрытие активного TCP-сеанса
    /// требует ожидания завершения сетевых операций.
    /// </summary>
    /// <param name="cancellationToken">Ограничение времени остановки.</param>
    /// <returns>Задача остановки.</returns>
    public override async Task StopAsync(CancellationToken cancellationToken)
    {
        await manager.StopAllAsync(cancellationToken).ConfigureAwait(false);
        await base.StopAsync(cancellationToken).ConfigureAwait(false);
    }
}
