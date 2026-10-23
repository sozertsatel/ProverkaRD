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

    /// <summary>
    /// Сначала имя файла ВЭРД, затем явный «Этап N» в тексте акта,
    /// затем префикс номера элемента акта (6.65 → 6), затем вторая группа шифра (100-06-3000 → 6),
    /// затем шапка листа ВЭРД. Дата в имени файла этапом не становится.
    /// </summary>
    public static string Pick(
        string? verdPath,
        IEnumerable<string>? actStages,
        string? verdHeader,
        IEnumerable<string?>? elementNumbers = null,
        IEnumerable<string?>? ciphers = null)
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

        var fromElement = First(elementNumbers, FromElementNumber);
        if (fromElement is not null)
            return fromElement;
        var fromCipher = First(ciphers, FromCipher);
        if (fromCipher is not null)
            return fromCipher;
        return FromText(verdHeader) ?? "";
    }

    /// <summary>«6.65» и «6,126» дают 6. «Этап 6.65» сюда не передаётся: это не номер элемента.</summary>
    public static string? FromElementNumber(string? elementNo)
    {
        if (string.IsNullOrWhiteSpace(elementNo))
            return null;
        var match = ElementPrefix().Match(elementNo.Trim().Replace(',', '.'));
        return match.Success ? TrimStage(match.Groups["n"].Value) : null;
    }

    /// <summary>Вторая числовая группа шифра: «100-06-3000-А-КЖ1» → 6. «00» этапом не считается.</summary>
    public static string? FromCipher(string? cipher)
    {
        if (string.IsNullOrWhiteSpace(cipher))
            return null;
        var match = CipherStage().Match(cipher);
        return match.Success ? TrimStage(match.Groups["n"].Value) : null;
    }

    private static string? First(IEnumerable<string?>? values, Func<string?, string?> read)
    {
        if (values is null)
            return null;
        foreach (var value in values)
        {
            var stage = read(value);
            if (stage is not null)
                return stage;
        }

        return null;
    }

    private static string? TrimStage(string digits)
    {
        var trimmed = digits.TrimStart('0');
        return trimmed.Length == 0 ? null : trimmed;
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

    [GeneratedRegex(@"^(?<n>\d+)\.\d+")]
    private static partial Regex ElementPrefix();

    [GeneratedRegex(@"(?<!\d)\d{3}-(?<n>\d{2})-\d{3,}")]
    private static partial Regex CipherStage();
}
