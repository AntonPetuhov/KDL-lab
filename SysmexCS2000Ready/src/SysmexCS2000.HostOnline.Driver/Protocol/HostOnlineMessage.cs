namespace SysmexCS2000.HostOnline.Driver.Protocol;

/// <summary>
/// Представляет общий 58-символьный заголовок Sysmex Host Online.
/// </summary>
public record HostOnlineHeader(
    char Kind, char Subtype, string Version, int BlockNumber, int TotalBlocks,
    char SampleType, string Date, string Time, string RackNumber,
    string TubePosition, string SampleId, char IdInformation, string PatientName);

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
