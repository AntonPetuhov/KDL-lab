namespace AnalyzerService.LisDatabase;

/// <summary>
/// Содержит заказ ЛИС для ответа анализатору
/// </summary>
public record LisOrder(string SampleId, string PatientId, string LastName, string FirstName, string BirthDate, string Sex, IReadOnlyList<string> Parameters);

/// <summary>
/// Содержит один результат, принятый от анализатора.
/// </summary>
public record LisResult(string SampleId, string TestCode, string Value, string? Units, string? Flags);
