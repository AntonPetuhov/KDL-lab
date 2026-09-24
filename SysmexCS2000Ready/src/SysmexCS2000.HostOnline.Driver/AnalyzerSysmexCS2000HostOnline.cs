using System.Net;
using System.Net.Sockets;
using System.Text;
using AnalyzerService.Contracts;
using AnalyzerService.LisDatabase;
using AnalyzerService.Transport;
using SysmexCS2000.HostOnline.Driver.Lis;
using SysmexCS2000.HostOnline.Driver.Protocol;

namespace SysmexCS2000.HostOnline.Driver;

/// <summary>Координирует работу реализации драйвера анализатора</summary>
public class AnalyzerSysmexCS2000HostOnline : IDisposable
{
    #region Управляющие символы ASTM
    private const byte STX = 0x02;
    private const byte ETX = 0x03;
    #endregion

    private readonly IAnalyzerLogger logger;
    private readonly AnalyzerSettings settings;
    private readonly TcpHost host;
    private readonly HostOnlineCodec codec = new();
    private readonly LisDBProvider dbProvider;
    private readonly HostOnlineResultHandler resultHandler;
    private readonly HostOnlineQualityControlHandler qualityControlHandler;
    private readonly CancellationTokenSource localStop = new();
    private readonly Dictionary<string, SortedDictionary<int, string>> blocks = new(StringComparer.Ordinal); // для складывания фреймов
    private TcpClient? client;

    /// <summary>Создаёт анализатор и синхронные зависимости.</summary>
    public AnalyzerSysmexCS2000HostOnline(IAnalyzerLogger logger, AnalyzerSettings settings)
    {
        this.logger = logger;
        this.settings = settings;
        host = new TcpHost(logger, "Sysmex CS-2000i Host Online");
        dbProvider = new LisDBProvider(settings, logger);
        resultHandler = new HostOnlineResultHandler(settings, logger, dbProvider);
        qualityControlHandler = new HostOnlineQualityControlHandler(settings, logger);
    }

    /// <summary>Асинхронно ожидает подключения и данные TCP без блокировки потока службы.</summary><param name="token">Сигнал остановки.</param><returns>Рабочая задача.</returns>
    public async Task RunAsync(CancellationToken token)
    {
        // Проверка, что Initialize был вызван
        if (logger == null || host == null || dbProvider == null || resultHandler == null)
            throw new InvalidOperationException("Драйвер не инициализирован. Вызовите Initialize().");
        // Освобождаем старый CTS, если есть
        // using освобождает?
        using CancellationTokenSource linked = CancellationTokenSource.CreateLinkedTokenSource(token, localStop.Token);

        if (settings == null)
            throw new InvalidOperationException("Настройки не инициализированы.");

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
            catch (Exception) when (linked.IsCancellationRequested) { break; }
            catch (EndOfStreamException)
            {
                logger.Transport("IPU закрыл соединение Host Online; сервер ожидает новое подключение.");
                blocks.Clear();
            }
            catch (Exception ex)
            {
                host.RecordError(ex);
                logger.Error("Ошибка Sysmex Host Online; ожидается новое подключение.", ex);
                blocks.Clear();
            }
            finally
            {
                if (client is not null) { host.ReleaseClient(client); client = null; }
            }
        }
    }

    /// <summary>
    /// Асинхронно принимает ограниченные STX/ETX-фреймы одного соединения.
    /// </summary>
    private async Task HandleClientAsync(TcpClient connectedClient, CancellationToken token)
    {
        NetworkStream stream = connectedClient.GetStream();
        while (connectedClient.Connected && !token.IsCancellationRequested)
        {
            // читаем полный фрейм
            string block = await ReadTextAsync(stream, token).ConfigureAwait(false);
            host.RecordRead();
            logger.Protocol($"RX: {block}");
            // DS21 - текст информации о пробе, не содержит блоков результата.
            if (block.StartsWith("DS21", StringComparison.Ordinal))
            {
                logger.Protocol("Получен DS21 с информацией о пробе; результат не создаётся.");
                continue;
            }
            // собираем полное сообщение
            string? completeMsg = AddBlock(block);

            if (completeMsg is null) 
                continue;
            // Если запрос задания, сообщение начинается с R
            if (completeMsg[0] == 'R')
            {
                HostOnlineInquiry inquiry = codec.ParseInquiry(completeMsg);
                LisOrder? order = dbProvider.GetOrder(inquiry.Header.SampleId);
                string emptyCode = order is null ? "999" : "000";
                foreach (string response in codec.BuildOrder(inquiry, order, emptyCode))
                    await WriteTextAsync(stream, response, token).ConfigureAwait(false);
            }
            else if (completeMsg[0] == 'D' && settings.ResultHandlerStatus)
            {
                HostOnlineResult result = codec.ParseResult(completeMsg);
                if (result.Header.SampleType == 'C') qualityControlHandler.Handle(result);
                else resultHandler.Handle(result);
            }
            else
            {
                logger.Protocol($"Текст типа {completeMsg[0]} принят без прикладной обработки.");
            }
        }
    }

    #region Сборка полного сообщения
    /// <summary>
    /// Собирает разделённые фреймы по номерам блоков и возвращает полное тело, полное сообщение.
    /// Нужно, если прибор будет посылать большое сообщение, которое будет разделено на блоки
    /// </summary>
    private string? AddBlock(string body)
    {
        // Проверка минимальной длины
        if (body.Length < HostOnlineCodec.HeaderLength) 
            throw new HostOnlineProtocolException("Text Length Error", "Короткий блок.");
        // Извлечение номера блока и общего числа блоков
        int number = int.Parse(body.Substring(4, 2));
        int total = int.Parse(body.Substring(6, 2));
        // Тип + Sample ID
        string key = body[0] + body.Substring(27, 15);
        // Если для данного ключа еще нет записи в словаре
        // Через out-параметр parts возвращает само значение, если ключ найден.
        if (!blocks.TryGetValue(key, out SortedDictionary<int, string>? parts))
        {
            // создаём пустой словарь, куда будут складываться блок сообщения
            parts = new SortedDictionary<int, string>();
            blocks[key] = parts;
        }
        // помещаем текущий фрейм в этот словарь
        parts[number] = body;
        // Если количество блоков, которые мы сложили, меньше заявленного кол-ва блоков total - выходим, полное сообщение еще не готово
        if (parts.Count < total) 
            return null;
        // Проверяем полноту последовательности, все ли блоки собрали
        if (parts.Keys.Count != total || parts.Keys.First() != 1 || parts.Keys.Last() != total)
            throw new HostOnlineProtocolException("Block Number Error", "Последовательность блоков неполна.");
        // собираем целое сообщение
        // берем первый блок, с заголовком, берем остальные части (skip проспускает 1 элемент словаря), и удаляем у них заголовок, после склеиваем в одну строку concat
        string completeMessage = parts[1] + string.Concat(parts.Skip(1).Select(item => item.Value[HostOnlineCodec.HeaderLength..]));
        // очистка буфера
        blocks.Remove(key);

        // нормализация строки, но поидее избыточно, можно просто возвращать completeMessage
        return completeMessage[..4] + "01" + total.ToString("00") + completeMessage[8..];
    }
    #endregion

    #region чтение данных из потока
    /// <summary>
    /// Читает один текст между STX и ETX
    /// </summary>
    private static async Task<string> ReadTextAsync(NetworkStream stream, CancellationToken token)
    {
        List<byte> body = []; // накопитель байт тела фрейма
        bool started = false; // флаг, когда начали читать тело фрейма
        byte[] one = new byte[1];

        while (true)
        {
            // Читаем один байт из потока
            int received_bytes = await stream.ReadAsync(one, token).ConfigureAwait(false);
            // Если поток закрыт, пробрасываем исключение
            if (received_bytes == 0) 
                throw new EndOfStreamException("IPU закрыл соединение.");
            // Пока не встретили STX — игнорируем всё
            if (!started) 
            { 
                if (one[0] == STX) started = true; // начинаем накапливать байты, читаем тело фрейма
                continue; 
            }
            // Если ETX - возвращаем накопленное тело сообщения
            if (one[0] == ETX) 
                return Encoding.ASCII.GetString(body.ToArray());
            // Иначе добавляем байт в тело
            body.Add(one[0]);
            // Контроль длины фрейма: STX + тело + ETX ≤ 255
            if (body.Count > 253) 
                throw new HostOnlineProtocolException("Text Length Error", "Текст превышает 255 символов со STX/ETX.");
        }
    }

    #endregion

    /// <summary>Асинхронно отправляет STX, тело и ETX без ACK/NAK согласно TCP-разделу PDF.</summary>
    private async Task WriteTextAsync(NetworkStream stream, string body, CancellationToken token)
    {
        byte[] bytes = [STX, .. Encoding.ASCII.GetBytes(body), ETX];
        if (bytes.Length > 255) throw new HostOnlineProtocolException("Text Length Error", "Исходящий текст превышает 255 символов.");
        await stream.WriteAsync(bytes, token).ConfigureAwait(false);
        await stream.FlushAsync(token).ConfigureAwait(false);
        host.RecordWrite();
        logger.Protocol($"Host Online TX: {body}");
    }

    /// <summary>Синхронно инициирует остановку, закрывая активный TCP.</summary><param name="token">Параметр контракта.</param><returns>Завершённая задача.</returns>
    public Task StopAsync(CancellationToken token) { localStop.Cancel(); client?.Dispose(); host.Stop(); return Task.CompletedTask; }
    /// <summary>Освобождает ресурсы.</summary>
    public void Dispose() { host.Dispose(); localStop.Dispose(); }
}
