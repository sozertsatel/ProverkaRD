using System.Text.Json;

namespace ProverkaRD.Engine;

/// <summary>Проверка не начинается, пока не выбраны обе ведомости.</summary>
public static class LedgerGate
{
    public static IReadOnlyList<string> Missing(string? verdPath, string? vesrPath)
    {
        var missing = new List<string>();
        if (string.IsNullOrWhiteSpace(verdPath))
            missing.Add("Не загружена ВЭРД");
        if (string.IsNullOrWhiteSpace(vesrPath))
            missing.Add("Не загружена ВЭСР");
        return missing;
    }

    public static string Warning(string? verdPath, string? vesrPath)
    {
        var missing = Missing(verdPath, vesrPath);
        return missing.Count == 0 ? "" : string.Join(". ", missing) + ".";
    }
}

/// <summary>
/// Пути ведомостей, которые нужно вернуть после перезапуска автообновления.
/// Обычный запуск файл не читает: отметка снимается один раз.
/// </summary>
public static class LedgerSession
{
    public const string RestoreArgument = "--restore-session";

    private const string SessionFile = "ledgers.json";
    private const string MarkerFile = "restore-after-update";

    public sealed record Paths(string? VerdPath, string? VesrPath);

    public static void Write(string directory, string? verdPath, string? vesrPath)
    {
        Directory.CreateDirectory(directory);
        var json = JsonSerializer.Serialize(new Paths(verdPath, vesrPath), JsonOptions);
        File.WriteAllText(Path.Combine(directory, SessionFile), json);
    }

    public static void Save(string directory, string? verdPath, string? vesrPath)
    {
        Write(directory, verdPath, vesrPath);
        File.WriteAllText(Path.Combine(directory, MarkerFile), "1");
    }

    public static Paths? TakeAfterUpdate(string directory, bool restoreRequested)
    {
        var marker = Path.Combine(directory, MarkerFile);
        var marked = File.Exists(marker);
        if (!restoreRequested && !marked)
            return null;

        Paths? paths = null;
        try
        {
            var file = Path.Combine(directory, SessionFile);
            if (File.Exists(file))
                paths = JsonSerializer.Deserialize<Paths>(File.ReadAllText(file));
        }
        catch
        {
            paths = null;
        }
        finally
        {
            TryDelete(marker);
        }

        return paths ?? new Paths(null, null);
    }

    private static void TryDelete(string path)
    {
        try
        {
            if (File.Exists(path))
                File.Delete(path);
        }
        catch
        {
            // Отметка останется до следующего запуска.
        }
    }

    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };
}
