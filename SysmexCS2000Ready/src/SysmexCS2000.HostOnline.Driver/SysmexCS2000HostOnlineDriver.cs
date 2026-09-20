using AnalyzerService.Contracts;

namespace SysmexCS2000.HostOnline.Driver;

/// <summary>Является единственной точкой входа DLL собственного протокола Sysmex Host Online.</summary>
public sealed class SysmexCS2000HostOnlineDriver : IAnalyzerDriver
{
    private AnalyzerSysmexCS2000HostOnline? analyzer;

    /// <inheritdoc />
    public void Initialize(IAnalyzerLogger logger, AnalyzerSettings settings)
    {
        ArgumentNullException.ThrowIfNull(logger);
        ArgumentNullException.ThrowIfNull(settings);
        if (!string.Equals(settings.Protocol, "SYSMEX_HOST_ONLINE", StringComparison.OrdinalIgnoreCase))
            throw new ArgumentException("Драйвер требует Protocol=SYSMEX_HOST_ONLINE.", nameof(settings));
        analyzer = new AnalyzerSysmexCS2000HostOnline(logger, settings);
    }

    /// <inheritdoc />
    public Task RunAsync(CancellationToken cancellationToken) => (analyzer ?? throw new InvalidOperationException("Драйвер не инициализирован.")).RunAsync(cancellationToken);
    /// <inheritdoc />
    public Task StopAsync(CancellationToken cancellationToken) => analyzer?.StopAsync(cancellationToken) ?? Task.CompletedTask;
    /// <summary>Освобождает обработчик и TCP-ресурсы.</summary>
    public void Dispose() => analyzer?.Dispose();
}
