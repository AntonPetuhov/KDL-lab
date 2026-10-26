using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using AnalyzerService.Contracts;
using AnalyzerService.LisDatabase;
using Maglumi.Snibe.Driver.Protocol;

namespace Maglumi.Snibe.Driver.Lis;

/// <summary>Адаптер X8 к существующему файловому интерфейсу ЛИС (.res/.ok, Windows-1251).</summary>
public sealed partial class X8ResultHandler
{
    private readonly AnalyzerSettings settings;
    private readonly IAnalyzerLogger logger;
    private readonly Func<string, string?> translate;

    static X8ResultHandler() => Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
    public X8ResultHandler(AnalyzerSettings settings, IAnalyzerLogger logger, LisDBProvider repository)
        : this(settings, logger, repository.TranslateResultCode) { }

    /// <summary>Явная зависимость для проверки файлового обмена без рабочей БД.</summary>
    public X8ResultHandler(AnalyzerSettings settings, IAnalyzerLogger logger, Func<string, string?> translate)
    {
        this.settings = settings;
        this.logger = logger;
        this.translate = translate;
    }

    public void Handle(IReadOnlyList<X8SampleResult> samples, string sourceId)
    {
        List<(string Name, string Content)> files = [];
        int sampleNumber = 0;
        string output = Path.GetFullPath(settings.OutputFolder!, AppContext.BaseDirectory);
        foreach (X8SampleResult sample in samples.Where(s => !s.QualityControl))
        {
            string name = sourceId + "_" + (++sampleNumber).ToString("0000", CultureInfo.InvariantCulture);
            if (File.Exists(Path.Combine(output, name + ".ok"))) continue;
            if (sample.Items.Count == 0) throw new InvalidDataException("Пациентское сообщение без OBX не является результатом.");
            string sampleId = LisField(sample.SampleId);
            string analyzer = LisField(settings.AnalyzerCode!);
            StringBuilder content = new($"O|1|{sampleId}||ALL|R|{DateTime.Now:yyyyMMddHHmmss}|||||X||||ALL||||||||||F\r\n");
            int sequence = 0;
            foreach (X8ResultItem item in sample.Items)
            {
                // Не выдаём предварительные, ошибочные и текстовые данные как финальные числа.
                if (item.Status != "F") throw new InvalidDataException($"X8 {item.Code}: OBX-11='{item.Status}', ожидался F.");
                if (!string.IsNullOrWhiteSpace(item.Alarms))
                    throw new InvalidDataException($"X8 {item.Code}: присутствуют тревоги NTE-3='{item.Alarms}'.");
                if (item.Flags is not ("" or "N" or "H" or "L" or "HH" or "LL"))
                    throw new InvalidDataException($"X8 {item.Code}: неподдерживаемый флаг '{item.Flags}'.");
                string value = FormatValue(item.Quantitative);
                string? mapped = translate(item.Code);
                if (string.IsNullOrWhiteSpace(mapped))
                    throw new InvalidDataException($"Код X8 '{item.Code}' не сопоставлен с PSMV2.");
                string completed = item.Completed;
                if (!DateTime.TryParseExact(completed, "yyyyMMddHHmmss", CultureInfo.InvariantCulture, DateTimeStyles.None, out _))
                    throw new InvalidDataException($"X8 {item.Code}: неверное время завершения OBX-19='{completed}'.");
                // Позиции файловых полей совпадают с Sysmex; приборные значения берём из X8.
                content.Append($"R|{++sequence}|^^^{LisField(mapped)}^^^^{analyzer}|{value}|||{item.Flags}||F||MAGLUMI^||{completed}|{analyzer}\r\n");
                logger.Result($"Образец {sampleId}: X8={item.Code}, PSMV2={mapped}, число={item.Quantitative}, " +
                    $"качественный={item.Qualitative}, единицы={item.Units}, флаг={item.Flags}, ЛИС={value}.");
            }
            files.Add((name, content.ToString()));
        }
        // Проверяем всё сообщение перед созданием новых .ok. Повторная обработка завершённых файлов безопасна.
        Directory.CreateDirectory(output);
        foreach (var file in files)
        {
            Write(Path.Combine(output, file.Name + ".res"), file.Content);
            Write(Path.Combine(output, file.Name + ".ok"), "ok\r\n");
        }
    }

    /// <summary>Сохраняет все переданные десятичные знаки, меняет только разделитель для ЛИС.</summary>
    public static string FormatValue(string value)
    {
        if (!NumericValue().IsMatch(value))
            throw new InvalidDataException($"Неподдерживаемое числовое значение X8 '{value}'. Качественные и пороговые значения требуют настройки ЛИС.");
        return value.Replace('.', ',');
    }

    [GeneratedRegex(@"\A[+-]?(?:[0-9]+(?:\.[0-9]+)?|\.[0-9]+)\z", RegexOptions.CultureInvariant)]
    private static partial Regex NumericValue();

    private static string LisField(string value)
    {
        if (value.Any(c => char.IsControl(c) || c is '|' or '^' or '\\' or '&' or '~'))
            throw new InvalidDataException("Недопустимый разделитель в файловом поле ЛИС.");
        return value;
    }

    private static void Write(string path, string content)
    {
        string temporary = path + ".tmp";
        try
        {
            File.WriteAllText(temporary, content, Encoding.GetEncoding(1251, EncoderFallback.ExceptionFallback, DecoderFallback.ExceptionFallback));
            File.Move(temporary, path, true);
        }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }
}
