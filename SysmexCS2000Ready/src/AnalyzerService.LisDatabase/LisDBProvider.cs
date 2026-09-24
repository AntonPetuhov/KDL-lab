using AnalyzerService.Contracts;
using Microsoft.Data.SqlClient;

namespace AnalyzerService.LisDatabase;

/// <summary>
/// Выполняет запросы к БД ЛИС
/// </summary>
public sealed class LisDBProvider(AnalyzerSettings settings, IAnalyzerLogger logger)
{
    /// <summary>
    /// Получаем пациента и незавершённые тесты по RID.
    /// </summary>
    public LisOrder? GetOrder(string sampleId)
    {
        using SqlConnection connection = new(settings.ConnectionString);
        connection.Open();
        const string patientSql = """
            SELECT TOP 1 p.pop_pid, p.pop_enamn, p.pop_fnamn, p.pop_fdatum,
              CASE WHEN p.pop_kon = 'K' THEN 'F' ELSE 'M' END
            FROM dbo.remiss r WITH (NOLOCK)
            INNER JOIN dbo.pop p WITH (NOLOCK) ON p.pop_pid = r.pop_pid
            WHERE r.rem_deaktiv = 'O' AND r.rem_rid = @rid AND r.rem_ank_dttm IS NOT NULL
            """;
        using SqlCommand patientCommand = new(patientSql, connection) { CommandTimeout = 10 };
        patientCommand.Parameters.AddWithValue("@rid", sampleId);
        string patientId, lastName, firstName, birthDate, sex;
        using (SqlDataReader reader = patientCommand.ExecuteReader())
        {
            if (!reader.Read()) return null;
            patientId = reader.IsDBNull(0) ? string.Empty : reader.GetString(0);
            lastName = reader.IsDBNull(1) ? string.Empty : reader.GetString(1);
            firstName = reader.IsDBNull(2) ? string.Empty : reader.GetString(2);
            birthDate = reader.IsDBNull(3) ? string.Empty : reader.GetDateTime(3).ToString("yyyyMMdd");
            sex = reader.IsDBNull(4) ? string.Empty : reader.GetString(4);
        }

        const string testsSql = """
            SELECT DISTINCT k.amt_analyskod
            FROM dbo.remiss r WITH (NOLOCK)
            INNER JOIN dbo.bestall b WITH (NOLOCK) ON b.rem_id = r.rem_id
            LEFT JOIN dbo.omgang o WITH (NOLOCK) ON b.rem_id=o.rem_id AND b.pro_id=o.pro_id AND b.ana_analyskod=o.ana_analyskod
            INNER JOIN dbo.konvana k WITH (NOLOCK) ON k.met_kod=b.ana_analyskod AND k.ins_maskin=@instrument
            WHERE r.rem_deaktiv='O' AND r.rem_rid=@rid AND o.omg_resultat IS NULL
              AND (b.bes_svarstat='U' OR (b.bes_svarstat IS NULL AND b.bes_antalomg=0))
            """;
        using SqlCommand testsCommand = new(testsSql, connection) { CommandTimeout = 10 };
        testsCommand.Parameters.AddWithValue("@rid", sampleId);
        testsCommand.Parameters.AddWithValue("@instrument", settings.AnalyzerConfigurationCode ?? string.Empty);
        List<string> parameters = [];
        using (SqlDataReader reader = testsCommand.ExecuteReader())
            while (reader.Read()) if (!reader.IsDBNull(0)) parameters.Add(reader.GetString(0));
        logger.Protocol($"Для {sampleId} найдено параметров: {parameters.Count}.");
        return new LisOrder(sampleId, patientId, lastName, firstName, birthDate, sex, parameters);
    }

    /// <summary>Синхронно переводит код прибора в код PSMV2.</summary><param name="analyzerCode">Код прибора.</param><returns>Код PSMV2 либо null.</returns>
    public string? TranslateResultCode(string analyzerCode)
    {
        using SqlConnection connection = new(settings.ConnectionString);
        connection.Open();
        const string sql = """
            SELECT TOP 1 target.amt_analyskod
            FROM dbo.konvana source WITH (NOLOCK)
            INNER JOIN dbo.konvana target WITH (NOLOCK) ON target.met_kod=source.met_kod AND target.ins_maskin='PSMV2'
            WHERE source.ins_maskin=@instrument AND source.amt_analyskod=@code
            """;
        using SqlCommand command = new(sql, connection) { CommandTimeout = 10 };
        command.Parameters.AddWithValue("@instrument", settings.AnalyzerConfigurationCode ?? string.Empty);
        command.Parameters.AddWithValue("@code", analyzerCode);
        return command.ExecuteScalar() as string;
    }
}
