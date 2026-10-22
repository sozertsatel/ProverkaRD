using System.Text.Json;
using ProverkaRD.Engine;

namespace ProverkaRD;

public sealed class AppSettings
{
    private static readonly string SettingsDir = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "ProverkaRD");

    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    public List<string> IgnoredSigners { get; set; } = [];
    public bool GroupSignerWarnings { get; set; } = true;
    public bool GroupDuplicateFindings { get; set; } = true;
    public bool CheckUpdatesOnStart { get; set; } = true;

    /// <summary>Ставка НДС в процентах. 22 — ставка РФ с 2026 года. 0 — это 0%, а не «не задано».</summary>
    public decimal VatPercent { get; set; } = 22m;

    public CheckOptions ToOptions() => new()
    {
        IgnoredSigners = IgnoredSigners
            .Select(s => s.Trim())
            .Where(s => s.Length > 0)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList(),
        GroupSignerWarnings = GroupSignerWarnings,
        GroupDuplicateFindings = GroupDuplicateFindings,
        VatPercent = VatPercent,
    };

    public static AppSettings Load()
    {
        try
        {
            var path = Path.Combine(SettingsDir, "settings.json");
            if (File.Exists(path))
                return JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(path)) ?? new AppSettings();
        }
        catch
        {
            // Повреждённый файл не должен мешать проверке.
        }

        return new AppSettings();
    }

    public void Save()
    {
        try
        {
            Directory.CreateDirectory(SettingsDir);
            File.WriteAllText(Path.Combine(SettingsDir, "settings.json"), JsonSerializer.Serialize(this, JsonOptions));
        }
        catch
        {
            // Настройки некритичны.
        }
    }
}
