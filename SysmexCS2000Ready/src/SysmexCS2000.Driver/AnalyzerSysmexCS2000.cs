using System.Net;
using System.Net.Sockets;
using AnalyzerService.Contracts;
using AnalyzerService.Lis;
using AnalyzerService.Transport;
using SysmexCS2000.Driver.Lis;
using SysmexCS2000.Driver.Protocol;

namespace SysmexCS2000.Driver;

/// <summary>
/// Координирует TCP-host, ASTM-сессию, запросы заказов и результаты CS-2000i.
/// Загрузка DLL остаётся ответственностью службы-хоста.
/// </summary>
public class AnalyzerSysmexCS2000 : IDisposable
{
    private readonly IAnalyzerLogger logger;
    private readonly AnalyzerSettings settings;
    private readonly TcpHost tcpHost;
    private readonly AstmSession session;
    private readonly AstmMessageParser parser = new();
    private readonly AstmMessageBuilder builder = new();
    private readonly LisRepository repository;
    private readonly SysmexResultHandler resultHandler;
    private readonly CancellationTokenSource localStop = new();
    private TcpClient? client;

    /// <summary>Создаёт обработчик и его синхронные зависимости.</summary><param name="logger">Логгер.</param><param name="settings">Настройки JSON.</param>
    public AnalyzerSysmexCS2000(IAnalyzerLogger logger, AnalyzerSettings settings)
    {
        this.logger = logger;
        this.settings = settings;
        tcpHost = new TcpHost(logger, "Sysmex CS-2000i");
        session = new AstmSession(logger);
        repository = new LisRepository(settings, logger);
        resultHandler = new SysmexResultHandler(settings, logger, repository);
    }

    /// <summary>Асинхронно принимает подключения и сообщения, поскольку TCP-ожидания нельзя выполнять синхронно без блокировки.</summary><param name="token">Сигнал остановки.</param><returns>Задача рабочего цикла.</returns>
    public async Task RunAsync(CancellationToken token)
    {
        using CancellationTokenSource linked = CancellationTokenSource.CreateLinkedTokenSource(token, localStop.Token);
        IPAddress address = IPAddress.Parse(settings.IPaddress!);
        tcpHost.Start(address, settings.Port);
        logger.Service($"Sysmex CS-2000i запущен, протокол {settings.Protocol}.");
        while (!linked.IsCancellationRequested)
        {
            try
            {
                client = await tcpHost.AcceptAsync(linked.Token).ConfigureAwait(false);
                await HandleClientAsync(client, linked.Token).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (linked.IsCancellationRequested) { break; }
            catch (Exception ex)
            {
                logger.Error("Ошибка соединения Sysmex; ожидается новое подключение IPU.", ex);
                client?.Dispose();
                client = null;
            }
        }
        logger.Service("Рабочий цикл Sysmex CS-2000i остановлен.");
    }

    /// <summary>Асинхронно обслуживает последовательные ASTM-транзакции одного IPU.</summary>
    private async Task HandleClientAsync(TcpClient connectedClient, CancellationToken token)
    {
        NetworkStream stream = connectedClient.GetStream();
        while (connectedClient.Connected && !token.IsCancellationRequested)
        {
            string raw = await session.ReceiveMessageAsync(stream, token).ConfigureAwait(false);
            logger.Protocol($"Получено ASTM-сообщение длиной {raw.Length}.");
            AstmMessage message;
            try { message = parser.Parse(raw); }
            catch (SysmexProtocolException ex) { logger.Error($"Ошибка сообщения {ex.Code}.", ex); continue; }

            if (message.Records.Any(r => r.Type == 'Q'))
            {
                string sampleId = parser.GetQuerySampleId(message);
                LisOrder? order = repository.GetOrder(sampleId);
                string response = order is null ? builder.BuildEmpty(sampleId, "999")
                    : order.Parameters.Count == 0 ? builder.BuildEmpty(sampleId, "000")
                    : builder.BuildOrder(order);
                await session.SendMessageAsync(stream, response, token).ConfigureAwait(false);
            }
            else if (message.Records.Any(r => r.Type == 'R') && settings.ResultHandlerStatus)
            {
                resultHandler.Handle(message);
            }
        }
    }

    /// <summary>Синхронно закрывает сокеты; задача завершена сразу после инициирования отмены.</summary><param name="token">Не используется, сохранён для контракта.</param><returns>Завершённая задача.</returns>
    public Task StopAsync(CancellationToken token)
    {
        localStop.Cancel();
        client?.Dispose();
        tcpHost.Stop();
        return Task.CompletedTask;
    }

    /// <summary>Освобождает transport и token source.</summary>
    public void Dispose() { tcpHost.Dispose(); localStop.Dispose(); }
}
