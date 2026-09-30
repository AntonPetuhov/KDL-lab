using AnalyzerService.Host.Configuration;
using AnalyzerService.Host.Runtime;
using AnalyzerService.Host.Logging;

namespace AnalyzerService.Host;

/// <summary>
/// Реализует жизненный цикл Windows Service: 
/// Загружает конфигурации JSON, запускает и останавливает AnalyzerManager
/// и завершает их при сигнале Service Control Manager.
/// </summary>
public class Worker(JsonAnalyzerSettingsProvider settingsProvider, AnalyzerSettingsValidator validator, AnalyzerManager analyzerManager, AnalyzerLoggerFactory loggerFactory, ILogger<Worker> logger) : BackgroundService
{
    /// <summary>
    /// Запуск службы и начало работы
    /// </summary>
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        string directory = Path.Combine(AppContext.BaseDirectory, "configs");
        var serviceLog = loggerFactory.CreateServiceLogger();
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
            serviceLog.Error($"Служба: исключение запуска или рабочего цикла; пробрасывается Generic Host. Каталог настроек: {directory}.", ex);
            logger.LogCritical(ex, "Служба анализаторов аварийно завершена.");
            throw;
        }
    }

    /// <summary>
    /// Остановка службы
    /// </summary>
    public override async Task StopAsync(CancellationToken cancellationToken)
    {
        try { await analyzerManager.StopAllAsync(cancellationToken).ConfigureAwait(false); }
        catch (Exception ex)
        {
            loggerFactory.CreateServiceLogger().Error("Служба: исключение остановки анализаторов; пробрасывается Windows Service.", ex);
            logger.LogError(ex, "Ошибка остановки анализаторов.");
            throw;
        }
        finally { await base.StopAsync(cancellationToken).ConfigureAwait(false); }
    }
}
