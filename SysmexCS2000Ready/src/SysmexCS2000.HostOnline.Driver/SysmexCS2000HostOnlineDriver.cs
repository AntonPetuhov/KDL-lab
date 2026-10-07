using AnalyzerService.Contracts;
using System.Net;

namespace SysmexCS2000.HostOnline.Driver;

/// <summary>
/// Точка входа DLL собственного протокола Sysmex Host Online.
/// </summary>
public sealed class SysmexCS2000HostOnlineDriver : IAnalyzerDriver
{
    private AnalyzerSysmexCS2000HostOnline? analyzer;

    #region Инициализация
    /// <summary>
    /// Метод будет вызван из Analyzer после загрузки DLL. Инициализирует драйвер анализатора.
    /// </summary>
    public void Initialize(IAnalyzerLogger logger, AnalyzerSettings settings)
    {
        ArgumentNullException.ThrowIfNull(logger);
        ArgumentNullException.ThrowIfNull(settings);

        #region проверка полей JSON
        // Только DLL знает, какие поля JSON обязательны для её реализации.
        List<string> errors = [];
        // Сравниваем поля конфигурации и формируем список ошибок
        // StringComparison.OrdinalIgnoreCase - без учета регистра
        if (!string.Equals(settings.Protocol, "SYSMEX_HOST_ONLINE", StringComparison.OrdinalIgnoreCase)) 
            errors.Add("Protocol=SYSMEX_HOST_ONLINE");
        if (!string.Equals(settings.ConnectionType, "TCPIP", StringComparison.OrdinalIgnoreCase)) 
            errors.Add("ConnectionType=TCPIP");
        if (!IPAddress.TryParse(settings.IPaddress, out _)) // проверка корректности ip адреса
            errors.Add("локальный IPaddress");
        if (settings.Port is < 1 or > 65535) 
            errors.Add("Port 1..65535");
        if (string.IsNullOrWhiteSpace(settings.ResultsFolder)) 
            errors.Add("ResultsFolder");
        if (string.IsNullOrWhiteSpace(settings.OutputFolder)) 
            errors.Add("OutputFolder");
        if (string.IsNullOrWhiteSpace(settings.ConnectionString)) 
            errors.Add("ConnectionString");
        if (string.IsNullOrWhiteSpace(settings.AnalyzerConfigurationCode)) 
            errors.Add("AnalyzerConfigurationCode");
        if (string.IsNullOrWhiteSpace(settings.AnalyzerCode)) 
            errors.Add("AnalyzerCode");

        if (errors.Count != 0) 
            throw new InvalidDataException($"Для Sysmex Host Online нужны корректные данные: {string.Join(", ", errors)}.");
        #endregion

        analyzer = new AnalyzerSysmexCS2000HostOnline(logger, settings);

        logger.Service($"Инициализация драйвера анализатора {settings.AnalyzerName} выполнена.");
    }
    #endregion

    /// <summary>Запускает собственный TCP listener и возвращает задачу его рабочего цикла.</summary>
    /// <param name="cancellationToken">Сигнал остановки.</param>
    /// <returns>Задача до завершения работы прибора.</returns>
    public Task RunAsync(CancellationToken cancellationToken) =>
        (analyzer ?? throw new InvalidOperationException("Драйвер не инициализирован.")).RunAsync(cancellationToken);

    /// <summary>Синхронно просит драйвер закрыть свои ресурсы и завершить RunAsync.</summary>
    public void Stop() => analyzer?.Stop();

    /// <summary>Синхронно освобождает обработчик и файловую очередь.</summary>
    public void Dispose() => analyzer?.Dispose();
}
