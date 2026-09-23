using System.Text;
using AnalyzerService.Contracts;

namespace AnalyzerService.Host.Logging;

/// <summary>
/// Записывает журналы одного анализатора по категориям и дням.
/// Связан с драйвером только через IAnalyzerLogger.
/// </summary>
public class AnalyzerLogger : IAnalyzerLogger
{
    private readonly string root;
    private readonly object locker = new();

    public AnalyzerLogger(string root)
    {
        this.root = root;
        Directory.CreateDirectory(root);
    }

    public void Service(string message) => Write("Service", message);
    public void Transport(string message) => Write("Transport", message);
    public void Protocol(string message) => Write("Protocol", message);
    public void Result(string message) => Write("Result", message);
    public void Error(string message, Exception exception) => Write("Error", $"{message}{Environment.NewLine}{exception}");

    private void Write(string category, string message)
    {
        lock (locker)
        {
            string directory = Path.Combine(root, category);
            if (!Directory.Exists(directory))
            {
                Directory.CreateDirectory(directory);
            }
                
            string logfileName = Path.Combine(directory, $"{category}_{DateTime.Now:yyyy-MM-dd}.log");
            File.AppendAllText(logfileName, $"{DateTimeOffset.Now:O} {message}{Environment.NewLine}", Encoding.UTF8);
        }
    }
}
