using System.Text;
using AnalyzerService.Contracts;
using AnalyzerService.LisDatabase;
using SysmexCS2000.HostOnline.Driver.Protocol;

namespace SysmexCS2000.HostOnline.Driver.Lis;

/// <summary>
/// Преобразует D121/D221 в .res/.ok файлы для ЛИС.
/// </summary>
public sealed class HostOnlineResultHandler(AnalyzerSettings settings, IAnalyzerLogger logger, LisDBProvider repository)
{
    static HostOnlineResultHandler() => Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);

    /// <summary>
    /// Сохраняет результат
    /// </summary>
    public void Handle(HostOnlineResult result, string sourceId)
    {
        if (result.Items.Count == 0) 
            throw new InvalidDataException("D-текст не содержит результатов.");

        // Windows Service часто работает из System32: относительный путь из JSON
        // должен вычисляться от каталога опубликованной службы, а не от current directory.
        string output = Path.GetFullPath(settings.OutputFolder!, AppContext.BaseDirectory);
        Directory.CreateDirectory(output);
        string sampleId = result.Header.SampleId;
        // Имя сырого файла постоянно при повторной попытке после сбоя переноса в archive.
        string fullFName = sourceId;
        if (File.Exists(Path.Combine(output, fullFName + ".ok"))) return;
        StringBuilder content = new($"O|1|{sampleId}||ALL|R|{DateTime.Now:yyyyMMddHHmmss}|||||X||||ALL||||||||||F\r\n");
        int sequence = 0;
        foreach (HostOnlineResultItem item in result.Items)
        {
            string? psm = repository.TranslateResultCode(item.ParameterCode);
            if (string.IsNullOrWhiteSpace(psm))
            {
                logger.Result($"Код {item.ParameterCode} не сопоставлен с PSMV2.");
                continue;
            }
            content.AppendLine($"R|{++sequence}|^^^{psm}^^^^{settings.AnalyzerCode}|{item.Data}|||{item.Flag}|F||SYSMEX^||{DateTime.Now:yyyyMMddHHmmss}|{settings.AnalyzerCode}");
        }
        if (sequence == 0)
            throw new InvalidDataException("Ни один код результата не сопоставлен с PSMV2.");
        Write(Path.Combine(output, fullFName + ".res"), content.ToString());
        Write(Path.Combine(output, fullFName + ".ok"), "ok" + Environment.NewLine);
        logger.Result($"Host Online: создано результатов {sequence} для {sampleId}.");
    }

    /// <summary>Pаписывает файл через временное имя.</summary>
    private static void Write(string path, string content)
    {
        string temporary = path + ".tmp";
        File.WriteAllText(temporary, content, Encoding.GetEncoding(1251));
        File.Move(temporary, path, true);
    }

}
