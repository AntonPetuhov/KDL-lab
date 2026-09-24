using System.Text;
using AnalyzerService.Contracts;

namespace AnalyzerService.Host.Logging;

/// <summary>
/// Записывает журналы одного анализатора по категориям и дням.
/// Связан с драйвером только через IAnalyzerLogger.
/// </summary>
public sealed class FileAnalyzerLogger : IAnalyzerLogger
{
    private readonly string root;
    private readonly object gate = new();

    /// <summary>Создаёт логгер и каталог журналов.</summary><param name="root">Каталог журналов.</param>
    public FileAnalyzerLogger(string root)
    {
        this.root = root;
        Directory.CreateDirectory(root);
    }

    /// <inheritdoc />
    public void Service(string message) => Write("Service", message);
    /// <inheritdoc />
    public void Transport(string message) => Write("Transport", message);
    /// <inheritdoc />
    public void Protocol(string message) => Write("Protocol", message);
    /// <inheritdoc />
    public void Result(string message) => Write("Result", message);
    /// <inheritdoc />
    public void Error(string message, Exception exception) => Write("Error", $"{message}{Environment.NewLine}{exception}");

    /// <summary>Синхронно добавляет одну запись, сохраняя порядок сообщений.</summary>
    private void Write(string category, string message)
    {
        lock (gate)
        {
            string directory = Path.Combine(root, category);
            Directory.CreateDirectory(directory);
            string path = Path.Combine(directory, $"{category}_{DateTime.Now:yyyy-MM-dd}.log");
            File.AppendAllText(path, $"{DateTimeOffset.Now:O} {message}{Environment.NewLine}", Encoding.UTF8);
        }
    }
}
