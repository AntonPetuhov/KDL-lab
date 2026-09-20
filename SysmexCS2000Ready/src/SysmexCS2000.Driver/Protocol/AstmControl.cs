namespace SysmexCS2000.Driver.Protocol;

/// <summary>Содержит управляющие байты ASTM E1381-02, используемые CS-2000i.</summary>
public static class AstmControl
{
    /// <summary>Start of text.</summary>
    public const byte Stx = 0x02;
    /// <summary>End of text.</summary>
    public const byte Etx = 0x03;
    /// <summary>End of transmission.</summary>
    public const byte Eot = 0x04;
    /// <summary>Enquiry.</summary>
    public const byte Enq = 0x05;
    /// <summary>Acknowledge.</summary>
    public const byte Ack = 0x06;
    /// <summary>Line feed.</summary>
    public const byte Lf = 0x0A;
    /// <summary>Carriage return.</summary>
    public const byte Cr = 0x0D;
    /// <summary>Negative acknowledge.</summary>
    public const byte Nak = 0x15;
    /// <summary>End of transmission block.</summary>
    public const byte Etb = 0x17;
}
