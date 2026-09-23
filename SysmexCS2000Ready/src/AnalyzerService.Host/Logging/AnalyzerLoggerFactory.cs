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
        return new AnalyzerLogger(Path.GetFullPath(logs, basePath));
    }
}
