namespace SysmexCS2000.HostOnline.Driver.Protocol;

/// <summary>
/// Представляет общий 58-символьный заголовок Sysmex Host Online.
/// </summary>
public record HostOnlineHeader(
    char type, char subtype, string version, int blockNumber, int totalBlocks,
    char sampleType, string date, string time, string rackNumber,
    string tubePosition, string sampleId, char idInformation, string patientName);

/// <summary>
/// Представляет запрос заказа R221 от IPU.
/// </summary>
public record HostOnlineInquiry(HostOnlineHeader Header, IReadOnlyList<string> ExistingParameters);

/// <summary>
/// Представляет один результат: код 3, данные 5, флаг 1.
/// </summary>
public record HostOnlineResultItem(string ParameterCode, string Data, char Flag);

/// <summary>
/// Представляет блок результатов D121/D221.
/// </summary>
public record HostOnlineResult(HostOnlineHeader Header, IReadOnlyList<HostOnlineResultItem> Items);
