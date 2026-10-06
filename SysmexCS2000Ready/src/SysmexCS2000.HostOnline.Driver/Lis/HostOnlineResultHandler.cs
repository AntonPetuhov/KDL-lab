using System.Globalization;
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
            // Low - повторное измерение того же теста. Сырое сообщение и
            // ParameterCode не меняем; заменяем код только при поиске теста ЛИС.
            string lisCode = MapLisResultCode(item.ParameterCode);
            string? psm = repository.TranslateResultCode(lisCode);
            if (string.IsNullOrWhiteSpace(psm))
            {
                logger.Result($"Код прибора {item.ParameterCode} (код для ЛИС {lisCode}) не сопоставлен с PSMV2.");
                continue;
            }
            if (!decimal.TryParse(item.Data, NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out _))
            {
                logger.Result($"Образец {sampleId}: код {item.ParameterCode} содержит нечисловой результат '{item.Data}', флаг '{item.Flag}'; строка ЛИС не создаётся.");
                continue;
            }
            string lisValue = FormatLisValue(item);
            logger.Result($"Образец {sampleId}: код прибора={item.ParameterCode}, код для ЛИС={lisCode}, значение прибора={item.Data}, значение ЛИС={lisValue}, флаг='{item.Flag}', код PSMV2={psm}.");
            //content.AppendLine($"R|{++sequence}|^^^{psm}^^^^{settings.AnalyzerCode}|{lisValue}|||{item.Flag}|F||SYSMEX^||{DateTime.Now:yyyyMMddHHmmss}|{settings.AnalyzerCode}");
            content.AppendLine($"R|{++sequence}|^^^{psm}^^^^{settings.AnalyzerCode}|{lisValue}|||N||F||SYS2000^||{DateTime.Now:yyyyMMddHHmmss}|{settings.AnalyzerCode}");
        }
        if (sequence == 0)
            throw new InvalidDataException("Ни один код результата не сопоставлен с PSMV2.");
        Write(Path.Combine(output, fullFName + ".res"), content.ToString());
        Write(Path.Combine(output, fullFName + ".ok"), "ok" + Environment.NewLine);
        logger.Result($"Host Online: создано результатов {sequence} для {sampleId}.");
    }

    /// <summary>
    /// Синхронно выбирает код сопоставления с ЛИС: повторные low 872/662
    /// относятся к тем же тестам, что med 882/652. Параметров I/O нет,
    /// поэтому асинхронность не нужна.
    /// </summary>
    /// <param name="instrumentCode">Исходный код результата прибора.</param>
    /// <returns>Код для поиска теста в ЛИС.</returns>
    public static string MapLisResultCode(string instrumentCode) => instrumentCode switch
    {
        "872" => "882",
        "662" => "652",
        _ => instrumentCode
    };

    /// <summary>
    /// Синхронно округляет уже расшифрованное число до одного десятичного знака
    /// и записывает запятую для файлов ЛИС. Неизвестный масштаб блокирует передачу.
    /// </summary>
    /// <param name="item">Результат, разобранный кодеком по настройке кода Host Online.</param>
    /// <returns>Число в виде «28,6».</returns>
    /// <exception cref="InvalidDataException">Нет масштаба кода или данные не являются числом.</exception>
    public static string FormatLisValue(HostOnlineResultItem item)
    {
        if (!HostOnlineCodec.HasDecimalPlaces(item.ParameterCode))
            throw new InvalidDataException($"Для кода {item.ParameterCode} не задан десятичный масштаб; числовой результат нельзя передать в ЛИС.");
        if (!decimal.TryParse(item.Data, NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out decimal value))
            throw new InvalidDataException($"Код {item.ParameterCode} не содержит числового результата: '{item.Data}'.");
        return Math.Round(value, 1, MidpointRounding.AwayFromZero).ToString("F1", CultureInfo.GetCultureInfo("ru-RU"));
    }

    /// <summary>Pаписывает файл через временное имя.</summary>
    private static void Write(string path, string content)
    {
        string temporary = path + ".tmp";
        File.WriteAllText(temporary, content, Encoding.GetEncoding(1251));
        File.Move(temporary, path, true);
    }

}
