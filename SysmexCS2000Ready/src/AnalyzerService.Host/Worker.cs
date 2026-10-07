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
        // создаем общий логгер службы
        var serviceLog = loggerFactory.CreateServiceLogger();
        try
        {
            foreach (var configFile in settingsProvider.LoadAll(directory))
            {
                // проверка корректности конфигурационных данных
                validator.ValidateAndThrow(configFile.Settings, configFile.SourcePath);
                // Если ActiveStatus = true, то добавляем в менеджер анализаторов AnalyzerManager
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
            serviceLog.Error($"Исключение запуска службы. Каталог настроек: {directory}.", ex);
            logger.LogCritical(ex, "Служба конфигурации анализаторов аварийно завершена.");
            throw;
        }
    }

    /// <summary>
    /// Остановка службы
    /// </summary>
    public override async Task StopAsync(CancellationToken cancellationToken)
    {
        try 
        {
            // ConfigureAwait(false) не нужно возвращать продолжение в исходный контекст после выполнения await
            await analyzerManager.StopAllAsync(cancellationToken).ConfigureAwait(false); 
        }
        catch (Exception ex)
        {
            loggerFactory.CreateServiceLogger().Error("Возникло исключение остановки службы анализаторов.", ex);
            logger.LogError(ex, "Ошибка остановки Службы анализаторов.");
            throw;
        }
        finally 
        { 
            await base.StopAsync(cancellationToken).ConfigureAwait(false); 
        }
    }
}
