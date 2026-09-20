using System.Text;
using AnalyzerService.Contracts;
using AnalyzerService.Lis;
using SysmexCS2000.HostOnline.Driver.Protocol;

namespace SysmexCS2000.HostOnline.Driver.Lis;

/// <summary>Преобразует D121/D221 в атомарные .res/.ok файлы для ЛИС.</summary>
public sealed class HostOnlineResultHandler(AnalyzerSettings settings, IAnalyzerLogger logger, LisRepository repository)
{
    static HostOnlineResultHandler() => Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);

    /// <summary>Синхронно сохраняет результат; короткие файлы не требуют async.</summary><param name="result">Разобранный результат.</param>
    public void Handle(HostOnlineResult result)
    {
        if (result.Items.Count == 0) throw new InvalidDataException("D-текст не содержит результатов.");
        string output = Path.GetFullPath(settings.OutputFolder!);
        Directory.CreateDirectory(output);
        string sampleId = result.Header.SampleId;
        string stem = $"SYS2000_{Sanitize(sampleId)}_{DateTime.Now:yyyyMMddHHmmssfff}";
        StringBuilder content = new($"O|1|{sampleId}||ALL|R|{DateTime.Now:yyyyMMddHHmmss}|||||X||||ALL||||||||||F\r\n");
        int sequence = 0;
        foreach (HostOnlineResultItem item in result.Items)
        {
            string? psm = repository.TranslateResultCode(item.ParameterCode);
            if (string.IsNullOrWhiteSpace(psm))
            {
                logger.Result($"Код Host Online {item.ParameterCode} не сопоставлен с PSMV2.");
                continue;
            }
            content.AppendLine($"R|{++sequence}|^^^{psm}^^^^{settings.AnalyzerCode}|{item.Data}|||{item.Flag}|F||SYSMEX^||{DateTime.Now:yyyyMMddHHmmss}|{settings.AnalyzerCode}");
        }
        if (sequence == 0) throw new InvalidDataException("Ни один код результата не сопоставлен с PSMV2.");
        Write(Path.Combine(output, stem + ".res"), content.ToString());
        Write(Path.Combine(output, stem + ".ok"), "ok" + Environment.NewLine);
        logger.Result($"Host Online: создано результатов {sequence} для {sampleId}.");
    }

    /// <summary>Атомарно записывает файл через временное имя.</summary>
    private static void Write(string path, string content)
    {
        string temporary = path + ".tmp";
        File.WriteAllText(temporary, content, Encoding.GetEncoding(1251));
        File.Move(temporary, path, true);
    }

    /// <summary>Заменяет недопустимые символы имени файла.</summary>
    private static string Sanitize(string value) => string.Concat(value.Select(c => Path.GetInvalidFileNameChars().Contains(c) ? '_' : c));
}
