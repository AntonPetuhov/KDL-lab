using System.Net;
using AnalyzerService.Contracts;

namespace Maglumi.Snibe.Driver;

/// <summary>Управляемая DLL для HL7-интерфейса MAGLUMI X8.</summary>
public sealed class MaglumiSnibeDriver : IAnalyzerDriver
{
    private AnalyzerMaglumiX8? analyzer;
    public void Initialize(IAnalyzerLogger logger, AnalyzerSettings settings)
    {
        ArgumentNullException.ThrowIfNull(logger);
        ArgumentNullException.ThrowIfNull(settings);
        if (analyzer is not null) throw new InvalidOperationException("Драйвер уже инициализирован.");
        List<string> errors = [];
        if (!string.Equals(settings.Protocol, "MAGLUMI_X8_HL7", StringComparison.OrdinalIgnoreCase)) errors.Add("Protocol=MAGLUMI_X8_HL7");
        if (!string.Equals(settings.ConnectionType, "TCPIP", StringComparison.OrdinalIgnoreCase)) errors.Add("ConnectionType=TCPIP");
        if (!IPAddress.TryParse(settings.IPaddress, out _)) errors.Add("локальный IPaddress");
        if (settings.Port is < 1 or > 65535) errors.Add("Port 1..65535");
        if (string.IsNullOrWhiteSpace(settings.ResultsFolder)) errors.Add("ResultsFolder");
        if (string.IsNullOrWhiteSpace(settings.OutputFolder)) errors.Add("OutputFolder");
        if (string.IsNullOrWhiteSpace(settings.ConnectionString)) errors.Add("ConnectionString");
        if (string.IsNullOrWhiteSpace(settings.AnalyzerConfigurationCode)) errors.Add("AnalyzerConfigurationCode");
        if (string.IsNullOrWhiteSpace(settings.AnalyzerCode)) errors.Add("AnalyzerCode");
        if (errors.Count != 0) throw new InvalidDataException($"Для MAGLUMI X8 нужны корректные данные: {string.Join(", ", errors)}.");
        analyzer = new(logger, settings);
        logger.Service($"Драйвер {settings.AnalyzerName} инициализирован: MAGLUMI X8 HL7 2.5 / UTF-8.");
    }

    public Task RunAsync(CancellationToken cancellationToken) =>
        (analyzer ?? throw new InvalidOperationException("Драйвер не инициализирован.")).RunAsync(cancellationToken);
    public void Stop() => analyzer?.Stop();
    public void Dispose() => analyzer?.Dispose();
}
