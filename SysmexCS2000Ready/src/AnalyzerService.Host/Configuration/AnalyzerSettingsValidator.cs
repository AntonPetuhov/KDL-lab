using AnalyzerService.Contracts;

namespace AnalyzerService.Host.Configuration;

/// <summary>
/// Проверяет только поля JSON, нужные хосту для выбора и загрузки DLL.
/// Тип подключения, протокол и его параметры проверяет сам драйвер.
/// </summary>
public sealed class AnalyzerSettingsValidator
{
    /// <summary>
    /// Синхронно проверяет все обязательные параметры и выбрасывает одну ошибку со списком нарушений.
    /// </summary>
    /// <param name="settings">Проверяемые настройки.</param>
    /// <param name="sourcePath">Путь для диагностического сообщения.</param>
    /// <exception cref="InvalidDataException">Обнаружены ошибки конфигурации.</exception>
    public void ValidateAndThrow(AnalyzerSettings settings, string sourcePath)
    {
        List<string> errors = [];
        if (string.IsNullOrWhiteSpace(settings.AnalyzerName)) errors.Add("AnalyzerName обязателен");
        if (!settings.Isdll) errors.Add("Isdll должен быть true");
        if (string.IsNullOrWhiteSpace(settings.DllPath)) errors.Add("DllPath обязателен");
        if (errors.Count != 0) throw new InvalidDataException($"Ошибки {sourcePath}: {string.Join("; ", errors)}");
    }
}
