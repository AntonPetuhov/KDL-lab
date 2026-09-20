namespace SysmexCS2000.Driver.Protocol;

/// <summary>Представляет проверенный кадр ASTM с номером, текстом и признаком последнего блока.</summary>
public sealed record AstmFrame(int Number, string Text, bool IsFinal);
