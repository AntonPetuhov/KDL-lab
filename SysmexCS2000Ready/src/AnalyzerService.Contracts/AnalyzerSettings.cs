namespace AnalyzerService.Contracts;

/// <summary>
/// Представляет конфигурацию одного анализатора, прочитанную из JSON.
/// Используется службой-хостом и загружаемым DLL-драйвером.
/// </summary>
public sealed class AnalyzerSettings
{
    /// <summary>Получает или задаёт необязательный уникальный идентификатор прибора.</summary>
    public string? AnalyzerId { get; set; }
    /// <summary>Получает или задаёт отображаемое уникальное имя анализатора.</summary>
    public required string AnalyzerName { get; set; }
    /// <summary>Получает или задаёт тип подключения, поддерживается TCPIP.</summary>
    public required string ConnectionType { get; set; }
    /// <summary>Получает или задаёт локальный IP-адрес ЛИС, который слушает служба.</summary>
    public string? IPaddress { get; set; }
    /// <summary>Получает или задаёт локальный TCP-порт ЛИС.</summary>
    public int Port { get; set; }
    /// <summary>Получает или задаёт признак загрузки драйвера из DLL.</summary>
    public bool Isdll { get; set; }
    /// <summary>Получает или задаёт путь к DLL относительно каталога службы или абсолютный путь.</summary>
    public string? DllPath { get; set; }
    /// <summary>Получает или задаёт признак активности конфигурации.</summary>
    public bool ActiveStatus { get; set; }
    /// <summary>Получает или задаёт признак запуска обмена.</summary>
    public bool WorkStatus { get; set; }
    /// <summary>Получает или задаёт признак обработки результатов.</summary>
    public bool ResultHandlerStatus { get; set; }
    /// <summary>Получает или задаёт каталог сырых результатов.</summary>
    public required string ResultsFolder { get; set; }
    /// <summary>Получает или задаёт необязательный каталог логов.</summary>
    public string? LogsFolder { get; set; }
    /// <summary>Получает или задаёт каталог результирующих файлов для ЛИС.</summary>
    public string? OutputFolder { get; set; }
    /// <summary>Получает или задаёт строку подключения к БД ЛИС.</summary>
    public required string ConnectionString { get; set; }
    /// <summary>Получает или задаёт код анализатора в выходном сообщении.</summary>
    public string? AnalyzerCode { get; set; }
    /// <summary>Получает или задаёт код прибора в Analyzer Configuration.</summary>
    public string? AnalyzerConfigurationCode { get; set; }
    /// <summary>
    /// Получает или задаёт формат протокола. Поле добавлено как расширение,
    /// поскольку TCP/IP поддерживает более одного формата Sysmex.
    /// </summary>
    public string Protocol { get; set; } = "ASTM_E1381_02_E1394_97";
}
