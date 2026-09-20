using System.Text;

namespace SysmexCS2000.Driver.Protocol;

/// <summary>Кодирует и декодирует кадры ASTM E1381-02, включая границы и checksum.</summary>
public sealed class AstmFrameCodec
{
    /// <summary>Синхронно кодирует один кадр.</summary><param name="frame">Кадр.</param><returns>Байты для TCP.</returns>
    /// <exception cref="ArgumentOutOfRangeException">Номер вне 0..7.</exception>
    public byte[] Encode(AstmFrame frame)
    {
        if (frame.Number is < 0 or > 7) throw new ArgumentOutOfRangeException(nameof(frame));
        byte terminator = frame.IsFinal ? AstmControl.Etx : AstmControl.Etb;
        byte[] body = Encoding.ASCII.GetBytes($"{frame.Number}{frame.Text}").Append(terminator).ToArray();
        byte[] checksum = AstmChecksum.ToAscii(AstmChecksum.Calculate(body));
        return [AstmControl.Stx, .. body, .. checksum, AstmControl.Cr, AstmControl.Lf];
    }

    /// <summary>Синхронно декодирует полный кадр и проверяет checksum.</summary><param name="bytes">Полный кадр.</param><returns>Проверенный кадр.</returns>
    /// <exception cref="SysmexProtocolException">Структура или checksum ошибочны.</exception>
    public AstmFrame Decode(ReadOnlySpan<byte> bytes)
    {
        if (bytes.Length < 7 || bytes[0] != AstmControl.Stx || bytes[^2] != AstmControl.Cr || bytes[^1] != AstmControl.Lf)
            throw new SysmexProtocolException("Frame Error", "Некорректные границы ASTM-кадра.");
        int end = bytes.IndexOf(AstmControl.Etx);
        bool final = true;
        if (end < 0) { end = bytes.IndexOf(AstmControl.Etb); final = false; }
        if (end < 2 || end + 5 != bytes.Length)
            throw new SysmexProtocolException("Frame Error", "ETX/ETB находится в недопустимой позиции.");
        byte expected = AstmChecksum.Calculate(bytes[1..(end + 1)]);
        string checksumText = Encoding.ASCII.GetString(bytes.Slice(end + 1, 2));
        if (!byte.TryParse(checksumText, System.Globalization.NumberStyles.HexNumber, null, out byte actual) || actual != expected)
            throw new SysmexProtocolException("Checksum Error", $"Ожидалась сумма {expected:X2}, получена {checksumText}.");
        int number = bytes[1] - (byte)'0';
        if (number is < 0 or > 7) throw new SysmexProtocolException("Frame Number Error", "Номер кадра должен быть 0..7.");
        return new AstmFrame(number, Encoding.ASCII.GetString(bytes.Slice(2, end - 2)), final);
    }

    /// <summary>Делит одну ASTM-запись на кадры заданного размера.</summary><param name="record">Запись с CR.</param><param name="maximumTextLength">Максимум символов текста.</param><param name="firstNumber">Первый номер.</param><returns>Последовательность кадров.</returns>
    public IReadOnlyList<AstmFrame> Split(string record, int maximumTextLength, int firstNumber)
    {
        if (maximumTextLength < 1) throw new ArgumentOutOfRangeException(nameof(maximumTextLength));
        List<AstmFrame> frames = [];
        for (int offset = 0, number = firstNumber; offset < record.Length; number = (number + 1) % 8)
        {
            int count = Math.Min(maximumTextLength, record.Length - offset);
            frames.Add(new AstmFrame(number, record.Substring(offset, count), offset + count == record.Length));
            offset += count;
        }
        return frames;
    }
}
