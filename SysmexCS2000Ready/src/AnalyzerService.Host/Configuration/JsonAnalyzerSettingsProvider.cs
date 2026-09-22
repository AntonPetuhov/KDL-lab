using System.Text.Json;
using AnalyzerService.Contracts;

namespace AnalyzerService.Host.Configuration;

/// <summary>
/// Читает JSON-файлы конфигурации при запуске службы.
/// </summary>
public class JsonAnalyzerSettingsProvider
{
    // Настройка десериализации: не учитывать регистр имён свойств
    private static readonly JsonSerializerOptions options = new() { PropertyNameCaseInsensitive = true };

    // Получает все файлы конфигурации в директории и создается класс с настройками
    public IReadOnlyList<SettingsFile> LoadAll(string directory)
    {
        if (!Directory.Exists(directory))
            throw new DirectoryNotFoundException($"Каталог конфигурации не найден: {directory}");

        // Для каждого пути создаётся SettingsFile
        return Directory.GetFiles(directory, "*.json")
            .OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
            .Select(path => new SettingsFile(path, Load(path))) 
            .ToArray();
    }

    /// <summary>
    /// Загружает один JSON-файл.
    /// </summary>
    public AnalyzerSettings Load(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
            throw new ArgumentException("Путь не может быть пустым.", nameof(path));
        if (!File.Exists(path)) 
            throw new FileNotFoundException("Файл конфигурации не найден.", path);

        // возвращаем десериализованные настройки
        // Десериализация JSON в объект AnalyzerSettings
        // ?? null?объединяющий оператор (null?coalescing operator)
        // возвращает результат своего левого операнда, если он существует и не равен null, а в противном случае возвращает правый операнд
        return JsonSerializer.Deserialize<AnalyzerSettings>(File.ReadAllText(path), options)
            ?? throw new JsonException($"Конфигурация {path} содержит null.");
    }
}

// Связывает настройки с файлом, из которого они были прочитаны.
public record SettingsFile(string SourcePath, AnalyzerSettings Settings);
