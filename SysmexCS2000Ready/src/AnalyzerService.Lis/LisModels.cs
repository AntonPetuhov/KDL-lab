namespace AnalyzerService.Lis;

/// <summary>
/// Содержит заказ ЛИС для ответа анализатору
/// </summary>
public sealed record LisOrder(string SampleId, string PatientId, string LastName, string FirstName, string BirthDate, string Sex, IReadOnlyList<string> Parameters);

/// <summary>
/// Содержит один результат, принятый от анализатора.
/// </summary>
public sealed record LisResult(string SampleId, string TestCode, string Value, string? Units, string? Flags);
