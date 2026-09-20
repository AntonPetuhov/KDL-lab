using AnalyzerService.Lis;
using SysmexCS2000.HostOnline.Driver.Protocol;

/// <summary>Выполняет автономные проверки фиксированного формата Sysmex Host Online.</summary>
internal static class Program
{
    /// <summary>Запускает проверки и возвращает код процесса.</summary>
    private static int Main()
    {
        try
        {
            ParseInquiry();
            ParseResult();
            BuildOrder();
            Console.WriteLine("All Host Online protocol tests passed.");
            return 0;
        }
        catch (Exception ex) { Console.Error.WriteLine(ex); return 1; }
    }

    /// <summary>Проверяет позиции Sample ID и кода параметра R221.</summary>
    private static void ParseInquiry()
    {
        string body = Header('R', '2', "123456", new string(' ', 15)) + "010" + new string(' ', 6);
        HostOnlineInquiry inquiry = new HostOnlineCodec().ParseInquiry(body);
        Equal("123456", inquiry.Header.SampleId, "Sample ID");
        Equal("010", inquiry.ExistingParameters.Single(), "parameter");
    }

    /// <summary>Проверяет разбор D121: код, данные и флаг.</summary>
    private static void ParseResult()
    {
        string body = Header('D', '1', "123456", "IVANOV") + "010 1234+";
        HostOnlineResult result = new HostOnlineCodec().ParseResult(body);
        Equal("010", result.Items.Single().ParameterCode, "result code");
        Equal("1234", result.Items.Single().Data, "result value");
        Equal('+', result.Items.Single().Flag, "result flag");
    }

    /// <summary>Проверяет S221, фиксированную длину и специальный код 000.</summary>
    private static void BuildOrder()
    {
        HostOnlineCodec codec = new();
        HostOnlineInquiry inquiry = codec.ParseInquiry(Header('R', '2', "123456", new string(' ', 15)));
        var emptyOrder = new LisOrder("123456", "", "", "", "", "", []);
        string response = codec.BuildOrder(inquiry, emptyOrder, "000").Single();
        Equal('S', response[0], "response kind");
        Equal("000", response.Substring(58, 3), "empty code");
        Equal(67, response.Length, "response length");
    }

    /// <summary>Формирует 58-символьный тестовый заголовок.</summary>
    private static string Header(char kind, char subtype, string sample, string patient) =>
        $"{kind}{subtype}210101U2609201234RACK01" + "01" + sample.PadLeft(15) + "B" + patient.PadRight(15)[..15];

    /// <summary>Сравнивает ожидаемое и фактическое значение.</summary>
    private static void Equal<T>(T expected, T actual, string name)
    {
        if (!EqualityComparer<T>.Default.Equals(expected, actual)) throw new InvalidOperationException($"{name}: expected {expected}, actual {actual}");
    }
}
