using System.Text;
using AnalyzerService.Contracts;
using AnalyzerService.LisDatabase;
using AnalyzerService.ResultFiles;
using SysmexCS2000.HostOnline.Driver.Lis;
using SysmexCS2000.HostOnline.Driver.Protocol;

namespace SysmexCS2000.HostOnline.Driver;

/// <summary>
/// Разбирает прикладной протокол Sysmex Host Online в уже открытом потоке.
/// Вызовы Start/Stop и владение TCP-сокетом принадлежат сервис-хосту; DLL владеет
/// только кодеком, запросом в ЛИС и очередью сырых результатов.
/// </summary>
public sealed class AnalyzerSysmexCS2000HostOnline : IDisposable
{
    private const byte Stx = 0x02;
    private const byte Etx = 0x03;
    private readonly IAnalyzerLogger logger;
    private readonly AnalyzerSettings settings;
    private readonly HostOnlineCodec codec = new();
    private readonly LisDBProvider dbProvider;
    private readonly HostOnlineResultHandler resultHandler;
    private readonly RawResultQueue resultQueue;
    private readonly Dictionary<string, SortedDictionary<int, (string Body, byte[] Raw)>> blocks = new(StringComparer.Ordinal);

    /// <summary>
    /// Синхронно создаёт обработчики без открытия сети; исключения инициализации
    /// поднимаются в AnalyzerRuntime, который пишет их в файловый журнал.
    /// </summary>
    /// <param name="logger">Журнал данного анализатора.</param>
    /// <param name="settings">Проверенный JSON.</param>
    public AnalyzerSysmexCS2000HostOnline(IAnalyzerLogger logger, AnalyzerSettings settings)
    {
        this.logger = logger;
        this.settings = settings;
        dbProvider = new LisDBProvider(settings, logger);
        resultHandler = new HostOnlineResultHandler(settings, logger, dbProvider);
        resultQueue = new RawResultQueue(settings, logger, ProcessStoredResult);
    }

    /// <summary>Синхронно запускает очередь; TCP listener запускает AnalyzerRuntime.</summary>
    /// <exception cref="IOException">Не удалось создать каталог сырых результатов.</exception>
    public void Start()
    {
        if (settings.ResultHandlerStatus) resultQueue.Start();
        logger.Service($"Host Online драйвер {settings.AnalyzerName} запущен; ResultHandlerStatus={settings.ResultHandlerStatus}.");
    }

    /// <summary>
    /// Асинхронно принимает тексты в одном уже принятом соединении. Здесь ожидаются
    /// только байты потока; SQL-запрос GetOrder и запись .raw выполняются синхронно.
    /// Ошибки логируются и пробрасываются в AnalyzerRuntime для закрытия сеанса.
    /// </summary>
    /// <param name="connection">Поток и счётчики общего TCP-host.</param>
    /// <param name="token">Отмена службы.</param>
    /// <returns>Задача до закрытия соединения прибором или сервисом.</returns>
    public async Task HandleConnectionAsync(IAnalyzerConnection connection, CancellationToken token)
    {
        ArgumentNullException.ThrowIfNull(connection);
        blocks.Clear();
        try
        {
            while (!token.IsCancellationRequested)
            {
                (string body, byte[] rawFrame) = await ReadTextAsync(connection.Stream, token).ConfigureAwait(false);
                connection.RecordRead();
                logger.Protocol($"RX: {body}");
                if (body.StartsWith("DS21", StringComparison.Ordinal))
                {
                    logger.Protocol("Получен DS21: информационный текст, результат не создаётся.");
                    continue;
                }
                (string Body, byte[] Raw)? complete = AddBlock(body, rawFrame);
                if (complete is null) continue;

                // Если сообщение с запросом задания
                if (complete.Value.Body[0] == 'R')
                {
                    HostOnlineInquiry inquiry = codec.ParseInquiry(complete.Value.Body);
                    LisOrder? order = dbProvider.GetOrder(inquiry.Header.SampleId);
                    string emptyCode = order is null ? "999" : "000";
                    IReadOnlyList<string> responses = codec.BuildOrder(inquiry, order, emptyCode);
                    logger.Protocol($"Задание {inquiry.Header.SampleId}: кандидаты ЛИС=[{string.Join(",", order?.Parameters ?? [])}], " +
                                    $"коды S221=[{string.Join(",", responses.SelectMany(HostOnlineCodec.ReadOrderCodes))}], " +
                                    $"блоков={responses.Count}; ID Information={responses[0][42]}, " +
                                    $"поле пациента='{responses[0].Substring(43, 15).TrimEnd()}'.");
                    foreach (string response in responses)
                        await WriteTextAsync(connection, response, token).ConfigureAwait(false);
                }

                // Если сообщение с результатом
                else if (complete.Value.Body[0] == 'D')
                {
                    string sampleId = complete.Value.Body.Substring(27, 15).Trim();
                    // Если Контроль качества
                    if (complete.Value.Body[8] == 'C')
                        resultQueue.SaveQualityControl(sampleId, complete.Value.Raw);
                    else
                        resultQueue.SaveResult(sampleId, complete.Value.Raw);
                }
                else logger.Protocol($"Текст типа {complete.Value.Body[0]} принят без прикладной обработки.");
            }
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested) { }
        catch (EndOfStreamException) { throw; }
        catch (Exception ex)
        {
            logger.Error($"Host Online: исключение при обработке сеанса {settings.AnalyzerName}; пробрасывается в TCP-host.", ex);
            throw;
        }
        finally { blocks.Clear(); }
    }

    /// <summary>
    /// Синхронно объединяет несколько кадров сообщения, оставляя точные входные байты
    /// для сохранения результатов. Ошибки номеров являются ошибками протокола.
    /// </summary>
    /// <param name="body">Тело принятого кадра.</param>
    /// <param name="rawFrame">Байты с STX/ETX.</param>
    /// <returns>Полное сообщение либо null до прихода последнего блока.</returns>
    private (string Body, byte[] Raw)? AddBlock(string body, byte[] rawFrame)
    {
        if (body.Length < HostOnlineCodec.HeaderLength)
            throw new HostOnlineProtocolException("Text Length Error", "Короткий блок.");
        if (!int.TryParse(body.AsSpan(4, 2), out int number) ||
            !int.TryParse(body.AsSpan(6, 2), out int total) || number < 1 || total < number || total > 99)
            throw new HostOnlineProtocolException("Block Number Error", "Некорректный номер блока.");
        string key = body[0] + body.Substring(27, 15);
        if (!blocks.TryGetValue(key, out SortedDictionary<int, (string Body, byte[] Raw)>? parts))
        {
            parts = new();
            blocks.Add(key, parts);
        }
        parts[number] = (body, rawFrame);
        if (parts.Count < total) return null;
        if (parts.Keys.First() != 1 || parts.Keys.Last() != total)
            throw new HostOnlineProtocolException("Block Number Error", "Последовательность блоков неполна.");
        string complete = parts[1].Body + string.Concat(parts.Skip(1).Select(p => p.Value.Body[HostOnlineCodec.HeaderLength..]));
        byte[] raw = parts.Values.SelectMany(p => p.Raw).ToArray();
        blocks.Remove(key);
        return (complete[..4] + "01" + total.ToString("00") + complete[8..], raw);
    }

    /// <summary>
    /// Синхронно восстанавливает сохранённые STX/ETX-кадры и создаёт выход ЛИС
    /// на выделенном потоке RawResultQueue; ошибка переводит .raw в errors.
    /// </summary>
    /// <param name="raw">Исходные байты результата.</param>
    /// <param name="sourceId">Имя файла для идемпотентного выхода .res/.ok.</param>
    private void ProcessStoredResult(byte[] raw, string sourceId)
    {
        List<string> parts = [];
        for (int offset = 0; offset < raw.Length;)
        {
            if (raw[offset++] != Stx) throw new InvalidDataException("Ожидался STX сырого кадра.");
            int end = Array.IndexOf(raw, Etx, offset);
            if (end < 0) throw new InvalidDataException("Нет ETX сырого кадра.");
            parts.Add(Encoding.ASCII.GetString(raw, offset, end - offset));
            offset = end + 1;
        }
        if (parts.Count == 0 || parts[0].Length < HostOnlineCodec.HeaderLength ||
            !int.TryParse(parts[0].AsSpan(6, 2), out int total) || parts.Count != total)
            throw new InvalidDataException("Неполное сырое сообщение Host Online.");
        for (int i = 0; i < parts.Count; i++)
            if (parts[i].Length < HostOnlineCodec.HeaderLength ||
                !int.TryParse(parts[i].AsSpan(4, 2), out int number) || number != i + 1)
                throw new InvalidDataException("Неверный номер блока сырого сообщения.");
        string body = parts[0] + string.Concat(parts.Skip(1).Select(p => p[HostOnlineCodec.HeaderLength..]));
        body = body[..4] + "01" + total.ToString("00") + body[8..];
        resultHandler.Handle(codec.ParseResult(body), sourceId);
    }

    /// <summary>
    /// Асинхронно читает один ограниченный STX/ETX-кадр; ожидание TCP-байтов
    /// нельзя выполнить синхронно без блокировки потока службы.
    /// </summary>
    /// <param name="stream">Открытый поток из сервиса.</param>
    /// <param name="token">Отмена чтения.</param>
    /// <returns>ASCII-тело и точные входные байты кадра.</returns>
    private static async Task<(string Body, byte[] Raw)> ReadTextAsync(Stream stream, CancellationToken token)
    {
        List<byte> body = [];
        bool started = false;
        byte[] one = new byte[1];
        while (true)
        {
            int count = await stream.ReadAsync(one, token).ConfigureAwait(false);
            if (count == 0) throw new EndOfStreamException("Прибор закрыл соединение.");
            if (!started) { if (one[0] == Stx) started = true; continue; }
            if (one[0] == Etx) return (Encoding.ASCII.GetString(body.ToArray()), [Stx, .. body, Etx]);
            body.Add(one[0]);
            if (body.Count > 253)
                throw new HostOnlineProtocolException("Text Length Error", "Кадр превышает 255 байт со STX/ETX.");
        }
    }

    /// <summary>
    /// Асинхронно пишет один S221 в поток и фиксирует TX. Проверка ASCII предотвращает
    /// незаметную замену символов на '?', характерную для Encoding.ASCII.GetBytes.
    /// </summary>
    /// <param name="connection">Поток и счётчик TX.</param>
    /// <param name="body">Подготовленный текст S221.</param>
    /// <param name="token">Отмена записи.</param>
    /// <returns>Задача завершения отправки.</returns>
    private async Task WriteTextAsync(IAnalyzerConnection connection, string body, CancellationToken token)
    {
        if (body.Any(c => c > 0x7F))
            throw new HostOnlineProtocolException("Text Distinction Error", "Исходящий текст содержит не-ASCII символы.");
        byte[] bytes = [Stx, .. Encoding.ASCII.GetBytes(body), Etx];
        if (bytes.Length > 255)
            throw new HostOnlineProtocolException("Text Length Error", "Исходящий кадр превышает 255 байт.");
        await connection.Stream.WriteAsync(bytes, token).ConfigureAwait(false);
        await connection.Stream.FlushAsync(token).ConfigureAwait(false);
        connection.RecordWrite();
        logger.Protocol($"TX: {body}");
    }

    /// <summary>Синхронно завершает обработку накопленных файлов после закрытия TCP host.</summary>
    public void Stop() { if (settings.ResultHandlerStatus) resultQueue.Stop(); }

    /// <summary>Синхронно освобождает очередь; TCP-сокетом владеет AnalyzerRuntime.</summary>
    public void Dispose() => resultQueue.Dispose();
}
