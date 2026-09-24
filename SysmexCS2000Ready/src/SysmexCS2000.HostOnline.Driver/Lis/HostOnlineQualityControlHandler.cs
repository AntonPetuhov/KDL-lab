using System.Text;
using AnalyzerService.Contracts;
using SysmexCS2000.HostOnline.Driver.Protocol;

namespace SysmexCS2000.HostOnline.Driver.Lis;

/// <summary>
/// Сохраняет результаты контроля качества Host Online отдельно от результатов пациентов.
/// Использует исходные коды прибора, поскольку контроль не является заказом пациента в БД ЛИС.
/// </summary>
public sealed class HostOnlineQualityControlHandler(AnalyzerSettings settings, IAnalyzerLogger logger)
{
    static HostOnlineQualityControlHandler() => Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);

    /// <summary>
    /// Синхронно записывает небольшие .res/.ok файлы в ResultsFolder/QualityControl.
    /// Маркер .ok создаётся последним, только после успешной записи результата.
    /// </summary>
    /// <param name="result">Разобранный D121/D221 с Sample Distinction Code C.</param>
    /// <exception cref="InvalidDataException">Тип пробы неверен либо данных нет.</exception>
    /// <exception cref="IOException">Не удалось записать файлы контроля.</exception>
    public void Handle(HostOnlineResult result)
    {
        if (result.Header.SampleType != 'C')
            throw new InvalidDataException("Для сохранения контроля требуется Sample Distinction Code C.");
        if (result.Items.Count == 0)
            throw new InvalidDataException("Сообщение контроля качества не содержит результатов.");

        string root = Path.IsPathRooted(settings.ResultsFolder)
            ? Path.GetFullPath(settings.ResultsFolder)
            : Path.GetFullPath(settings.ResultsFolder, Path.Combine(AppContext.BaseDirectory, settings.AnalyzerName));
        string directory = Path.Combine(root, "QualityControl");
        Directory.CreateDirectory(directory);
        string sampleId = result.Header.SampleId;
        string safeId = string.Concat(sampleId.Select(c => Path.GetInvalidFileNameChars().Contains(c) ? '_' : c));
        string stem = $"SYS2000_QC_{safeId}_{DateTime.Now:yyyyMMddHHmmssfff}_{Guid.NewGuid():N}";
        string timestamp = DateTime.Now.ToString("yyyyMMddHHmmss");
        StringBuilder content = new($"O|1|{sampleId}||ALL|R|{timestamp}|||||C||||ALL||||||||||F\r\n");
        int sequence = 0;
        foreach (HostOnlineResultItem item in result.Items)
            content.Append($"R|{++sequence}|^^^{item.ParameterCode}^^^^{settings.AnalyzerCode}|{item.Data}|||{item.Flag}|F||SYSMEX^QC||{timestamp}|{settings.AnalyzerCode}\r\n");

        string resultPath = Path.Combine(directory, stem + ".res");
        string temporary = resultPath + ".tmp";
        File.WriteAllText(temporary, content.ToString(), Encoding.GetEncoding(1251));
        File.Move(temporary, resultPath);
        string markerPath = Path.Combine(directory, stem + ".ok");
        File.WriteAllText(markerPath, "ok" + Environment.NewLine, Encoding.ASCII);
        logger.Result($"Контроль качества {sampleId}: сохранено {sequence} показателей в {directory}.");
    }
}
