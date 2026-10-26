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
    /// Освобождает драйвер и помечает контекст для выгрузки.
    /// </summary>
    public void Dispose()
    {
        if (disposed) 
            return;

        disposed = true;

        // Выгрузка контекста должна быть запрошена и при ошибке Dispose драйвера.
        // Исключение DLL не скрывается: AnalyzerManager запишет его в Error.
        try 
        { 
            Instance.Dispose(); 
        }
        finally 
        { 
            context.Unload(); 
        }
    }
}
