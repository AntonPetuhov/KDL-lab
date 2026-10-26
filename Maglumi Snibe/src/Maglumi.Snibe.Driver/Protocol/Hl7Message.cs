using System.Text;

namespace Maglumi.Snibe.Driver.Protocol;

/// <summary>Поля нумеруются по HL7, включая особую нумерацию MSH.</summary>
public sealed class Hl7Segment(string text)
{
    private readonly string[] fields = text.Split('|');
    public string Name => fields[0];
    public string Field(int number) => Name == "MSH" && number == 1 ? "|" :
        fields.ElementAtOrDefault(Name == "MSH" ? number - 1 : number) ?? "";
    public string Component(int field, int component) => Field(field).Split('^').ElementAtOrDefault(component - 1) ?? "";
}

public sealed record Hl7Message(IReadOnlyList<Hl7Segment> Segments)
{
    public Hl7Segment Header => Segments[0];
    public string Type => Header.Field(9).Trim();

    public Hl7Segment Single(string name) => Segments.SingleOrDefault(s => s.Name == name)
        ?? throw new InvalidDataException($"Нет обязательного сегмента {name}.");

    /// <summary>Руководство допускает несколько сообщений MSH в одном MLLP.</summary>
    public static IReadOnlyList<Hl7Message> Parse(string text)
    {
        if (!text.EndsWith('\r')) throw new InvalidDataException("HL7 должен завершаться CR сегмента.");
        List<Hl7Message> messages = [];
        List<Hl7Segment>? current = null;
        foreach (string line in text.Split('\r', StringSplitOptions.RemoveEmptyEntries))
        {
            Hl7Segment segment = new(line);
            if (segment.Name == "MSH")
            {
                if (segment.Field(2) != "^~\\&" || segment.Field(12) != "2.5" ||
                    !string.Equals(segment.Field(18), "UTF-8", StringComparison.OrdinalIgnoreCase) ||
                    string.IsNullOrWhiteSpace(segment.Field(10)))
                    throw new InvalidDataException("X8: требуются HL7 2.5, UTF-8, разделители |^~\\& и MSH-10.");
                current = [];
                messages.Add(new(current));
            }
            if (current is null || segment.Name.Length != 3 || line.Length < 4 || line[3] != '|')
                throw new InvalidDataException("Неверная структура HL7: ожидаются MSH и сегменты NAME|... .");
            current.Add(segment);
        }
        if (messages.Count == 0) throw new InvalidDataException("Пустое сообщение HL7.");
        return messages;
    }
}

public static class Hl7Text
{
    public static string Escape(string value)
    {
        if (value.Any(char.IsControl)) throw new InvalidDataException("Управляющий символ в поле HL7.");
        return value.Replace("\\", "\\E\\").Replace("|", "\\F\\").Replace("^", "\\S\\")
            .Replace("~", "\\R\\").Replace("&", "\\T\\");
    }

    public static string Unescape(string value)
    {
        StringBuilder result = new();
        for (int i = 0; i < value.Length; i++)
        {
            if (value[i] != '\\') { result.Append(value[i]); continue; }
            int end = value.IndexOf('\\', i + 1);
            if (end < 0) throw new InvalidDataException("Незакрытая escape-последовательность HL7.");
            result.Append(value[(i + 1)..end] switch
            {
                "F" => "|", "S" => "^", "R" => "~", "E" => "\\", "T" => "&",
                _ => throw new InvalidDataException("Неподдерживаемая escape-последовательность HL7.")
            });
            i = end;
        }
        return result.ToString();
    }

    public static string Segment(string name, params (int Field, string Value)[] values)
    {
        string[] fields = new string[values.Max(v => v.Field) + 1];
        fields[0] = name;
        foreach (var (field, value) in values) fields[field] = value;
        return string.Join('|', fields) + "\r";
    }
}
