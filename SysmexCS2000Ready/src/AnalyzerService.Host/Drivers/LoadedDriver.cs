using AnalyzerService.Contracts;

namespace AnalyzerService.Host.Drivers;

/// <summary>
/// Владеет экземпляром драйвера и контекстом его загрузки.
/// </summary>
public class LoadedDriver(IAnalyzerDriver instance, DriverLoadContext context) : IDisposable
{
    private bool disposed;

    /// <summary>
    /// Получает загруженный драйвер.
    /// </summary>
    public IAnalyzerDriver Instance { get; } = instance;

    /// <summary>
    /// Синхронно освобождает драйвер и помечает контекст для выгрузки.
    /// </summary>
    public void Dispose()
    {
        if (disposed) return;
        disposed = true;

        if (Instance is not null) Instance.Dispose();
        if (context is not null) context.Unload();

        GC.SuppressFinalize(this); // ? нужно ли???
    }
}
