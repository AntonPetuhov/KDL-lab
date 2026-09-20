using AnalyzerService.Contracts;

namespace SysmexCS2000.Driver;

/// <summary>Является единственной публичной точкой входа DLL-плагина Sysmex CS-2000i.</summary>
public sealed class SysmexCS2000Driver : IAnalyzerDriver
{
    private AnalyzerSysmexCS2000? analyzer;

    /// <inheritdoc />
    public void Initialize(IAnalyzerLogger logger, AnalyzerSettings settings)
    {
        ArgumentNullException.ThrowIfNull(logger);
        ArgumentNullException.ThrowIfNull(settings);
        if (!string.Equals(settings.Protocol, "ASTM_E1381_02_E1394_97", StringComparison.OrdinalIgnoreCase))
            throw new ArgumentException("Драйвер требует Protocol=ASTM_E1381_02_E1394_97.", nameof(settings));
        if (analyzer is not null) throw new InvalidOperationException("Драйвер уже инициализирован.");
        analyzer = new AnalyzerSysmexCS2000(logger, settings);
    }

    /// <inheritdoc />
    public Task RunAsync(CancellationToken cancellationToken) =>
        (analyzer ?? throw new InvalidOperationException("Драйвер не инициализирован.")).RunAsync(cancellationToken);

    /// <inheritdoc />
    public Task StopAsync(CancellationToken cancellationToken) => analyzer?.StopAsync(cancellationToken) ?? Task.CompletedTask;

    /// <summary>Освобождает объект анализатора и сетевые ресурсы.</summary>
    public void Dispose() => analyzer?.Dispose();
}
