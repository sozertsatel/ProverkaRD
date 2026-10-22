namespace ProverkaRD.Engine;

/// <summary>Одни и те же цвета статуса в окне и в книге Excel. Шрифт — Segoe UI.</summary>
public static class SeverityPalette
{
    public const string Critical = "#F4C7C3";
    public const string Formatting = "#FFE08A";
    public const string Warning = "#F8CBAD";
    public const string Ok = "#C6EFCE";
    public const string Header = "#1F4E79";
    public const string Ink = "#1F2937";
    public const string FontName = "Segoe UI";

    public static string For(Severity severity) => severity switch
    {
        Severity.Critical => Critical,
        Severity.Formatting => Formatting,
        Severity.Warning => Warning,
        _ => Ok,
    };
}
