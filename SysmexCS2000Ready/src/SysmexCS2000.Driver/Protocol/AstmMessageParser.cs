namespace SysmexCS2000.Driver.Protocol;

/// <summary>Разбирает H/P/Q/O/R/L записи и проверяет обязательный порядок сообщения.</summary>
public sealed class AstmMessageParser
{
    /// <summary>Синхронно разбирает сообщение в записи; операция выполняется только в памяти.</summary><param name="text">Объединённый текст кадров.</param><returns>Типизированное сообщение.</returns>
    /// <exception cref="SysmexProtocolException">Записи пусты или нарушают порядок.</exception>
    public AstmMessage Parse(string text)
    {
        string[] records = text.Split('\r', StringSplitOptions.RemoveEmptyEntries);
        if (records.Length < 2 || records[0].FirstOrDefault() != 'H' || records[^1].FirstOrDefault() != 'L')
            throw new SysmexProtocolException("Message Structure Error", "Сообщение должно начинаться H и завершаться L.");
        return new AstmMessage(records.Select(r => new AstmRecord(r[0], r.Split('|'))).ToArray());
    }

    /// <summary>Извлекает Sample ID из Q-записи, включая компонент после ^.</summary><param name="message">Сообщение запроса.</param><returns>Sample ID.</returns>
    public string GetQuerySampleId(AstmMessage message)
    {
        AstmRecord query = message.Records.SingleOrDefault(r => r.Type == 'Q')
            ?? throw new SysmexProtocolException("Query Error", "Q-запись отсутствует.");
        string field = query.Fields.ElementAtOrDefault(2) ?? string.Empty;
        string id = field.Split('^', StringSplitOptions.RemoveEmptyEntries).FirstOrDefault() ?? string.Empty;
        if (string.IsNullOrWhiteSpace(id)) throw new SysmexProtocolException("Query Error", "Sample ID отсутствует.");
        return id;
    }
}

/// <summary>Представляет полное ASTM-сообщение.</summary><param name="Records">Записи сообщения.</param>
public sealed record AstmMessage(IReadOnlyList<AstmRecord> Records);
/// <summary>Представляет одну ASTM-запись.</summary><param name="Type">Идентификатор.</param><param name="Fields">Поля.</param>
public sealed record AstmRecord(char Type, IReadOnlyList<string> Fields);
