using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace ProverkaRD.Engine;

/// <summary>
/// Сравнение строк: точное совпадение после снятия невидимых символов — «Совпадает»,
/// совпадение после нормализации оформления — «Ошибка оформления», иначе «Критично».
/// </summary>
public static partial class TextMatch
{
    private static readonly CultureInfo Ru = CultureInfo.GetCultureInfo("ru-RU");

    public static string StripInvisible(string? value)
    {
        if (string.IsNullOrEmpty(value))
            return "";

        var sb = new StringBuilder(value.Length);
        foreach (var ch in value)
        {
            if (ch is '\u202A' or '\u202B' or '\u202C' or '\u202D' or '\u202E'
                or '\u200B' or '\u200C' or '\u200D' or '\u200E' or '\u200F'
                or '\uFEFF' or '\u2060' or '\u00AD')
                continue;
            sb.Append(ch);
        }
        return sb.ToString();
    }

    /// <summary>Ключ «как написано»: невидимые символы сняты, края обрезаны, оформление внутри сохранено.</summary>
    public static string StrictKey(string? value) => StripInvisible(value).Trim();

    public static string Normalize(string? value)
    {
        var text = StripInvisible(value).Replace('\u00A0', ' ');
        var sb = new StringBuilder(text.Length);
        foreach (var ch in text)
        {
            var lower = char.ToLowerInvariant(ch);
            if (lower == 'ё')
                lower = 'е';
            lower = lower switch
            {
                'a' => 'а',
                'b' => 'в',
                'c' => 'с',
                'e' => 'е',
                'h' => 'н',
                'k' => 'к',
                'm' => 'м',
                'o' => 'о',
                'p' => 'р',
                't' => 'т',
                'x' => 'х',
                'y' => 'у',
                '«' or '»' or '“' or '”' or '„' or '‟' => '"',
                '‘' or '’' => '\'',
                '\u2013' or '\u2014' or '\u2212' => '-',
                _ => lower,
            };
            sb.Append(lower);
        }

        var folded = Whitespace().Replace(sb.ToString(), " ").Trim().TrimEnd('.', ' ');
        folded = PunctuationSpaces().Replace(folded, "$1");
        folded = LetterDigit().Replace(folded, "");
        folded = DigitLetter().Replace(folded, "");
        folded = HyphenSpaces().Replace(folded, "-");
        folded = Whitespace().Replace(folded, " ").Trim();
        return folded;
    }

    public static Severity Compare(string? left, string? right)
    {
        var a = StrictKey(left);
        var b = StrictKey(right);
        if (a.Length == 0 && b.Length == 0)
            return Severity.Ok;
        if (a == b)
            return Severity.Ok;
        if (Normalize(left) == Normalize(right))
            return Severity.Formatting;
        return Severity.Critical;
    }

    public static string CodeKey(string? code) => Normalize(code);

    public static string FormatMoney(decimal value) => value.ToString("#,0.00", Ru);

    public static decimal? ParseMoney(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
            return null;

        var text = StripInvisible(raw).Replace('\u00A0', ' ').Trim();
        if (text is "-" or "—" or "–")
            return null;
        // Код ДГП, а не сумма.
        if (DgpOnly().IsMatch(text))
            return null;

        text = text.Replace(" ", "").Replace("'", "");
        text = text.Replace(',', '.');
        text = NotMoneyChars().Replace(text, "");
        if (!MoneyShape().IsMatch(text))
            return null;
        return decimal.TryParse(text, NumberStyles.Number, CultureInfo.InvariantCulture, out var value)
            ? value
            : null;
    }

    public static bool MoneyEquals(decimal left, decimal right) => Math.Abs(left - right) <= 0.01m;

    public static string CollapseLines(string? value) =>
        Whitespace().Replace(StripInvisible(value).Replace('\u00A0', ' '), " ").Trim();

    [GeneratedRegex(@"\s+")]
    private static partial Regex Whitespace();

    [GeneratedRegex(@"\s*([.,;:()№])\s*")]
    private static partial Regex PunctuationSpaces();

    [GeneratedRegex(@"(?<=[\p{L}])\s+(?=\d)")]
    private static partial Regex LetterDigit();

    [GeneratedRegex(@"(?<=\d)\s+(?=[\p{L}])")]
    private static partial Regex DigitLetter();

    [GeneratedRegex(@"\s*-\s*")]
    private static partial Regex HyphenSpaces();

    [GeneratedRegex(@"^\d+(?:\.\d+)+\.?$")]
    private static partial Regex DgpOnly();

    [GeneratedRegex(@"[^0-9.\-]")]
    private static partial Regex NotMoneyChars();

    [GeneratedRegex(@"^-?\d+(?:\.\d+)?$")]
    private static partial Regex MoneyShape();
}
