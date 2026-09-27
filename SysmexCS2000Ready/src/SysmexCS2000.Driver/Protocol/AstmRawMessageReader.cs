using System.Text;

namespace SysmexCS2000.Driver.Protocol;

/// <summary>Восстанавливает логические записи из сохранённых байтов ASTM-транзакции для фонового обработчика.</summary>
public static class AstmRawMessageReader
{
    /// <summary>
    /// Синхронно проверяет ENQ, номера и checksum сохранённых кадров, пропуская
    /// повторные или отклонённые при приёме кадры; I/O в методе нет.
    /// </summary>
    /// <param name="raw">Исходные байты входящей транзакции.</param>
    /// <returns>Текст принятых записей.</returns>
    /// <exception cref="SysmexProtocolException">Транзакция не содержит корректных кадров или EOT.</exception>
    public static string Read(byte[] raw)
    {
        if (raw.Length < 3 || raw[0] != AstmControl.Enq || raw[^1] != AstmControl.Eot)
            throw new SysmexProtocolException("Frame Error", "В сыром файле нет ENQ или EOT.");
        AstmFrameCodec codec = new();
        StringBuilder text = new();
        int expected = 1;
        for (int offset = 1; offset < raw.Length - 1;)
        {
            if (raw[offset++] != AstmControl.Stx)
                continue; // сетевой приёмник ответил NAK и продолжил ожидание STX
            int end = Array.IndexOf(raw, AstmControl.Lf, offset);
            if (end < 0 || end >= raw.Length - 1)
                throw new SysmexProtocolException("Frame Error", "Неполный ASTM-кадр в сыром файле.");
            byte[] frameBytes = raw[(offset - 1)..(end + 1)];
            offset = end + 1;
            AstmFrame frame;
            try { frame = codec.Decode(frameBytes); }
            catch (SysmexProtocolException) { continue; } // кадр был отклонён NAK при сетевом приёме
            if (frame.Number == (expected + 7) % 8) continue; // повтор после потерянного ACK
            if (frame.Number != expected)
                throw new SysmexProtocolException("Frame Number Error", "Неверный номер принятого кадра в сыром файле.");
            text.Append(frame.Text);
            expected = (expected + 1) % 8;
        }
        if (text.Length == 0) throw new SysmexProtocolException("Message Structure Error", "Нет принятых ASTM-записей.");
        return text.ToString();
    }
}
