namespace SysmexCS2000.HostOnline.Driver.Protocol;

/// <summary>Представляет ошибку собственного протокола с именем из спецификации.</summary>
public class HostOnlineProtocolException : Exception
{
    /// <summary>Создаёт ошибку.</summary><param name="code">Имя ошибки.</param><param name="message">Контекст.</param>
    public HostOnlineProtocolException(string code, string message) : base(message) => Code = code;
    /// <summary>Получает имя ошибки протокола.</summary>
    public string Code { get; }
}
