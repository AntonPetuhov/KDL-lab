using System.Text.Json;
using AnalyzerService.Contracts;

namespace AnalyzerService.Host.Configuration;

/// <summary>
/// Синхронно читает малые JSON-файлы конфигурации при запуске службы.
/// Синхронный ввод выбран потому, что операция разовая и выполняется до сетевого цикла.
/// </summary>
public sealed class JsonAnalyzerSettingsProvider
{
    private static readonly JsonSerializerOptions Options = new() { PropertyNameCaseInsensitive = true };

    /// <summary>
    /// Загружает все JSON-конфигурации в стабильном порядке имён файлов.
    /// </summary>
    /// <param name="directory">Каталог конфигураций.</param>
    /// <returns>Настройки вместе с исходными путями.</returns>
    /// <exception cref="DirectoryNotFoundException">Каталог отсутствует.</exception>
    public IReadOnlyList<SettingsFile> LoadAll(string directory)
    {
        if (!Directory.Exists(directory))
            throw new DirectoryNotFoundException($"Каталог конфигурации не найден: {directory}");

        return Directory.GetFiles(directory, "*.json")
            .OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
            .Select(path => new SettingsFile(path, Load(path)))
            .ToArray();
    }

    /// <summary>
    /// Загружает один JSON-файл синхронно.
    /// </summary>
    /// <param name="path">Полный путь к файлу.</param>
    /// <returns>Десериализованные настройки.</returns>
    /// <exception cref="FileNotFoundException">Файл отсутствует.</exception>
    /// <exception cref="JsonException">JSON некорректен.</exception>
    public AnalyzerSettings Load(string path)
    {
        if (!File.Exists(path)) throw new FileNotFoundException("Файл конфигурации не найден.", path);
        return JsonSerializer.Deserialize<AnalyzerSettings>(File.ReadAllText(path), Options)
            ?? throw new JsonException($"Конфигурация {path} содержит null.");
    }
}

/// <summary>Связывает настройки с файлом, из которого они были прочитаны.</summary>
public sealed record SettingsFile(string SourcePath, AnalyzerSettings Settings);
