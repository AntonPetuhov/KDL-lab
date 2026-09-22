namespace AnalyzerService.Contracts;

/// <summary>
/// Представляет конфигурацию одного анализатора, прочитанную из JSON.
/// Используется службой-хостом и загружаемым DLL-драйвером.
/// </summary>
public sealed class AnalyzerSettings
{
    /// <summary>Уникальный ID прибора</summary>
    public string AnalyzerId { get; set; } 
    /// <summary>Уникальное имя анализатора.</summary>
    public required string AnalyzerName { get; set; }
    /// <summary>Тип подключения ("TCPIP", "Serial", "File")</summary>
    public required string ConnectionType { get; set; }
    /// <summary>Локальный IP-адрес ЛИС, который слушает служба.</summary>
    public string? IPaddress { get; set; }
    /// <summary>Локальный TCP-порт ЛИС.</summary>
    public int Port { get; set; }
    /// <summary>Признак загрузки драйвера из DLL.</summary>
    public bool Isdll { get; set; }
    /// <summary>Путь к DLL относительно каталога службы или абсолютный путь.</summary>
    public string? DllPath { get; set; }
    /// <summary>Признак активности конфигурации.</summary>
    public bool ActiveStatus { get; set; }
    /// <summary>Признак запуска обмена.</summary>
    public bool WorkStatus { get; set; }
    /// <summary>Признак обработки результатов.</summary>
    public bool ResultHandlerStatus { get; set; }
    /// <summary>Каталог сырых результатов.</summary>
    public required string ResultsFolder { get; set; }
    /// <summary>Каталог логов.</summary>
    public string? LogsFolder { get; set; }
    /// <summary>Каталог результирующих файлов для ЛИС.</summary>
    public string? OutputFolder { get; set; }
    /// <summary>Строка подключения к БД ЛИС.</summary>
    public required string ConnectionString { get; set; }
    /// <summary>Код анализатора в выходном сообщении.</summary>
    public string? AnalyzerCode { get; set; }
    /// <summary>Код прибора в Analyzer Configuration.</summary>
    public string? AnalyzerConfigurationCode { get; set; }
    /// <summary> Формат протокола.</summary>
    public string Protocol { get; set; } = "ASTM_E1381_02_E1394_97";
}
