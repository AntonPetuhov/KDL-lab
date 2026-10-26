namespace Maglumi.Snibe.Driver.Protocol;

public sealed record X8ResultItem(string Code, string Quantitative, string Qualitative, string Units,
    string Flags, string Status, string Completed, string Alarms);
public sealed record X8SampleResult(string SampleId, bool QualityControl, IReadOnlyList<X8ResultItem> Items);

public static class X8ResultCodec
{
    /// <summary>Поддерживает несколько SPM и MSH. Связь OBR/OBX проверяется, а не исправляется догадкой.</summary>
    public static IReadOnlyList<X8SampleResult> Parse(IReadOnlyList<Hl7Message> messages)
    {
        List<X8SampleResult> results = [];
        foreach (Hl7Message message in messages)
        {
            if (message.Type != "OUL^R22") throw new InvalidDataException("Файл результатов содержит сообщение другого типа.");
            List<X8ResultItem>? items = null;
            string? obrCode = null;
            string? obrSequence = null;
            bool waitingForObservation = false;
            foreach (Hl7Segment segment in message.Segments.Skip(1))
            {
                switch (segment.Name)
                {
                    case "SPM":
                        if (waitingForObservation) throw new InvalidDataException("OBR без соответствующего OBX перед следующим образцом.");
                        string role = segment.Field(11);
                        if (role is not ("P" or "Q")) throw new InvalidDataException("SPM-11 должен содержать P или Q.");
                        string id = Hl7Text.Unescape(segment.Component(2, role == "Q" ? 3 : 1));
                        if (string.IsNullOrWhiteSpace(id)) throw new InvalidDataException("Нет идентификатора образца/контроля в SPM-2.");
                        items = [];
                        results.Add(new(id, role == "Q", items));
                        obrCode = null;
                        obrSequence = null;
                        break;
                    case "OBR":
                        if (items is null) throw new InvalidDataException("OBR без SPM.");
                        if (waitingForObservation) throw new InvalidDataException("OBR без соответствующего OBX.");
                        obrCode = Hl7Text.Unescape(segment.Component(4, 1).Trim());
                        obrSequence = segment.Field(1);
                        if (string.IsNullOrWhiteSpace(obrCode)) throw new InvalidDataException("Пустой код OBR-4.1.");
                        waitingForObservation = true;
                        break;
                    case "OBX":
                        if (items is null || obrCode is null || !waitingForObservation) throw new InvalidDataException("OBX без SPM/OBR или повторный OBX.");
                        string code = Hl7Text.Unescape(segment.Field(3).Trim());
                        if (code != obrCode || segment.Field(1) != obrSequence)
                            throw new InvalidDataException($"OBR/OBX не согласованы: {obrCode}/{code}, номера {obrSequence}/{segment.Field(1)}.");
                        if (segment.Field(5).Split('^').Length > 2)
                            throw new InvalidDataException("В OBX-5 ожидаются только количественный и качественный компоненты.");
                        items.Add(new(code, Hl7Text.Unescape(segment.Component(5, 1).Trim()),
                            Hl7Text.Unescape(segment.Component(5, 2).Trim()), Hl7Text.Unescape(segment.Field(6)),
                            Hl7Text.Unescape(segment.Field(8)), segment.Field(11), segment.Field(19), ""));
                        waitingForObservation = false;
                        break;
                    case "NTE":
                        // NTE-3 содержит код, резерв и описание тревоги через ^.
                        if (items is null || items.Count == 0 || waitingForObservation || segment.Field(1) != obrSequence)
                            throw new InvalidDataException("NTE без соответствующего OBX.");
                        string alarms = string.Join('^', segment.Field(3).Split('^').Select(Hl7Text.Unescape)).Trim('^', ' ');
                        items[^1] = items[^1] with { Alarms = string.Join(';', new[] { items[^1].Alarms, alarms }.Where(s => s.Length != 0)) };
                        break;
                    case "PID": case "PV1": case "DG1": case "ORC": case "SID": case "ZAV":
                        break;
                    default:
                        throw new InvalidDataException($"Неизвестный сегмент результата X8: {segment.Name}.");
                }
            }
            if (waitingForObservation) throw new InvalidDataException("Сообщение завершилось без OBX для последнего OBR.");
        }
        if (results.Count == 0) throw new InvalidDataException("Результат не содержит SPM.");
        return results;
    }
}
