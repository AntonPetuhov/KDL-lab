namespace SysmexCS2000.Driver.Protocol;

/// <summary>Описывает нарушение ASTM с фазой и документированным именем ошибки.</summary>
public sealed class SysmexProtocolException : Exception
{
    /// <summary>Создаёт ошибку протокола.</summary><param name="code">Код или имя из спецификации.</param><param name="message">Описание.</param>
    public SysmexProtocolException(string code, string message) : base(message) => Code = code;
    /// <summary>Получает документированное имя или код ошибки.</summary>
    public string Code { get; }
}
