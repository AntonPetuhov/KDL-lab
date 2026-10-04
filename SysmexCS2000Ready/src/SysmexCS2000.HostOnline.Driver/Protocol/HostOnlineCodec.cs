using System.Globalization;
using AnalyzerService.LisDatabase;

namespace SysmexCS2000.HostOnline.Driver.Protocol;

/// <summary>
/// Кодирует и разбирает фиксированные поля согласно протоколу Sysmex Host Online.
/// STX/ETX обрабатываются транспортом; тело ограничено 253 символами.
/// </summary>
public class HostOnlineCodec
{
    /// <summary>Длина общей части текста до первого 9-символьного блока.</summary>
    public const int HeaderLength = 58;

    /// <summary>Максимальное число параметров в одном тексте по спецификации.</summary>
    public const int MaximumParametersPerBlock = 22;

    /// <summary>
    /// Разбирает сообщение с запросом задания
    /// </summary>
    public HostOnlineInquiry ParseInquiry(string message)
    {
        HostOnlineHeader header = ParseHeader(message);
        if (header.Kind != 'R' || header.Subtype != '2' || header.Version != "21")
            throw new HostOnlineProtocolException("Text Distinction Error", "Ожидался запрос задания R221.");

        return new HostOnlineInquiry(header, ParseParameterCodes(message));
    }

    /// <summary>
    /// Синхронно разбирает результат D121 или D221.
    /// </summary>
    public HostOnlineResult ParseResult(string message)
    {
        HostOnlineHeader header = ParseHeader(message);
        if (header.Kind != 'D' || header.Subtype is not ('1' or '2') || header.Version != "21")
            throw new HostOnlineProtocolException("Text Distinction Error", "Ожидался результат D121 или D221.");

        // проверяем размер секций с тестами
        EnsureDataBlocks(message);
        List<HostOnlineResultItem> items = [];
        for (int offset = HeaderLength; offset < message.Length; offset += 9)
        {
            string block = message.Substring(offset, 9);

            string code = block[..3];
            string rawData = block.Substring(3, 5);
            char flag = block[8];

            string value = FormatResultValue(code, rawData);
            items.Add(new HostOnlineResultItem(code, value, flag));
        }
        return new HostOnlineResult(header, items);
    }

    /// <summary>
    /// Восстанавливает десятичный разделитель по таблице кодов Host Online.
    /// Для неизвестного кода сохраняет сырые цифры: передавать их в ЛИС без масштаба нельзя.
    /// </summary>
    private static string FormatResultValue(string code, string rawData)
    {
        // Убираем пробелы, которые протокол добавляет для выравнивания
        string trimmed = rawData.Trim();

        // Если данных нет или это не число (*****, /////, +++++, -----, XXXXX и т.п.) —
        // возвращаем как есть, без вставки запятой.
        if (trimmed.Length == 0 || !trimmed.All(char.IsDigit))
            return trimmed;

        if (!DecimalPlaces.TryGetValue(code, out int decimals))
            return trimmed;
        if (!decimal.TryParse(trimmed, NumberStyles.None, CultureInfo.InvariantCulture, out decimal raw))
            throw new InvalidDataException($"Неверные числовые данные для кода {code}: '{rawData}'.");
        decimal scale = 1;
        for (int i = 0; i < decimals; i++) scale *= 10;
        return (raw / scale).ToString($"F{decimals}", CultureInfo.InvariantCulture);
    }

    /// <summary>Проверяет, задан ли документированный масштаб для числового кода.</summary>
    public static bool HasDecimalPlaces(string code) => DecimalPlaces.ContainsKey(code);

    // Формат из таблицы кодов второго PDF, стр. 47/53; здесь коды сырья стенда.
    // Assay Group Settings реального прибора должны соответствовать этой таблице.

    private static readonly Dictionary<string, int> DecimalPlaces = new()
    {
        ["391"] = 3, ["392"] = 1,
        ["401"] = 3, ["402"] = 1,
        ["651"] = 4, ["652"] = 1,
        ["661"] = 4, ["662"] = 1,
        ["871"] = 4, ["872"] = 1,
        ["881"] = 4, ["882"] = 1,
    };

    /// <summary>
    /// Формирует один или несколько ответов (Задания для анализатора из ЛИС) S221, максимум по 22 параметра.
    /// Возвращает список строк - блоки ответа хоста
    /// </summary>
    public IReadOnlyList<string> BuildOrder(HostOnlineInquiry inquiry, LisOrder? order, string emptyCode)
    {
        // Если заказ отсутствует или у него нет параметров — берётся код заглушка
        // иначе - список параметров
        IReadOnlyList<string> codes = order is null || order.Parameters.Count == 0
            ? [ValidateEmptyCode(emptyCode)]
            : order.Parameters.Select(NormalizeOrderCode).Distinct(StringComparer.Ordinal).ToArray();

        // максимум в одном сообщении согласно документации 22 параметра, отсюда считаем, сколько будет блоков сообщений
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

    /// <summary>
    /// Синхронно преобразует трёхзначный код результата в код заказа: два знака
    /// группы анализа и ноль согласно разделам 5.4 и 6 Host Online PDF.
    /// </summary>
    /// <param name="source">Код из сопоставления ЛИС.</param>
    /// <returns>Трёхзначный код группы для S221.</returns>
    /// <exception cref="InvalidDataException">Код ЛИС не состоит из трёх ASCII-цифр.</exception>
    public static string NormalizeOrderCode(string source)
    {
        string code = source.Trim();
        if (code.Length != 3 || code.Any(c => c is < '0' or > '9'))
            throw new InvalidDataException($"Недопустимый код задания ЛИС: '{source}'.");
        return code is "000" or "999" ? code : code[..2] + "0";
    }

    /// <summary>Синхронно извлекает коды из сформированного S221 для диагностического журнала.</summary>
    /// <param name="body">Тело одного блока S221.</param>
    /// <returns>Коды всех параметров этого блока.</returns>
    public static IEnumerable<string> ReadOrderCodes(string body)
    {
        for (int offset = HeaderLength; offset + 9 <= body.Length; offset += 9)
            yield return body.Substring(offset, 3);
    }

    /// <summary>
    /// Разбирает фиксированный заголовок.
    /// </summary>
    private static HostOnlineHeader ParseHeader(string message)
    {
        if (message.Length < HeaderLength) 
            throw new HostOnlineProtocolException("Text Length Error", $"Текст короче {HeaderLength} символов.");

        if (!int.TryParse(message.Substring(4, 2), out int blockNum) || !int.TryParse(message.Substring(6, 2), out int total) || blockNum < 1 || total < blockNum)
            throw new HostOnlineProtocolException("Block Number Error", "Недопустимые номер или количество блоков.");

        // собираем объект заголовка сообщения на основании переданной строки сообщения
        return new HostOnlineHeader(message[0], message[1], message.Substring(2, 2), blockNum, total, message[8],
            message.Substring(9, 6), message.Substring(15, 4), message.Substring(19, 6), message.Substring(25, 2),
            message.Substring(27, 15).Trim(), message[42], message.Substring(43, 15).TrimEnd());
    }

    /// <summary>
    /// Проверяет кратность хвоста 9-символьным блокам. 
    /// Согласно документации параметры имеют по 9 символов
    /// </summary>
    private static void EnsureDataBlocks(string message)
    {
        if ((message.Length - HeaderLength) % 9 != 0)
            throw new HostOnlineProtocolException("Text Length Error", "Длина области данных не кратна 9.");
    }

    /// <summary>
    /// Извлекает трёхсимвольные коды параметров.
    /// </summary>
    private static IReadOnlyList<string> ParseParameterCodes(string message)
    {
        EnsureDataBlocks(message);
        List<string> parameters = [];
        for (int offset = HeaderLength; offset < message.Length; offset += 9) 
            parameters.Add(message.Substring(offset, 3));

        return parameters;
    }

    /// <summary>Создаёт заголовок S221 с полями запроса и данными пациента.</summary>
    private static string BuildHeader(HostOnlineHeader source, LisOrder? order, int block, int total)
    {
        string date = DateTime.Now.ToString("yyMMdd");
        string time = DateTime.Now.ToString("HHmm");
        string patient = order is null ? string.Empty : FormatPatientName(order.LastName, order.FirstName);
        char sampleType = source.SampleType is 'U' or 'E' or 'C' ? source.SampleType : 'U';
        char idInformation = source.IdInformation is 'M' or 'A' or 'B' or 'C'
            ? source.IdInformation
            : throw new InvalidDataException("Неизвестный способ регистрации Sample ID в R221.");
        return "S221" + block.ToString("00") + total.ToString("00") + sampleType + date + time
            + Fit(source.RackNumber, 6, false) + Fit(source.TubePosition, 2, false)
            + Fit(source.SampleId, 15, true) + idInformation + Fit(patient, 15, false);
    }

    /// <summary>
    /// Синхронно транслитерирует ФИО в ASCII и ограничивает его полем 15 символов.
    /// Документ допускает в S221 только имя пациента; даты рождения и пола в нём нет.
    /// </summary>
    /// <param name="lastName">Фамилия из БД.</param>
    /// <param name="firstName">Имя из БД.</param>
    /// <returns>ASCII-поле без завершающих пробелов.</returns>
    /// <exception cref="InvalidDataException">В имени встретился неподдерживаемый символ.</exception>
    public static string FormatPatientName(string? lastName, string? firstName)
    {
        string original = $"{lastName} {firstName}".Trim().ToUpperInvariant();
        Dictionary<char, string> cyrillic = new()
        {
            ['А']="A", ['Б']="B", ['В']="V", ['Г']="G", ['Д']="D", ['Е']="E", ['Ё']="YO",
            ['Ж']="ZH", ['З']="Z", ['И']="I", ['Й']="Y", ['К']="K", ['Л']="L", ['М']="M",
            ['Н']="N", ['О']="O", ['П']="P", ['Р']="R", ['С']="S", ['Т']="T", ['У']="U",
            ['Ф']="F", ['Х']="KH", ['Ц']="TS", ['Ч']="CH", ['Ш']="SH", ['Щ']="SHCH",
            ['Ъ']="", ['Ы']="Y", ['Ь']="", ['Э']="E", ['Ю']="YU", ['Я']="YA"
        };
        System.Text.StringBuilder ascii = new();
        foreach (char c in original)
        {
            // В S221 допустимы печатные коды символов, включая цифры и пунктуацию;
            // управляющие коды исключены (Host Online PDF, поле Patient Name).
            if (c is >= ' ' and <= '~') ascii.Append(c);
            else if (cyrillic.TryGetValue(c, out string? replacement)) ascii.Append(replacement);
            else throw new InvalidDataException($"Нельзя передать символ имени U+{(int)c:X4} в ASCII.");
        }
        return ascii.ToString()[..Math.Min(15, ascii.Length)].TrimEnd();
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
