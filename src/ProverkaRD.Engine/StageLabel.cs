using System.Globalization;
using System.Text.RegularExpressions;

namespace ProverkaRD.Engine;

/// <summary>
/// Номер этапа для имени отчёта. «Этап 7» — этап. «Этап 7.29» — номер элемента, его не берём.
/// </summary>
public static partial class StageLabel
{
    public static string? FromText(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return null;
        foreach (Match match in StageNumber().Matches(text))
        {
            if (!int.TryParse(match.Groups["n"].Value, NumberStyles.None, CultureInfo.InvariantCulture, out var number))
                continue;
            if (number < 0)
                continue;
            return number.ToString(CultureInfo.InvariantCulture);
        }

        return null;
    }

    public static string Pick(string? verdPath, IEnumerable<string>? actStages, string? verdHeader)
    {
        var fromName = FromText(FileName(verdPath));
        if (fromName is not null)
            return fromName;
        if (actStages is not null)
        {
            foreach (var stage in actStages)
            {
                if (!string.IsNullOrWhiteSpace(stage))
                    return stage.Trim();
            }
        }

        return FromText(verdHeader) ?? "";
    }

    public static string ReportFileName(string? stage, DateTime when)
    {
        var date = when.ToString("dd.MM.yyyy", CultureInfo.InvariantCulture);
        var stem = string.IsNullOrWhiteSpace(stage)
            ? ProductBrand.Name + " — " + date
            : ProductBrand.Name + " — Этап " + stage.Trim() + " — " + date;
        stem = Sanitize(stem);
        if (stem.Length == 0)
            stem = ProductBrand.Name;
        return stem + ".xlsx";
    }

    public static string Sanitize(string name)
    {
        // Набор Windows, а не Path.GetInvalidFileNameChars: на Linux в нём нет двоеточия и «|».
        var buffer = new char[name.Length];
        var count = 0;
        foreach (var ch in name)
        {
            if (ch < ' ' || IsWindowsInvalid(ch))
                continue;
            buffer[count++] = ch;
        }

        return new string(buffer, 0, count).Trim().TrimEnd('.', ' ');
    }

    private static bool IsWindowsInvalid(char ch) => ch is '<' or '>' or ':' or '"' or '/' or '\\' or '|' or '?' or '*';

    private static string FileName(string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
            return "";
        try
        {
            return Path.GetFileName(path);
        }
        catch (Exception ex) when (ex is ArgumentException)
        {
            return path;
        }
    }

    [GeneratedRegex(@"(?<![\p{L}\d])этап(?:а|е|у|ом)?\s*(?:№\s*)?(?<n>\d+)(?!\d)(?!\s*[.,]\d)", RegexOptions.IgnoreCase)]
    private static partial Regex StageNumber();
}
