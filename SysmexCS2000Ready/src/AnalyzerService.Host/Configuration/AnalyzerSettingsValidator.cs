using System.Net;
using AnalyzerService.Contracts;

namespace AnalyzerService.Host.Configuration;

/// <summary>Проверяет конфигурацию до загрузки и исполнения сторонней DLL.</summary>
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
        if (!string.Equals(settings.ConnectionType, "TCPIP", StringComparison.OrdinalIgnoreCase)) errors.Add("поддерживается только ConnectionType=TCPIP");
        if (!IPAddress.TryParse(settings.IPaddress, out _)) errors.Add("IPaddress должен быть IP-адресом локального интерфейса");
        if (settings.Port is < 1 or > 65535) errors.Add("Port должен быть от 1 до 65535");
        if (!settings.Isdll) errors.Add("Isdll должен быть true");
        if (string.IsNullOrWhiteSpace(settings.DllPath)) errors.Add("DllPath обязателен");
        if (settings.Protocol is null || !(settings.Protocol.Equals("ASTM_E1381_02_E1394_97", StringComparison.OrdinalIgnoreCase)
            || settings.Protocol.Equals("SYSMEX_HOST_ONLINE", StringComparison.OrdinalIgnoreCase)))
            errors.Add("Protocol должен быть ASTM_E1381_02_E1394_97 или SYSMEX_HOST_ONLINE");
        if (string.IsNullOrWhiteSpace(settings.OutputFolder)) errors.Add("OutputFolder обязателен");
        if (errors.Count != 0) throw new InvalidDataException($"Ошибки {sourcePath}: {string.Join("; ", errors)}");
    }
}
