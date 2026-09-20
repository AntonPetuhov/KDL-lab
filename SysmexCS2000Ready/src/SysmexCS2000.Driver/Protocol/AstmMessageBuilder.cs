using AnalyzerService.Lis;

namespace SysmexCS2000.Driver.Protocol;

/// <summary>Формирует документированные H/P/O/L ответы на запрос заказа CS-2000i.</summary>
public sealed class AstmMessageBuilder
{
    /// <summary>Синхронно формирует ответ с заказом.</summary><param name="order">Данные ЛИС.</param><returns>ASTM-текст.</returns>
    public string BuildOrder(LisOrder order)
    {
        string now = DateTime.Now.ToString("yyyyMMddHHmmss");
        List<string> records = [$"H|\\^&|||LIS||||||||E1394-97|{now}", $"P|1|{order.PatientId}|||{order.LastName}^{order.FirstName}||{order.BirthDate}|{order.Sex}"];
        int sequence = 0;
        records.AddRange(order.Parameters.Select(p => $"O|{++sequence}|{order.SampleId}||^^^{p}|R|{now}|||||N||||SERUM"));
        records.Add("L|1|N");
        return string.Join('\r', records) + '\r';
    }

    /// <summary>Формирует ответ без параметров: 000 либо 999 по спецификации.</summary><param name="sampleId">Sample ID.</param><param name="code">Документированный код 000/999.</param><returns>ASTM-текст.</returns>
    public string BuildEmpty(string sampleId, string code)
    {
        if (code is not ("000" or "999")) throw new ArgumentOutOfRangeException(nameof(code));
        string now = DateTime.Now.ToString("yyyyMMddHHmmss");
        return $"H|\\^&|||LIS||||||||E1394-97|{now}\rP|1\rO|1|{sampleId}||^^^{code}|R|||||||N\rL|1|N\r";
    }
}
