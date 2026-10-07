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
    private readonly object locker = new();

    /// <summary>
    /// Создаёт логгер и каталог журналов.
    /// </summary>
    public FileAnalyzerLogger(string root)
    {
        this.root = root;
        Directory.CreateDirectory(root);
    }

    public void Service(string message) => Write("Service", $"    Info: {message}");
    public void Transport(string message) => Write("Transport", $"    Info: {message}");
    public void Protocol(string message) => Write("Protocol",
        message.StartsWith("RX: ", StringComparison.Ordinal) || message.StartsWith("TX: ", StringComparison.Ordinal)
            ? message : $"    Info: {message}");
    public void Result(string message) => Write("Result", $"    Info: {message}");
    public void Error(string message, Exception exception) => Write("Error", $"Error: {message}{Environment.NewLine}{exception}");

    /// <summary>
    /// Добавляет одну запись в лог, сохраняя порядок сообщений.
    /// </summary>
    private void Write(string category, string message)
    {
        lock (locker)
        {
            string directory = Path.Combine(root, category);
            Directory.CreateDirectory(directory);
            string path = Path.Combine(directory, $"{category}_{DateTime.Now:yyyy-MM-dd}.log");
            File.AppendAllText(path, $"{DateTime.Now} {message}{Environment.NewLine}", Encoding.UTF8);
        }
    }
}
