using AnalyzerService.Contracts;
using Microsoft.Data.SqlClient;

namespace AnalyzerService.LisDatabase;

/// <summary>
/// Выполняет запросы к БД ЛИС
/// </summary>
public class LisDBProvider(AnalyzerSettings settings, IAnalyzerLogger logger)
{
    /// <summary>
    /// Запрос задания. Получаем пациента и незавершённые тесты по RID.
    /// </summary>
    public LisOrder? GetOrder(string sampleId)
    {
        using SqlConnection connection = new(settings.ConnectionString);
        connection.Open();

        const string patientSql = """
            SELECT TOP 1 
            p.pop_pid, p.pop_enamn, p.pop_fnamn, p.pop_fdatum,
              CASE WHEN p.pop_kon = 'K' THEN 'F' ELSE 'M' END
            FROM dbo.remiss r WITH (NOLOCK)
            INNER JOIN dbo.pop p WITH (NOLOCK) ON p.pop_pid = r.pop_pid
            WHERE r.rem_deaktiv = 'O' AND r.rem_rid = @rid AND r.rem_ank_dttm IS NOT NULL
            """;

        using SqlCommand patientCommand = new(patientSql, connection) 
        { 
            CommandTimeout = 10 
        };

        patientCommand.Parameters.AddWithValue("@rid", sampleId);

        // локальные переменные для данных пациента
        string patientId, lastName, firstName, birthDate, sex;

        using (SqlDataReader reader = patientCommand.ExecuteReader())
        {
            if (!reader.Read()) 
                return null;

            patientId = reader.IsDBNull(0) ? string.Empty : reader.GetString(0);
            lastName = reader.IsDBNull(1) ? string.Empty : reader.GetString(1);
            firstName = reader.IsDBNull(2) ? string.Empty : reader.GetString(2);
            birthDate = reader.IsDBNull(3) ? string.Empty : reader.GetDateTime(3).ToString("yyyyMMdd");
            sex = reader.IsDBNull(4) ? string.Empty : reader.GetString(4);
        }

        // в качестве задания нужно получить тесты которые не отвалидированы
        // либо тесты с которых снята валидация (Reject) - b.bes_svarstat = 'U, 
        // либо новые тесты (b.bes_svarstat IS NULL AND b.bes_antalomg = 0), зарегистрированные и без результата
        const string testsSql = """
            SELECT b.ana_analyskod, b.bes_svarstat, b.bes_antalomg,
                   k.amt_analyskod,
                   CASE WHEN o.omg_resultat IS NULL THEN 0 ELSE 1 END AS has_result
            FROM dbo.remiss r WITH (NOLOCK)
            INNER JOIN dbo.bestall b WITH (NOLOCK) ON b.rem_id = r.rem_id
            LEFT JOIN dbo.omgang o WITH (NOLOCK) ON b.rem_id=o.rem_id AND b.pro_id=o.pro_id AND b.ana_analyskod=o.ana_analyskod
            LEFT JOIN dbo.konvana k WITH (NOLOCK) ON k.met_kod=b.ana_analyskod AND k.ins_maskin=@instrument
            WHERE r.rem_deaktiv='O' 
            AND r.rem_rid=@rid 
            """;

        using SqlCommand testsCommand = new(testsSql, connection) 
        { 
            CommandTimeout = 10 
        };

        testsCommand.Parameters.AddWithValue("@rid", sampleId);
        testsCommand.Parameters.AddWithValue("@instrument", settings.AnalyzerConfigurationCode ?? string.Empty);

        HashSet<string> parameters = new(StringComparer.Ordinal);

        using (SqlDataReader reader = testsCommand.ExecuteReader())
            while (reader.Read())
            {
                string lisTest = reader.IsDBNull(0) ? "<null>" : Convert.ToString(reader.GetValue(0)) ?? "<null>";
                string? status = reader.IsDBNull(1) ? null : Convert.ToString(reader.GetValue(1))?.Trim();
                int? attempts = reader.IsDBNull(2) ? null : Convert.ToInt32(reader.GetValue(2));
                string? mappedCode = reader.IsDBNull(3) ? null : Convert.ToString(reader.GetValue(3))?.Trim();
                bool hasResult = reader.GetInt32(4) != 0;
                bool pending = status == "U" || (status is null && attempts == 0);
                string reason = hasResult ? "уже есть результат" :
                    !pending ? $"статус={status ?? "NULL"}, число попыток={attempts?.ToString() ?? "NULL"}" :
                    string.IsNullOrWhiteSpace(mappedCode) ? "нет кода для прибора" : "включён";
                logger.Protocol($"Задание {sampleId}: исследование ЛИС {lisTest}, код прибора={mappedCode ?? "нет"}, " +
                                $"статус={status ?? "NULL"}, попыток={attempts?.ToString() ?? "NULL"}, " +
                                $"результат={(hasResult ? "есть" : "нет")}, решение={reason}.");
                if (!hasResult && pending && !string.IsNullOrWhiteSpace(mappedCode)) parameters.Add(mappedCode);
            }
        
        logger.Protocol($"Для {sampleId} найдено уникальных кодов задания из ЛИС: {parameters.Count} " +
                        $"[{string.Join(",", parameters)}].");

        return new LisOrder(sampleId, patientId, lastName, firstName, birthDate, sex, parameters.ToArray());
    }

    /// <summary>
    /// Преобразует код прибора в код PSMV2.
    /// </summary>
    public string? TranslateResultCode(string analyzerCode)
    {
        using SqlConnection connection = new(settings.ConnectionString);
        connection.Open();
        // Ищем только тесты, которые настроены для прибора и настроены для PSMV2
        const string sql = """
            SELECT TOP 1 target.amt_analyskod
            FROM dbo.konvana source WITH (NOLOCK)
            INNER JOIN dbo.konvana target WITH (NOLOCK) ON target.met_kod=source.met_kod AND target.ins_maskin='PSMV2'
            WHERE source.ins_maskin=@instrument AND source.amt_analyskod=@code
            """;
        using SqlCommand command = new(sql, connection) 
        { 
            CommandTimeout = 10 
        };
        command.Parameters.AddWithValue("@instrument", settings.AnalyzerConfigurationCode ?? string.Empty);
        command.Parameters.AddWithValue("@code", analyzerCode);

        return command.ExecuteScalar() as string;
    }
}
