using System.Net;
using System.Net.Sockets;
using System.Text;
using AnalyzerService.Contracts;
using AnalyzerService.Lis;
using SysmexCS2000.HostOnline.Driver.Lis;
using SysmexCS2000.HostOnline.Driver.Protocol;
using SysmexCS2000.HostOnline.Driver.Transport;

namespace SysmexCS2000.HostOnline.Driver;

/// <summary>Координирует TCP, R221/S221, D121/D221, ЛИС и результаты собственного протокола.</summary>
public sealed class AnalyzerSysmexCS2000HostOnline : IDisposable
{
    private const byte Stx = 0x02;
    private const byte Etx = 0x03;
    private readonly IAnalyzerLogger logger;
    private readonly AnalyzerSettings settings;
    private readonly HostOnlineTcpHost host;
    private readonly HostOnlineCodec codec = new();
    private readonly LisRepository repository;
    private readonly HostOnlineResultHandler resultHandler;
    private readonly CancellationTokenSource localStop = new();
    private readonly Dictionary<string, SortedDictionary<int, string>> blocks = new(StringComparer.Ordinal);
    private TcpClient? client;

    /// <summary>Создаёт анализатор и синхронные зависимости.</summary><param name="logger">Логгер.</param><param name="settings">JSON-настройки.</param>
    public AnalyzerSysmexCS2000HostOnline(IAnalyzerLogger logger, AnalyzerSettings settings)
    {
        this.logger = logger;
        this.settings = settings;
        host = new HostOnlineTcpHost(logger);
        repository = new LisRepository(settings, logger);
        resultHandler = new HostOnlineResultHandler(settings, logger, repository);
    }

    /// <summary>Асинхронно ожидает подключения и данные TCP без блокировки потока службы.</summary><param name="token">Сигнал остановки.</param><returns>Рабочая задача.</returns>
    public async Task RunAsync(CancellationToken token)
    {
        using CancellationTokenSource linked = CancellationTokenSource.CreateLinkedTokenSource(token, localStop.Token);
        host.Start(IPAddress.Parse(settings.IPaddress!), settings.Port);
        logger.Service("Sysmex CS-2000i Host Online запущен.");
        while (!linked.IsCancellationRequested)
        {
            try
            {
                client = await host.AcceptAsync(linked.Token).ConfigureAwait(false);
                await HandleClientAsync(client, linked.Token).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (linked.IsCancellationRequested) { break; }
            catch (Exception ex)
            {
                logger.Error("Ошибка Sysmex Host Online; ожидается новое подключение.", ex);
                client?.Dispose();
                client = null;
                blocks.Clear();
            }
        }
    }

    /// <summary>Асинхронно принимает ограниченные STX/ETX-тексты одного соединения.</summary>
    private async Task HandleClientAsync(TcpClient connected, CancellationToken token)
    {
        NetworkStream stream = connected.GetStream();
        while (connected.Connected && !token.IsCancellationRequested)
        {
            string block = await ReadTextAsync(stream, token).ConfigureAwait(false);
            logger.Protocol($"Host Online RX: {block}");
            string? complete = AddBlock(block);
            if (complete is null) continue;
            if (complete[0] == 'R')
            {
                HostOnlineInquiry inquiry = codec.ParseInquiry(complete);
                LisOrder? order = repository.GetOrder(inquiry.Header.SampleId);
                string emptyCode = order is null ? "999" : "000";
                foreach (string response in codec.BuildOrder(inquiry, order, emptyCode))
                    await WriteTextAsync(stream, response, token).ConfigureAwait(false);
            }
            else if (complete[0] == 'D' && settings.ResultHandlerStatus)
            {
                resultHandler.Handle(codec.ParseResult(complete));
            }
            else
            {
                logger.Protocol($"Текст типа {complete[0]} принят без прикладной обработки.");
            }
        }
    }

    /// <summary>Собирает разделённые тексты по номерам блоков и возвращает полное тело.</summary>
    private string? AddBlock(string body)
    {
        if (body.Length < HostOnlineCodec.HeaderLength) throw new HostOnlineProtocolException("Text Length Error", "Короткий блок.");
        int number = int.Parse(body.Substring(4, 2));
        int total = int.Parse(body.Substring(6, 2));
        string key = body[0] + body.Substring(27, 15);
        if (!blocks.TryGetValue(key, out SortedDictionary<int, string>? parts)) blocks[key] = parts = [];
        parts[number] = body;
        if (parts.Count < total) return null;
        if (parts.Keys.Count != total || parts.Keys.First() != 1 || parts.Keys.Last() != total)
            throw new HostOnlineProtocolException("Block Number Error", "Последовательность блоков неполна.");
        string complete = parts[1] + string.Concat(parts.Skip(1).Select(item => item.Value[HostOnlineCodec.HeaderLength..]));
        blocks.Remove(key);
        return complete[..4] + "01" + total.ToString("00") + complete[8..];
    }

    /// <summary>Асинхронно читает один текст между STX и ETX; TCP не имеет собственных границ сообщений.</summary>
    private static async Task<string> ReadTextAsync(NetworkStream stream, CancellationToken token)
    {
        List<byte> body = [];
        bool started = false;
        byte[] one = new byte[1];
        while (true)
        {
            int count = await stream.ReadAsync(one, token).ConfigureAwait(false);
            if (count == 0) throw new EndOfStreamException("IPU закрыл соединение.");
            if (!started) { if (one[0] == Stx) started = true; continue; }
            if (one[0] == Etx) return Encoding.ASCII.GetString(body.ToArray());
            body.Add(one[0]);
            if (body.Count > 253) throw new HostOnlineProtocolException("Text Length Error", "Текст превышает 255 символов со STX/ETX.");
        }
    }

    /// <summary>Асинхронно отправляет STX, тело и ETX без ACK/NAK согласно TCP-разделу PDF.</summary>
    private async Task WriteTextAsync(NetworkStream stream, string body, CancellationToken token)
    {
        byte[] bytes = [Stx, .. Encoding.ASCII.GetBytes(body), Etx];
        if (bytes.Length > 255) throw new HostOnlineProtocolException("Text Length Error", "Исходящий текст превышает 255 символов.");
        await stream.WriteAsync(bytes, token).ConfigureAwait(false);
        await stream.FlushAsync(token).ConfigureAwait(false);
        logger.Protocol($"Host Online TX: {body}");
    }

    /// <summary>Синхронно инициирует остановку, закрывая активный TCP.</summary><param name="token">Параметр контракта.</param><returns>Завершённая задача.</returns>
    public Task StopAsync(CancellationToken token) { localStop.Cancel(); client?.Dispose(); host.Stop(); return Task.CompletedTask; }
    /// <summary>Освобождает ресурсы.</summary>
    public void Dispose() { host.Dispose(); localStop.Dispose(); }
}
