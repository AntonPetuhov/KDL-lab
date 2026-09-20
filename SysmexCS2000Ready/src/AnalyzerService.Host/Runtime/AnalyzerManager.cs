using AnalyzerService.Contracts;
using AnalyzerService.Host.Drivers;
using AnalyzerService.Host.Logging;

namespace AnalyzerService.Host.Runtime;

/// <summary>Хранит анализаторы, предотвращает дубликаты и координирует их жизненный цикл.</summary>
public sealed class AnalyzerManager(AnalyzerLoggerFactory loggerFactory) : IDisposable
{
    private readonly Dictionary<string, AnalyzerRuntime> analyzers = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Синхронно добавляет конфигурацию анализатора.</summary><param name="settings">Проверенные настройки.</param>
    public void Add(AnalyzerSettings settings)
    {
        if (!analyzers.TryAdd(settings.AnalyzerName, new AnalyzerRuntime(settings, new DriverLoader(), loggerFactory)))
            throw new InvalidOperationException($"Анализатор {settings.AnalyzerName} настроен повторно.");
    }

    /// <summary>Запускает все драйверы и ожидает их рабочие циклы.</summary><param name="token">Сигнал остановки.</param><returns>Задача до завершения всех драйверов.</returns>
    public async Task StartAllAsync(CancellationToken token)
    {
        foreach (AnalyzerRuntime analyzer in analyzers.Values) await analyzer.StartAsync(token).ConfigureAwait(false);
        await Task.WhenAll(analyzers.Values.Select(analyzer => analyzer.Completion)).ConfigureAwait(false);
    }

    /// <summary>Последовательно останавливает все драйверы и агрегирует ошибки.</summary><param name="token">Ограничение остановки.</param><returns>Задача остановки.</returns>
    public async Task StopAllAsync(CancellationToken token)
    {
        List<Exception> errors = [];
        foreach (AnalyzerRuntime analyzer in analyzers.Values)
            try { await analyzer.StopAsync(token).ConfigureAwait(false); } catch (Exception ex) { errors.Add(ex); }
        if (errors.Count != 0) throw new AggregateException("Ошибки остановки анализаторов.", errors);
    }

    /// <summary>Освобождает все runtime.</summary>
    public void Dispose() { foreach (AnalyzerRuntime analyzer in analyzers.Values) analyzer.Dispose(); }
}
