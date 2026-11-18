using System.Security.Cryptography;

namespace UnityQcExporter;

/// <summary>Переносит готовые файлы без преобразования. Локальная стадия Publishing сохраняет границу публикации при сбое.</summary>
internal sealed class DeliveryQueue(Source source, string output, ILogger logger)
{
    /// <summary>Обрабатывает отложенные операции, затем завершённые CSV верхнего уровня Ready.</summary>
    public void Scan(CancellationToken token)
    {
        // Вход создаёт производитель. Ошибка настройки пути должна быть видна оператору.
        if (!Directory.Exists(source.ReadyFolder))
            throw new DirectoryNotFoundException(source.ReadyFolder);
        string state = Directory.CreateDirectory(Path.Combine(source.ReadyFolder, ".unity-export")).FullName;
        using var lease = File.Open(Path.Combine(state, "queue.lock"), FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
        string publishing = Directory.CreateDirectory(Path.Combine(state, "Publishing")).FullName;
        string sent = Directory.CreateDirectory(Path.Combine(state, "Sent")).FullName;
        Directory.CreateDirectory(output);
        foreach (var folder in new[] { publishing, source.ReadyFolder })
        {
            foreach (var path in Directory.EnumerateFiles(folder, "*.csv", SearchOption.TopDirectoryOnly))
            {
                token.ThrowIfCancellationRequested();
                try { Deliver(path, publishing, sent, folder == publishing); }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                {
                    logger.LogError(ex, "{Id}: ошибка доставки {File}. Исходный CSV сохранён.", source.Id, path);
                }
            }
        }
    }

    /// <summary>Ставит локальную отметку перед публикацией, переименовывает файл на стороне Unity и архивирует исходник.</summary>
    private void Deliver(string path, string publishing, string sent, bool recovering)
    {
        string name = Path.GetFileName(path);
        string final = Path.Combine(output, source.Id + "__" + name);
        string part = final + ".uploading";
        string staged = Path.Combine(publishing, name);
        string receipt = Path.Combine(sent, name);
        if (!recovering)
        {
            if (File.Exists(receipt) || File.Exists(staged) || File.Exists(final))
                throw new IOException("Имя пакета уже использовано. Проверьте источник и архив перед повторной отправкой.");

            // Временное имя зарезервировано за этой очередью. Неполную копию можно восстановить.
            File.Copy(path, part, overwrite: true);
            File.Move(path, staged);
        }
        if (File.Exists(part))
        {
            File.Move(part, final); // Публикация в одном каталоге без перезаписи.
        }
        else if (!File.Exists(final) || !SameContent(staged, final))
        {
            throw new IOException("Статус публикации неопределён. Требуется сверка с Unity, автоматическая повторная выдача запрещена.");
        }
        File.Move(staged, receipt);
        logger.LogInformation("{Id}: опубликован файл {File}", source.Id, final);
    }

    /// <summary>При восстановлении сравнивает опубликованный CSV и сохранённую локальную копию.</summary>
    private static bool SameContent(string first, string second)
    {
        using var a = File.OpenRead(first);
        using var b = File.OpenRead(second);
        return a.Length == b.Length && SHA256.HashData(a).AsSpan().SequenceEqual(SHA256.HashData(b));
    }
}
