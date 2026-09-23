using AnalyzerService.Contracts;
using AnalyzerService.Host.Drivers;
using AnalyzerService.Host.Logging;

namespace AnalyzerService.Host.Runtime;

/// <summary>
/// Менеджер анализаторов,
/// Хранит анализаторы, предотвращает дубликаты и координирует их жизненный цикл.
/// </summary>
public class AnalyzerManager(AnalyzerLoggerFactory loggerFactory) : IDisposable
{
    // Словарь анализаторов для запуска. Ключи сравниваются без учета регистра
    private readonly Dictionary<string, AnalyzerRuntime> analyzers = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Регистрируем новый анализатор в словаре analyzers (анализаторы, которые будут запущены). 
    /// Выбрасывается исключение, если анализатор с таким именем уже есть.
    /// </summary>
    public void Add(AnalyzerSettings settings)
    {
        // если такая пара уже есть в словаре
        if (!analyzers.TryAdd(settings.AnalyzerName, new AnalyzerRuntime(settings, new DriverLoader(), loggerFactory)))
        {
            throw new InvalidOperationException($"Анализатор {settings.AnalyzerName} настроен повторно.");
        } 
    }

    /// <summary>
    /// Запускает все драйвера приборов и ожидает их рабочие циклы.
    /// </summary>
    public async Task StartAllAsync(CancellationToken token)
    {
        foreach (AnalyzerRuntime analyzer in analyzers.Values) 
        {
            //await analyzer.StartAsync(token).ConfigureAwait(false);
            await analyzer.StartAsync(token);
        }
        await Task.WhenAll(analyzers.Values.Select(analyzer => analyzer.Completion)).ConfigureAwait(false);
    }

    /// <summary>
    /// Последовательно останавливает все драйвера и аггрегирует ошибки.
    /// </summary>
    public async Task StopAllAsync(CancellationToken token)
    {
        List<Exception> errors = [];
        foreach (AnalyzerRuntime analyzer in analyzers.Values)
        {
            try 
            { 
                await analyzer.StopAsync(token).ConfigureAwait(false); 
            } 
            catch (Exception ex) 
            { 
                errors.Add(ex); 
            }
            finally
            {
                // поидее не нужно так как есть Dispose ниже
                analyzer.Dispose(); // переиспользовать объект анализатора будет нельзя, только создать новый, тк освобождаем ресурсы
            }
        }
            
        if (errors.Count != 0) throw new AggregateException("Ошибки остановки анализаторов.", errors);
    }

    // освобождаем ресурсы
    public void Dispose() 
    {
        foreach (AnalyzerRuntime analyzer in analyzers.Values) 
        {
            analyzer.Dispose();
        }
    }
}
