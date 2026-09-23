using AnalyzerService.Host.Configuration;
using AnalyzerService.Host.Runtime;

namespace AnalyzerService.Host;

/// <summary>
/// Реализует жизненный цикл Windows Service: 
/// Загружает конфигурации JSON, запускает и останавливает AnalyzerManager
/// и завершает их при сигнале Service Control Manager.
/// </summary>
public class Worker(JsonAnalyzerSettingsProvider settingsProvider, AnalyzerSettingsValidator validator, AnalyzerManager analyzerManager, ILogger<Worker> logger) : BackgroundService
{
    /// <summary>
    /// Запуск службы и начало работы
    /// </summary>
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        string directory = Path.Combine(AppContext.BaseDirectory, "configs");
        try
        {
            foreach (var configFile in settingsProvider.LoadAll(directory))
            {
                // проверка корректности конфигурационных данных
                validator.ValidateAndThrow(configFile.Settings, configFile.SourcePath);
                // Если ActiveStatus = true, то добавляем в менеджер анализаторов
                if (configFile.Settings.ActiveStatus)
                    analyzerManager.Add(configFile.Settings);
            }

            // запускаем все анализаторы, которые должны быть запущены, согласно конфигурации
            //await analyzerManager.StartAllAsync(stoppingToken).ConfigureAwait(false);
            await analyzerManager.StartAllAsync(stoppingToken);
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
    /// Остановка службы
    /// </summary>
    public override async Task StopAsync(CancellationToken cancellationToken)
    {
        //await analyzerManager.StopAllAsync(cancellationToken).ConfigureAwait(false);
        //await base.StopAsync(cancellationToken).ConfigureAwait(false);
        await analyzerManager.StopAllAsync(cancellationToken);
        await base.StopAsync(cancellationToken);
    }
}
