using AnalyzerService.Contracts;
using AnalyzerService.Transport;

namespace AnalyzerService.Host.Runtime;

/// <summary>
/// Адаптирует поток принятого TCP-клиента к контракту DLL-драйвера и передаёт
/// отметки RX/TX общему TcpHost. Жизненный цикл сокета остаётся в AnalyzerRuntime.
/// </summary>
public sealed class TcpAnalyzerConnection(Stream stream, TcpHost host) : IAnalyzerConnection
{
    /// <summary>Возвращает поток, открытый сервис-хостом.</summary>
    public Stream Stream { get; } = stream;

    /// <summary>Синхронно фиксирует принятый текст, без сетевого ожидания.</summary>
    public void RecordRead() => host.RecordRead();

    /// <summary>Синхронно фиксирует отправленный текст, без сетевого ожидания.</summary>
    public void RecordWrite() => host.RecordWrite();
}
