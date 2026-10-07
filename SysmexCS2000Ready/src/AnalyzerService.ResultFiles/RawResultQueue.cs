using AnalyzerService.Contracts;

namespace AnalyzerService.ResultFiles;

/// <summary>
/// Сохраняет исходные сообщения прибора и передаёт пациентские результаты отдельному
/// потоку. Драйвер отвечает за разбор протокола, а очередь - только за жизненный цикл файлов.
/// </summary>
public class RawResultQueue : IDisposable
{
    private readonly string root;
    private readonly IAnalyzerLogger logger;
    private readonly Action<byte[], string> process;
    private readonly AutoResetEvent wake = new(false);
    private readonly object locker = new();
    private Thread? worker;
    private bool stopping;

    /// <summary>
    /// Синхронно создаёт очередь из существующего ResultsFolder JSON; сеть и ожидание здесь не нужны.
    /// </summary>
    /// <param name="settings">Настройки анализатора.</param>
    /// <param name="logger">Логгер операций с файлами.</param>
    /// <param name="process">Преобразование одного сырого сообщения в файлы ЛИС; исключение означает ошибку файла.</param>
    /// <exception cref="ArgumentException">Папка результатов не указана.</exception>
    public RawResultQueue(AnalyzerSettings settings, IAnalyzerLogger logger, Action<byte[], string> process)
    {
        if (string.IsNullOrWhiteSpace(settings.ResultsFolder))
            throw new ArgumentException("ResultsFolder обязателен.", nameof(settings));

        root = Path.IsPathRooted(settings.ResultsFolder) // является ли путь абсолютным, начинается ли путь с корня
            ? Path.GetFullPath(settings.ResultsFolder)
            : Path.GetFullPath(settings.ResultsFolder, Path.Combine(AppContext.BaseDirectory, settings.AnalyzerName));

        this.logger = logger;
        this.process = process;
    }

    /// <summary>Возвращает абсолютную папку накопления для диагностики.</summary>
    public string Root => root;

    /// <summary>
    /// Синхронно запускает выделенный поток; он обрабатывает также файлы от предыдущего запуска.
    /// </summary>
    public void Start()
    {
        lock (locker)
        {
            if (worker is not null) 
                throw new InvalidOperationException("Очередь обработки результатов уже запущена.");

            Directory.CreateDirectory(root);
            Directory.CreateDirectory(Path.Combine(root, "archive"));
            Directory.CreateDirectory(Path.Combine(root, "errors"));
            Directory.CreateDirectory(Path.Combine(root, "QualityControl"));
            stopping = false;

            worker = new Thread(Run) 
            { 
                IsBackground = true, Name = "Raw results: " + Path.GetFileName(root) 
            };

            worker.Start();

            logger.Service($"Поток обработки сырых данных с прибора запущен: {root}.");
        }
    }

    /// <summary>
    /// Синхронно и атомарно сохраняет точные байты сообщения пациента: короткая запись
    /// должна завершиться до перехода TCP-приёмника к следующему сообщению.
    /// </summary>
    /// <param name="sampleId">Идентификатор образца для имени файла.</param>
    /// <param name="raw">Исходные байты полного сообщения.</param>
    /// <returns>Путь созданного файла.</returns>
    /// <exception cref="IOException">Ошибка записи; вызывающий драйвер логирует её и сохраняет соединение по своей политике.</exception>
    public string SaveResult(string sampleId, byte[] raw)
    {
        string path = Save(root, "SYS2000", sampleId, raw);
        logger.Result($"Сырой результат {sampleId} сохранён: {path}, {raw.Length} байт.");
        wake.Set();
        return path;
    }

    /// <summary>
    /// Синхронно сохраняет байты контроля как есть в QualityControl; этот каталог
    /// не просматривается обработчиком ЛИС до появления отдельного QC-обработчика.
    /// </summary>
    /// <param name="sampleId">Идентификатор контроля.</param>
    /// <param name="raw">Исходные байты.</param>
    /// <returns>Путь QC-файла.</returns>
    /// <exception cref="IOException">Ошибка записи.</exception>
    public string SaveQualityControl(string sampleId, byte[] raw)
    {
        string path = Save(Path.Combine(root, "QualityControl"), "SYS2000_QC", sampleId, raw);
        logger.Result($"Сырой контроль {sampleId} сохранён без преобразования: {path}, {raw.Length} байт.");
        return path;
    }

    /// <summary>
    /// Синхронно останавливает поток после текущего файла; выделенный поток позволяет
    /// не удерживать TCP-цикл во время обращения к БД и вывода файлов ЛИС.
    /// </summary>
    public void Stop()
    {
        Thread? current;
        lock (gate) { stopping = true; current = worker; wake.Set(); }
        if (current is null) return;
        if (Thread.CurrentThread == current) throw new InvalidOperationException("Поток очереди не может остановить сам себя.");
        current.Join();
        lock (gate) worker = null;
        logger.Service($"Очередь сырых результатов остановлена: {root}.");
    }

    /// <summary>Синхронно освобождает поток и сигнал; исключения остановки не скрываются.</summary>
    public void Dispose() { Stop(); wake.Dispose(); }

    /// <summary>Синхронно создаёт уникальный файл через временное имя и атомарное переименование.</summary>
    private static string Save(string directory, string prefix, string sampleId, byte[] raw)
    {
        Directory.CreateDirectory(directory);
        string safeId = string.Concat(sampleId.Trim().Take(60).Select(c =>
            char.IsLetterOrDigit(c) || c is '-' or '_' ? c : '_'));
        if (safeId.Length == 0) safeId = "UNKNOWN";
        string name = $"{prefix}_{safeId}_{DateTime.UtcNow:yyyyMMddHHmmssfffffff}.raw";
        string path = Path.Combine(directory, name);
        string temporary = path + ".tmp";
        try
        {
            File.WriteAllBytes(temporary, raw);
            File.Move(temporary, path);
            return path;
        }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }

    /// <summary>
    /// Логика работы очереди
    /// </summary>
    private void Run()
    {
        while (!stopping)
        {
            try
            {
                foreach (string file in Directory.EnumerateFiles(root, "*.raw", SearchOption.TopDirectoryOnly).OrderBy(x => x))
                {
                    if (stopping) 
                        break;
                    ProcessFile(file);
                }
            }
            catch (Exception ex) 
            { 
                logger.Error($"Ошибка сканирования очереди {root}.", ex); 
            }
            wake.WaitOne(TimeSpan.FromSeconds(2));
        }
    }

    /// <summary>Преобразует один файл и перемещает его в archive или errors; ошибки переноса сохраняют исходник.</summary>
    private void ProcessFile(string file)
    {
        string destination = "archive";
        try
        {
            byte[] raw = File.ReadAllBytes(file);
            process(raw, Path.GetFileNameWithoutExtension(file));
            logger.Result($"Сырой файл обработан: {file}.");
        }
        catch (Exception ex)
        {
            destination = "errors";
            logger.Error($"Обработка сырого файла не удалась: {file}; файл будет помещён в results/errors.", ex);
        }
        try
        {
            string target = Path.Combine(root, destination, Path.GetFileName(file));
            File.Move(file, target);
            logger.Result($"Файл перемещён: {target}.");
        }
        catch (Exception ex) { logger.Error($"Не удалось переместить файл {file} в {destination}; он остаётся в очереди.", ex); }
    }
}
