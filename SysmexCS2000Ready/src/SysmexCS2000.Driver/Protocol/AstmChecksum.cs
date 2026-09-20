namespace SysmexCS2000.Driver.Protocol;

/// <summary>Вычисляет ASTM checksum как младшие восемь бит суммы байтов.</summary>
public static class AstmChecksum
{
    /// <summary>Синхронно вычисляет checksum для данных от F# до ETX/ETB включительно.</summary><param name="bytes">Байты диапазона.</param><returns>Контрольная сумма.</returns>
    public static byte Calculate(ReadOnlySpan<byte> bytes)
    {
        int sum = 0;
        foreach (byte value in bytes) sum = (sum + value) & 0xFF;
        return (byte)sum;
    }

    /// <summary>Преобразует checksum в два заглавных ASCII hex-символа.</summary><param name="value">Сумма.</param><returns>Два байта ASCII.</returns>
    public static byte[] ToAscii(byte value) => System.Text.Encoding.ASCII.GetBytes(value.ToString("X2"));
}
