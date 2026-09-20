using System.Net.Sockets;
using System.Text;
using AnalyzerService.Contracts;

namespace SysmexCS2000.Driver.Protocol;

/// <summary>
/// Реализует ASTM E1381-02: ENQ/ACK/NAK/EOT, кадры, нумерацию,
/// максимум шесть попыток и таймауты 15/30 секунд из спецификации.
/// </summary>
public sealed class AstmSession(IAnalyzerLogger logger)
{
    private static readonly TimeSpan SenderTimeout = TimeSpan.FromSeconds(15);
    private static readonly TimeSpan ReceiverTimeout = TimeSpan.FromSeconds(30);
    private readonly AstmFrameCodec codec = new();

    /// <summary>
    /// Асинхронно принимает одно полное сообщение; сеть требует неблокирующего ожидания.
    /// </summary>
    /// <param name="stream">TCP-поток.</param><param name="token">Сигнал остановки.</param><returns>Текст записей.</returns>
    public async Task<string> ReceiveMessageAsync(NetworkStream stream, CancellationToken token)
    {
        byte first = await ReadByteAsync(stream, ReceiverTimeout, token).ConfigureAwait(false);
        if (first != AstmControl.Enq) throw new SysmexProtocolException("Establishment Error", $"Ожидался ENQ, получен 0x{first:X2}.");
        await WriteControlAsync(stream, AstmControl.Ack, token).ConfigureAwait(false);
        StringBuilder text = new();
        int expectedNumber = 1;

        while (true)
        {
            byte marker = await ReadByteAsync(stream, ReceiverTimeout, token).ConfigureAwait(false);
            if (marker == AstmControl.Eot) return text.ToString();
            if (marker != AstmControl.Stx)
            {
                await WriteControlAsync(stream, AstmControl.Nak, token).ConfigureAwait(false);
                continue;
            }

            byte[] raw = await ReadFrameRemainderAsync(stream, token).ConfigureAwait(false);
            try
            {
                AstmFrame frame = codec.Decode([AstmControl.Stx, .. raw]);
                if (frame.Number == (expectedNumber + 7) % 8)
                {
                    // Повтор предыдущего кадра означает, что ACK был потерян.
                    // Подтверждаем его снова, не дублируя текст сообщения.
                    await WriteControlAsync(stream, AstmControl.Ack, token).ConfigureAwait(false);
                    continue;
                }
                if (frame.Number != expectedNumber)
                    throw new SysmexProtocolException("Frame Number Error", $"Ожидался кадр {expectedNumber}, получен {frame.Number}.");
                text.Append(frame.Text);
                expectedNumber = (expectedNumber + 1) % 8;
                await WriteControlAsync(stream, AstmControl.Ack, token).ConfigureAwait(false);
            }
            catch (SysmexProtocolException ex)
            {
                logger.Protocol($"NAK: {ex.Code}: {ex.Message}");
                await WriteControlAsync(stream, AstmControl.Nak, token).ConfigureAwait(false);
            }
        }
    }

    /// <summary>
    /// Асинхронно отправляет сообщение, ожидая ACK для ENQ и каждого кадра.
    /// </summary>
    /// <param name="stream">TCP-поток.</param><param name="message">ASTM-текст.</param><param name="token">Сигнал остановки.</param><returns>Задача передачи.</returns>
    public async Task SendMessageAsync(NetworkStream stream, string message, CancellationToken token)
    {
        await WriteControlAsync(stream, AstmControl.Enq, token).ConfigureAwait(false);
        byte response = await ReadByteAsync(stream, SenderTimeout, token).ConfigureAwait(false);
        if (response == AstmControl.Enq)
        {
            logger.Protocol("Коллизия ENQ: приоритет передан IPU; host ждёт 20 секунд.");
            await Task.Delay(TimeSpan.FromSeconds(20), token).ConfigureAwait(false);
            throw new SysmexProtocolException("ENQ Collision", "IPU получил приоритет передачи.");
        }
        if (response != AstmControl.Ack) throw new SysmexProtocolException("HC ACK Code Error", $"На ENQ получен 0x{response:X2}.");

        int number = 1;
        foreach (string record in message.Split('\r', StringSplitOptions.RemoveEmptyEntries))
        {
            AstmFrame frame = new(number, record + "\r", true);
            byte[] bytes = codec.Encode(frame);
            bool accepted = false;
            for (int attempt = 1; attempt <= 6 && !accepted; attempt++)
            {
                await stream.WriteAsync(bytes, token).ConfigureAwait(false);
                await stream.FlushAsync(token).ConfigureAwait(false);
                response = await ReadByteAsync(stream, SenderTimeout, token).ConfigureAwait(false);
                accepted = response is AstmControl.Ack or AstmControl.Eot;
                if (!accepted && response != AstmControl.Nak)
                    throw new SysmexProtocolException("HC ACK Code Error", $"На кадр получен 0x{response:X2}.");
            }
            if (!accepted)
                throw new SysmexProtocolException("HC Transmission Count Error", "Кадр не принят за шесть попыток.");
            number = (number + 1) % 8;
        }
        await WriteControlAsync(stream, AstmControl.Eot, token).ConfigureAwait(false);
    }

    /// <summary>Асинхронно читает один байт с документированным timeout.</summary>
    private static async Task<byte> ReadByteAsync(NetworkStream stream, TimeSpan timeout, CancellationToken token)
    {
        using CancellationTokenSource timeoutSource = CancellationTokenSource.CreateLinkedTokenSource(token);
        timeoutSource.CancelAfter(timeout);
        byte[] value = new byte[1];
        try
        {
            int count = await stream.ReadAsync(value, timeoutSource.Token).ConfigureAwait(false);
            if (count == 0) throw new EndOfStreamException("IPU закрыл TCP-соединение.");
            return value[0];
        }
        catch (OperationCanceledException) when (!token.IsCancellationRequested)
        {
            throw new SysmexProtocolException("HC ACK Time-Out", $"Ответ не получен за {timeout.TotalSeconds:0} секунд.");
        }
    }

    /// <summary>Читает остаток кадра до LF с receiver timeout.</summary>
    private static async Task<byte[]> ReadFrameRemainderAsync(NetworkStream stream, CancellationToken token)
    {
        List<byte> bytes = [];
        while (bytes.Count < 64000)
        {
            byte value = await ReadByteAsync(stream, ReceiverTimeout, token).ConfigureAwait(false);
            bytes.Add(value);
            if (value == AstmControl.Lf) return bytes.ToArray();
        }
        throw new SysmexProtocolException("Frame Error", "Кадр превышает 64000 символов.");
    }

    /// <summary>Асинхронно отправляет управляющий байт.</summary>
    private static async Task WriteControlAsync(NetworkStream stream, byte value, CancellationToken token)
    {
        await stream.WriteAsync(new[] { value }, token).ConfigureAwait(false);
        await stream.FlushAsync(token).ConfigureAwait(false);
    }
}
