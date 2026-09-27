namespace SysmexCS2000.Driver.Protocol;

/// <summary>
/// Содержит логические записи ASTM для немедленной маршрутизации запроса и точные
/// входящие байты ENQ/кадров/EOT для архивирования результата или контроля.
/// </summary>
/// <param name="Text">Объединённые записи подтверждённых кадров.</param>
/// <param name="Raw">Полученные из TCP байты транзакции.</param>
public sealed record AstmReceivedMessage(string Text, byte[] Raw);
