namespace AnalyzerService.Transport;

/// <summary>
/// Связывает поток принятого клиента с диагностикой общего TcpHost.
/// Создаётся драйвером, который владеет сеансом.
/// </summary>
public class TcpAnalyzerConnection(Stream stream, TcpHost host) : ITcpConnection
{
    /// <summary>Возвращает открытый поток клиента без сетевого ожидания.</summary>
    public Stream Stream { get; } = stream;

    /// <summary> обновляет время последнего RX в TcpHost.</summary>
    public void RecordRead() => host.RecordRead();

    /// <summary> обновляет время последнего TX в TcpHost.</summary>
    public void RecordWrite() => host.RecordWrite();
}
