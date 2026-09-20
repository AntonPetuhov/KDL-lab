using System.Text;
using SysmexCS2000.Driver.Protocol;

/// <summary>Выполняет пакет независимых от сети проверок ASTM без стороннего test framework.</summary>
internal static class Program
{
    /// <summary>Запускает проверки и возвращает ненулевой код при первой ошибке.</summary><returns>Код процесса.</returns>
    private static int Main()
    {
        try
        {
            ChecksumMatchesKnownFrame();
            FrameRoundTrip();
            ParserReadsQuery();
            Console.WriteLine("All protocol tests passed.");
            return 0;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine(ex);
            return 1;
        }
    }

    /// <summary>Проверяет сумму по известному содержимому ASTM-кадра.</summary>
    private static void ChecksumMatchesKnownFrame()
    {
        byte[] body = [.. Encoding.ASCII.GetBytes("1H|\\^&|||LIS\r"), AstmControl.Etx];
        byte expected = (byte)(body.Sum(x => x) & 0xFF);
        Equal(expected, AstmChecksum.Calculate(body), "checksum");
    }

    /// <summary>Проверяет симметрию кодирования и декодирования.</summary>
    private static void FrameRoundTrip()
    {
        AstmFrame source = new(1, "H|\\^&|||LIS\r", true);
        AstmFrame decoded = new AstmFrameCodec().Decode(new AstmFrameCodec().Encode(source));
        Equal(source, decoded, "frame round-trip");
    }

    /// <summary>Проверяет извлечение Sample ID из Q-записи.</summary>
    private static void ParserReadsQuery()
    {
        AstmMessageParser parser = new();
        AstmMessage message = parser.Parse("H|\\^&|||CS-2000i\rQ|1|^123456||ALL||||||||O\rL|1|N\r");
        Equal("123456", parser.GetQuerySampleId(message), "query sample id");
    }

    /// <summary>Сравнивает значения и выбрасывает диагностическую ошибку.</summary>
    private static void Equal<T>(T expected, T actual, string name)
    {
        if (!EqualityComparer<T>.Default.Equals(expected, actual))
            throw new InvalidOperationException($"{name}: expected {expected}, actual {actual}");
    }
}
