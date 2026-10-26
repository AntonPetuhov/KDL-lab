using System.Text;

namespace Maglumi.Snibe.Driver.Protocol;

/// <summary>Обрамление X8: VT + UTF-8 HL7 + FS + CR. Один экземпляр на TCP-сеанс.</summary>
public sealed class MllpCodec(int maximumFrameBytes = 4 * 1024 * 1024)
{
    public static readonly UTF8Encoding Utf8 = new(false, true);
    private readonly List<byte> pending = [];
    private bool ending;
    public bool HasIncompleteFrame => pending.Count != 0;

    /// <summary>Возвращает точные байты кадров независимо от границ TCP-чтения.</summary>
    public IEnumerable<byte[]> Feed(byte[] bytes, int count)
    {
        for (int i = 0; i < count; i++)
        {
            byte value = bytes[i];
            if (pending.Count == 0)
            {
                if (value != 0x0B) throw new InvalidDataException("X8: ожидался VT в начале MLLP.");
                pending.Add(value);
                continue;
            }
            if (ending)
            {
                if (value != 0x0D) throw new InvalidDataException("X8: после FS ожидался CR.");
                pending.Add(value);
                byte[] frame = pending.ToArray();
                pending.Clear();
                ending = false;
                yield return frame;
                continue;
            }
            if (value == 0x0B) throw new InvalidDataException("X8: вложенный VT, незавершённое сообщение.");
            pending.Add(value);
            if (pending.Count > maximumFrameBytes - 1)
                throw new InvalidDataException("X8: превышен максимальный размер MLLP-кадра.");
            ending = value == 0x1C;
        }
    }

    public static byte[] Encode(string text) => [0x0B, .. Utf8.GetBytes(text), 0x1C, 0x0D];

    public static string Decode(byte[] frame)
    {
        if (frame.Length < 4 || frame[0] != 0x0B || frame[^2] != 0x1C || frame[^1] != 0x0D)
            throw new InvalidDataException("Неполный MLLP-кадр.");
        string text = Utf8.GetString(frame, 1, frame.Length - 3);
        if (text.Any(c => char.IsControl(c) && c != '\r'))
            throw new InvalidDataException("Недопустимый управляющий символ в HL7.");
        return text;
    }
}
