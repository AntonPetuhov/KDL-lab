using AnalyzerService.Contracts;

namespace AnalyzerService.Host.Configuration;

/// <summary>
/// Проверяет только поля JSON, нужные хосту для выбора и загрузки DLL.
/// Тип подключения, протокол и его параметры проверяет сам драйвер.
/// </summary>
public sealed class AnalyzerSettingsValidator
{
    /// <summary>
    /// проверяем все обязательные параметры в файле JSON и выбрасываем исключение со списком ошибок.
    /// </summary>
    public void ValidateAndThrow(AnalyzerSettings settings, string sourcePath)
    {
        List<string> errors = [];
        if (string.IsNullOrWhiteSpace(settings.AnalyzerName)) 
            errors.Add("AnalyzerName обязателен");
        // для подключения с помощью dll
        if (!settings.Isdll) 
            errors.Add("Isdll должен быть true");
        if (string.IsNullOrWhiteSpace(settings.DllPath)) 
            errors.Add("Путь к драйверу анализатора DllPath обязателен");
        if (errors.Count != 0) 
            throw new InvalidDataException($"Ошибки {sourcePath}: {string.Join("; ", errors)}");
    }
}
