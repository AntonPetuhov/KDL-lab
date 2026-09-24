using System.Text;
using AnalyzerService.Contracts;
using AnalyzerService.LisDatabase;
using SysmexCS2000.Driver.Protocol;

namespace SysmexCS2000.Driver.Lis;

/// <summary>Извлекает R-записи и атомарно создаёт файлы результата и подтверждения для ЛИС.</summary>
public sealed class SysmexResultHandler(AnalyzerSettings settings, IAnalyzerLogger logger, LisDBProvider repository)
{
    static SysmexResultHandler() => Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);

    /// <summary>
    /// Синхронно обрабатывает принятое и уже подтверждённое структурно сообщение.
    /// Файлы малы, а последовательная запись гарантирует порядок .res/.ok.
    /// </summary>
    /// <param name="message">ASTM-сообщение результата.</param>
    /// <exception cref="InvalidDataException">Нет Sample ID или результатов.</exception>
    public void Handle(AstmMessage message)
    {
        AstmRecord order = message.Records.FirstOrDefault(r => r.Type == 'O')
            ?? throw new InvalidDataException("В сообщении результата отсутствует O-запись.");
        string sampleId = order.Fields.ElementAtOrDefault(2) ?? string.Empty;
        if (string.IsNullOrWhiteSpace(sampleId)) throw new InvalidDataException("В O-записи отсутствует Sample ID.");

        List<LisResult> results = [];
        foreach (AstmRecord record in message.Records.Where(r => r.Type == 'R'))
        {
            string universalId = record.Fields.ElementAtOrDefault(2) ?? string.Empty;
            string test = universalId.Split('^', StringSplitOptions.RemoveEmptyEntries).LastOrDefault() ?? string.Empty;
            string value = record.Fields.ElementAtOrDefault(3) ?? string.Empty;
            if (!string.IsNullOrWhiteSpace(test) && !string.IsNullOrWhiteSpace(value))
                results.Add(new LisResult(sampleId, test, value, record.Fields.ElementAtOrDefault(4), record.Fields.ElementAtOrDefault(6)));
        }
        if (results.Count == 0) throw new InvalidDataException("В сообщении отсутствуют пригодные R-записи.");

        string output = Path.GetFullPath(settings.OutputFolder!);
        Directory.CreateDirectory(output);
        string stem = $"SYS2000_{Sanitize(sampleId)}_{DateTime.Now:yyyyMMddHHmmssfff}";
        StringBuilder content = new($"O|1|{sampleId}||ALL|R|{DateTime.Now:yyyyMMddHHmmss}|||||X||||ALL||||||||||F\r\n");
        int sequence = 0;
        foreach (LisResult result in results)
        {
            string? psmCode = repository.TranslateResultCode(result.TestCode);
            if (string.IsNullOrWhiteSpace(psmCode))
            {
                logger.Result($"Код Sysmex {result.TestCode} не сопоставлен с PSMV2; результат пропущен.");
                continue;
            }
            content.AppendLine($"R|{++sequence}|^^^{psmCode}^^^^{settings.AnalyzerCode}|{result.Value}|{result.Units}||{result.Flags}|F||SYSMEX^||{DateTime.Now:yyyyMMddHHmmss}|{settings.AnalyzerCode}");
        }
        if (sequence == 0) throw new InvalidDataException("Ни один код результата не сопоставлен с PSMV2.");
        WriteAtomically(Path.Combine(output, stem + ".res"), content.ToString());
        WriteAtomically(Path.Combine(output, stem + ".ok"), "ok" + Environment.NewLine);
        logger.Result($"Созданы {stem}.res и {stem}.ok, результатов: {sequence}.");
    }

    /// <summary>Атомарно заменяет целевой файл после полной записи временного.</summary>
    private static void WriteAtomically(string path, string content)
    {
        string temporary = path + ".tmp";
        File.WriteAllText(temporary, content, Encoding.GetEncoding(1251));
        File.Move(temporary, path, true);
    }

    /// <summary>Удаляет недопустимые символы из части имени файла.</summary>
    private static string Sanitize(string value) => string.Concat(value.Select(c => Path.GetInvalidFileNameChars().Contains(c) ? '_' : c));
}
