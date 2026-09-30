using AnalyzerService.Contracts;

namespace AnalyzerService.Host.Logging;

/// <summary>
/// Создаёт отдельный файловый логгер для каждого анализатора.
/// </summary>
public class AnalyzerLoggerFactory
{
    /// <summary>
    /// Создаёт логгер в каталоге анализатора.
    /// </summary>
    public IAnalyzerLogger Create(AnalyzerSettings settings)
    {
        string basePath = Path.Combine(AppContext.BaseDirectory, settings.AnalyzerName);
        // Если в настройках не указана папка для логов или указана пустая строка, использовать папку "Logs"
        string logs = string.IsNullOrWhiteSpace(settings.LogsFolder) ? "Logs" : settings.LogsFolder; 
        return new FileAnalyzerLogger(Path.GetFullPath(logs, basePath));
    }

    /// <summary>Создаёт общий файловый журнал ошибок до загрузки настроек приборов.</summary>
    /// <returns>Логгер службы в каталоге Logs рядом с EXE.</returns>
    public IAnalyzerLogger CreateServiceLogger() =>
        new FileAnalyzerLogger(Path.Combine(AppContext.BaseDirectory, "Logs"));
}
