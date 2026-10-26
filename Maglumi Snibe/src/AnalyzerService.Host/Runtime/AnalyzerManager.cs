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
    /// Исключение, если анализатор с таким именем уже есть.
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
        IAnalyzerLogger serviceLog = loggerFactory.CreateServiceLogger();

        foreach (AnalyzerRuntime analyzer in analyzers.Values) 
        {
            //await analyzer.StartAsync(token).ConfigureAwait(false);
            await analyzer.StartAsync(token);
            serviceLog.Service($"Менеджер запустил анализатор {analyzer.Name}");
        }

        serviceLog.Service($"Ждем когда все анализаторы завершат работу");
        // ждем когда все задачи завершатся
        await Task.WhenAll(analyzers.Values.Select(analyzer => analyzer.Completion)).ConfigureAwait(false);
    }

    /// <summary>
    /// Последовательно останавливает все драйверы и агрегирует ошибки.
    /// Каждое перехваченное исключение сохраняется в файл до повторного выбрасывания.
    /// </summary>
    public async Task StopAllAsync(CancellationToken token)
    {
        List<Exception> errors = [];
        IAnalyzerLogger serviceLog = loggerFactory.CreateServiceLogger();
        foreach (AnalyzerRuntime analyzer in analyzers.Values)
        {
            try 
            { 
                await analyzer.StopAsync(token).ConfigureAwait(false); 
            } 
            catch (Exception ex) 
            { 
                serviceLog.Error($"{analyzer.Name}: исключение остановки; будет включено в AggregateException.", ex);
                errors.Add(ex); 
            }
            finally
            {
                try 
                { 
                    analyzer.Dispose(); 
                }
                catch (Exception ex)
                {
                    serviceLog.Error($"{analyzer.Name}: исключение освобождения ресурсов; будет включено в AggregateException.", ex);
                    errors.Add(ex);
                }
            }
        }
            
        if (errors.Count != 0) 
            throw new AggregateException("Ошибки остановки анализаторов.", errors);
    }

    /// <summary>
    /// Освобождает ресурсы всех анализаторов; ошибки пишет в файл и аггрегирует.
    /// </summary>
    public void Dispose() 
    {
        List<Exception> errors = [];
        IAnalyzerLogger serviceLog = loggerFactory.CreateServiceLogger();
        foreach (AnalyzerRuntime analyzer in analyzers.Values) 
        {
            try 
            { 
                analyzer.Dispose(); 
            }
            catch (Exception ex)
            {
                serviceLog.Error($"{analyzer.Name}: исключение освобождения ресурсов при Dispose.", ex);
                errors.Add(ex);
            }
        }
        if (errors.Count != 0) 
            throw new AggregateException("Ресурсы одного или нескольких анализаторов не удалось освободить. Есть ошибки освобождения ресурсов анализаторов.", errors);
    }
}
