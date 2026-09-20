using System.Text;
using AnalyzerService.Lis;

namespace SysmexCS2000.HostOnline.Driver.Protocol;

/// <summary>
/// Кодирует и разбирает фиксированные поля собственного Sysmex Host Online.
/// STX/ETX обрабатываются транспортом; тело ограничено 253 символами.
/// </summary>
public sealed class HostOnlineCodec
{
    /// <summary>Длина общей части текста до первого 9-символьного блока.</summary>
    public const int HeaderLength = 58;
    /// <summary>Максимальное число параметров в одном тексте по спецификации.</summary>
    public const int MaximumParametersPerBlock = 22;

    /// <summary>Синхронно разбирает запрос R221.</summary><param name="body">Текст без STX/ETX.</param><returns>Запрос.</returns>
    public HostOnlineInquiry ParseInquiry(string body)
    {
        HostOnlineHeader header = ParseHeader(body);
        if (header.Kind != 'R' || header.Subtype != '2' || header.Version != "21")
            throw new HostOnlineProtocolException("Text Distinction Error", "Ожидался запрос R221.");
        return new HostOnlineInquiry(header, ParseParameterCodes(body));
    }

    /// <summary>Синхронно разбирает результат D121 или D221.</summary><param name="body">Текст без STX/ETX.</param><returns>Результат.</returns>
    public HostOnlineResult ParseResult(string body)
    {
        HostOnlineHeader header = ParseHeader(body);
        if (header.Kind != 'D' || header.Subtype is not ('1' or '2') || header.Version != "21")
            throw new HostOnlineProtocolException("Text Distinction Error", "Ожидался результат D121 или D221.");
        EnsureDataBlocks(body);
        List<HostOnlineResultItem> items = [];
        for (int offset = HeaderLength; offset < body.Length; offset += 9)
        {
            string block = body.Substring(offset, 9);
            items.Add(new HostOnlineResultItem(block[..3], block.Substring(3, 5).Trim(), block[8]));
        }
        return new HostOnlineResult(header, items);
    }

    /// <summary>Формирует один или несколько ответов S221, максимум по 22 параметра.</summary><param name="inquiry">Исходный запрос.</param><param name="order">Заказ или null.</param><param name="emptyCode">Код 000/999 при отсутствии заказа.</param><returns>Тела блоков без STX/ETX.</returns>
    public IReadOnlyList<string> BuildOrder(HostOnlineInquiry inquiry, LisOrder? order, string emptyCode)
    {
        IReadOnlyList<string> codes = order is null || order.Parameters.Count == 0
            ? [ValidateEmptyCode(emptyCode)]
            : order.Parameters;
        int total = (codes.Count + MaximumParametersPerBlock - 1) / MaximumParametersPerBlock;
        List<string> blocks = [];
        for (int index = 0; index < total; index++)
        {
            IEnumerable<string> part = codes.Skip(index * MaximumParametersPerBlock).Take(MaximumParametersPerBlock);
            string header = BuildHeader(inquiry.Header, order, index + 1, total);
            string parameters = string.Concat(part.Select(code => Fit(code, 3, false) + new string(' ', 6)));
            blocks.Add(header + parameters);
        }
        return blocks;
    }

    /// <summary>Синхронно разбирает фиксированный заголовок.</summary>
    private static HostOnlineHeader ParseHeader(string body)
    {
        if (body.Length < HeaderLength) throw new HostOnlineProtocolException("Text Length Error", $"Текст короче {HeaderLength} символов.");
        if (!int.TryParse(body.Substring(4, 2), out int block) || !int.TryParse(body.Substring(6, 2), out int total) || block < 1 || total < block)
            throw new HostOnlineProtocolException("Block Number Error", "Недопустимые номер или количество блоков.");
        return new HostOnlineHeader(body[0], body[1], body.Substring(2, 2), block, total, body[8],
            body.Substring(9, 6), body.Substring(15, 4), body.Substring(19, 6), body.Substring(25, 2),
            body.Substring(27, 15).Trim(), body[42], body.Substring(43, 15).TrimEnd());
    }

    /// <summary>Проверяет кратность хвоста 9-символьным блокам.</summary>
    private static void EnsureDataBlocks(string body)
    {
        if ((body.Length - HeaderLength) % 9 != 0)
            throw new HostOnlineProtocolException("Text Length Error", "Длина области данных не кратна 9.");
    }

    /// <summary>Извлекает трёхсимвольные коды параметров.</summary>
    private static IReadOnlyList<string> ParseParameterCodes(string body)
    {
        EnsureDataBlocks(body);
        List<string> result = [];
        for (int offset = HeaderLength; offset < body.Length; offset += 9) result.Add(body.Substring(offset, 3));
        return result;
    }

    /// <summary>Создаёт заголовок S221 с полями запроса и данными пациента.</summary>
    private static string BuildHeader(HostOnlineHeader source, LisOrder? order, int block, int total)
    {
        string date = DateTime.Now.ToString("yyMMdd");
        string time = DateTime.Now.ToString("HHmm");
        string patient = order is null ? string.Empty : $"{order.LastName} {order.FirstName}".Trim();
        return "S221" + block.ToString("00") + total.ToString("00") + "U" + date + time
            + Fit(source.RackNumber, 6, false) + Fit(source.TubePosition, 2, false)
            + Fit(source.SampleId, 15, true) + "C" + Fit(patient, 15, false);
    }

    /// <summary>Проверяет документированный специальный код.</summary>
    private static string ValidateEmptyCode(string code) => code is "000" or "999" ? code : throw new ArgumentOutOfRangeException(nameof(code));

    /// <summary>Обрезает и дополняет поле до точной длины.</summary>
    private static string Fit(string? value, int length, bool alignRight)
    {
        string text = value ?? string.Empty;
        if (text.Length > length) text = alignRight ? text[^length..] : text[..length];
        return alignRight ? text.PadLeft(length) : text.PadRight(length);
    }
}
