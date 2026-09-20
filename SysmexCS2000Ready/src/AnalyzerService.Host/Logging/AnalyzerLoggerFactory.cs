using AnalyzerService.Contracts;

namespace AnalyzerService.Host.Logging;

/// <summary>Создаёт отдельный файловый логгер для каждого анализатора.</summary>
public sealed class AnalyzerLoggerFactory
{
    /// <summary>Создаёт логгер в каталоге анализатора.</summary><param name="settings">Настройки анализатора.</param><returns>Новый логгер.</returns>
    public IAnalyzerLogger Create(AnalyzerSettings settings)
    {
        string basePath = Path.Combine(AppContext.BaseDirectory, settings.AnalyzerName);
        string logs = string.IsNullOrWhiteSpace(settings.LogsFolder) ? "Logs" : settings.LogsFolder;
        return new FileAnalyzerLogger(Path.GetFullPath(logs, basePath));
    }
}
