using System.Text.Json;
using System.Text.RegularExpressions;

namespace UnityQcExporter;

/// <summary>Описывает источник готовых CSV. Id образует уникальный префикс в общей папке Unity.</summary>
internal sealed record Source(string Id, bool Unity, string ReadyFolder);

/// <summary>Загружает настройки службы и проверяет независимость входов и выхода.</summary>
internal sealed record ExportSettings(int PollSeconds, string UnityOutputFolder, Source[] Sources)
{
    /// <summary>Синхронно читает JSON при запуске и разрешает пути относительно папки конфигурации.</summary>
    public static ExportSettings Load(string path)
    {
        var settings = JsonSerializer.Deserialize<ExportSettings>(File.ReadAllText(path))
            ?? throw new InvalidDataException("Пустая конфигурация.");
        if (settings.PollSeconds is < 1 or > 86400 || settings.Sources is null)
            throw new InvalidDataException("Нужны Sources и PollSeconds в пределах 1..86400.");

        string Resolve(string value)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(value);
            return Path.TrimEndingDirectorySeparator(Path.GetFullPath(value, Path.GetDirectoryName(path)!));
        }

        var output = Resolve(settings.UnityOutputFolder);
        var ids = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var dirs = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { output };
        var sources = new List<Source>();
        foreach (var source in settings.Sources.Where(s => s.Unity))
        {
            if (string.IsNullOrEmpty(source.Id) || !Regex.IsMatch(source.Id, @"\A[A-Za-z0-9-]+\z") || !ids.Add(source.Id))
                throw new InvalidDataException("Для активных Sources нужны уникальные Id из ASCII-букв, цифр и дефиса.");
            var ready = Resolve(source.ReadyFolder);
            if (!dirs.Add(ready) || dirs.Any(d => d.StartsWith(ready + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)
                || ready.StartsWith(d + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)))
                throw new InvalidDataException("ReadyFolder и UnityOutputFolder должны быть отдельными непересекающимися папками.");
            sources.Add(source with { ReadyFolder = ready });
        }
        return settings with { UnityOutputFolder = output, Sources = sources.ToArray() };
    }
}
