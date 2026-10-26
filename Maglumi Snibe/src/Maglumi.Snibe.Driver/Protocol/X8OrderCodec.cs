using System.Globalization;
using AnalyzerService.LisDatabase;

namespace Maglumi.Snibe.Driver.Protocol;

public sealed record X8Query(string SampleId, string Sequence, string Rack, string Position, string Priority);

public static class X8OrderCodec
{
    public static X8Query ParseQuery(Hl7Message message)
    {
        if (message.Type != "TSREQ") throw new InvalidDataException("Ожидался TSREQ.");
        Hl7Segment qpd = message.Single("QPD");
        if (qpd.Field(1).Trim() != "TSREQ") throw new InvalidDataException("QPD-1 не TSREQ.");
        if (qpd.Field(3).Split('^').Length > 4)
            throw new InvalidDataException("В QPD-3 ожидаются четыре компонента идентификатора/позиции.");
        string Read(int c) => Hl7Text.Unescape(qpd.Component(3, c));
        string id = Read(1);
        // Общий LisDBProvider ищет по RID, поэтому позиция или внутренний номер не заменяют штрихкод.
        if (string.IsNullOrWhiteSpace(id) || id.Length > 22)
            throw new InvalidDataException("TSREQ: для поиска в ЛИС необходим штрихкод QPD-3.1 (до 22 символов).");
        foreach (int component in new[] { 2, 4 })
        {
            string number = Read(component);
            if (number.Length > 5 || number.Any(c => c is < '0' or > '9'))
                throw new InvalidDataException($"QPD-3.{component}: ожидается номер до 5 цифр.");
        }
        if (Read(3).Length > 22) throw new InvalidDataException("QPD-3.3: код штатива превышает 22 символа.");
        string priority = qpd.Field(4).Trim();
        if (priority is not ("0" or "1"))
            throw new InvalidDataException($"Приоритет '{priority}' не соответствует таблице QPD-4 (0/1). Уточните настройку X8.");
        return new(id, Read(2), Read(3), Read(4), priority);
    }

    public static string BuildResponse(X8Query query, LisOrder? order, DateTime now)
    {
        string timestamp = now.ToString("yyyyMMddHHmmss", CultureInfo.InvariantCulture);
        string controlId = Guid.NewGuid().ToString("N")[..20];
        string result = $"MSH|^~\\&|LIS||X8||{timestamp}||OML^O33|{controlId}|P|2.5|||NE|NE||UTF-8\r";
        string specimen = string.Join('^', new[] { query.SampleId, query.Sequence, "", query.Rack, query.Position }.Select(Hl7Text.Escape));
        bool hasOrder = order is not null && order.Parameters.Count != 0;
        if (hasOrder)
        {
            if (!string.Equals(order!.SampleId, query.SampleId, StringComparison.Ordinal))
                throw new InvalidDataException("Штрихкод заказа ЛИС отличается от запроса X8.");
            string patient = string.Join(' ', new[] { order.LastName, order.FirstName }.Where(s => !string.IsNullOrWhiteSpace(s)));
            if (patient.Length > 50 || order.PatientId.Length > 20)
                throw new InvalidDataException("Данные пациента превышают ограничения X8.");
            List<(int, string)> pid = [(1, "1"), (3, Hl7Text.Escape(order.PatientId)), (5, Hl7Text.Escape(patient)),
                (7, order.BirthDate), (8, order.Sex switch { "M" => "1", "F" => "2", _ => "3" })];
            if (DateTime.TryParseExact(order.BirthDate, "yyyyMMdd", CultureInfo.InvariantCulture, DateTimeStyles.None, out DateTime birth))
            {
                int years = now.Year - birth.Year - (now.Date < birth.AddYears(now.Year - birth.Year) ? 1 : 0);
                if (years > 0) pid.Add((38, years + "^1"));
                else if (birth.Date <= now.Date) pid.Add((38, Math.Max(1, (now.Date - birth.Date).Days) + "^3"));
            }
            result += Hl7Text.Segment("PID", pid.ToArray());
        }
        // Тип материала отсутствует в общем LisOrder: не придумываем serum/plasma.
        result += Hl7Text.Segment("SPM", (1, "1"), (2, specimen), (11, "P"));
        if (!hasOrder) return result;
        result += Hl7Text.Segment("ORC", (1, "NW"), (7, query.Priority), (9, timestamp));
        int sequence = 0;
        foreach (string code in order!.Parameters.Distinct(StringComparer.Ordinal))
        {
            // konvana хранит готовый код канала X8, а не код группы Sysmex.
            if (string.IsNullOrWhiteSpace(code) || code.Length > 10)
                throw new InvalidDataException($"Неверный код исследования X8: '{code}'.");
            result += Hl7Text.Segment("OBR", (1, (++sequence).ToString(CultureInfo.InvariantCulture)), (4, Hl7Text.Escape(code)));
        }
        return result;
    }
}
