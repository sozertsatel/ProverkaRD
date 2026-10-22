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

    /// <summary>
    /// Как строка выглядит после снятия невидимых символов, неразрывных пробелов,
    /// похожей латиницы и концевых точек. Пробелы между словами сохраняются.
    /// </summary>
    public static string VisualKey(string? value)
    {
        var text = StripInvisible(value);
        var sb = new StringBuilder(text.Length);
        foreach (var ch in text)
        {
            if (char.IsWhiteSpace(ch))
            {
                sb.Append(' ');
                continue;
            }
            var category = char.GetUnicodeCategory(ch);
            if (category is UnicodeCategory.Format or UnicodeCategory.Control)
                continue;

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

        return Whitespace().Replace(sb.ToString(), " ").Trim().TrimEnd('.', ' ').Trim();
    }

    /// <summary>Текст для ячейки: переносы сняты, невидимые отличия сохранены.</summary>
    public static string ForReport(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return "";
        var sb = new StringBuilder(value.Length);
        foreach (var ch in value)
        {
            if (ch is '\r')
                continue;
            sb.Append(ch is '\n' or '\t' ? ' ' : ch);
        }
        return sb.ToString().Trim();
    }

    public static Severity Compare(string? left, string? right)
    {
        var a = left ?? "";
        var b = right ?? "";
        if (string.Equals(a, b, StringComparison.Ordinal))
            return Severity.Ok;
        if (VisualKey(a) == VisualKey(b))
            return Severity.Formatting;

        var (wordsA, digitsA) = Tokens(a);
        var (wordsB, digitsB) = Tokens(b);
        if (!digitsA.SequenceEqual(digitsB))
            return Severity.Critical;
        if (wordsA.Count != wordsB.Count)
            return Severity.Critical;

        var edits = 0;
        for (var i = 0; i < wordsA.Count; i++)
        {
            var wordA = wordsA[i];
            var wordB = wordsB[i];
            if (wordA == wordB)
                continue;
            var distance = EditDistance(wordA, wordB, 3);
            // Короткое слово вроде «ПК» не считается опечаткой: пропуск или замена — другое значение.
            if (distance > 2 || wordA.Length < 4 || wordB.Length < 4)
                return Severity.Critical;
            edits += distance;
            if (edits > 2)
                return Severity.Critical;
        }

        return Severity.Formatting;
    }

    /// <summary>
    /// Какая сторона пары — опечатка с пропущенными 1–2 буквами.
    /// Подстановка той же длины здесь не угадывается: обе стороны false.
    /// </summary>
    public static SpellingGap Spelling(string? left, string? right)
    {
        if (Compare(left, right) != Severity.Formatting)
            return new SpellingGap(false, false);
        if (VisualKey(left) == VisualKey(right))
            return new SpellingGap(false, false);

        var (wordsA, digitsA) = Tokens(left);
        var (wordsB, digitsB) = Tokens(right);
        if (!digitsA.SequenceEqual(digitsB) || wordsA.Count != wordsB.Count)
            return new SpellingGap(false, false);

        var leftBad = false;
        var rightBad = false;
        for (var i = 0; i < wordsA.Count; i++)
        {
            if (wordsA[i] == wordsB[i])
                continue;
            if (IsDeletionTypo(wordsA[i], wordsB[i]))
                leftBad = true;
            else if (IsDeletionTypo(wordsB[i], wordsA[i]))
                rightBad = true;
        }

        return new SpellingGap(leftBad, rightBad);
    }

    private static bool IsDeletionTypo(string candidate, string other)
    {
        if (candidate.Length < 4 || other.Length < 4)
            return false;
        var missing = other.Length - candidate.Length;
        if (missing is < 1 or > 2)
            return false;
        return EditDistance(candidate, other, 2) == missing;
    }

    private static (List<string> Words, List<string> Digits) Tokens(string? value)
    {
        var words = new List<string>();
        var digits = new List<string>();
        var word = new StringBuilder();
        var number = new StringBuilder();
        void FlushWord()
        {
            if (word.Length == 0)
                return;
            words.Add(word.ToString());
            word.Clear();
        }
        void FlushNumber()
        {
            if (number.Length == 0)
                return;
            digits.Add(number.ToString());
            number.Clear();
        }

        foreach (var ch in VisualKey(value))
        {
            if (char.IsLetter(ch))
            {
                FlushNumber();
                word.Append(ch);
            }
            else if (char.IsDigit(ch))
            {
                FlushWord();
                number.Append(ch);
            }
            else
            {
                FlushWord();
                FlushNumber();
            }
        }
        FlushWord();
        FlushNumber();
        return (words, digits);
    }

    private static int EditDistance(string left, string right, int limit)
    {
        if (left.Length < right.Length)
            (left, right) = (right, left);
        if (left.Length - right.Length > limit)
            return limit + 1;

        var previous = new int[right.Length + 1];
        var current = new int[right.Length + 1];
        for (var column = 0; column <= right.Length; column++)
            previous[column] = column;

        for (var row = 1; row <= left.Length; row++)
        {
            current[0] = row;
            var rowMin = current[0];
            for (var column = 1; column <= right.Length; column++)
            {
                var cost = left[row - 1] == right[column - 1] ? 0 : 1;
                current[column] = Math.Min(
                    Math.Min(current[column - 1] + 1, previous[column] + 1),
                    previous[column - 1] + cost);
                if (current[column] < rowMin)
                    rowMin = current[column];
            }
            if (rowMin > limit)
                return limit + 1;
            (previous, current) = (current, previous);
        }

        return previous[right.Length];
    }

    /// <summary>Текст без знаков пунктуации: отличие только ими — ошибка оформления.</summary>
    public static string WithoutPunctuation(string? value)
    {
        var text = PunctuationMarks().Replace(Normalize(value), "");
        return Whitespace().Replace(text, " ").Trim();
    }

    public static string CodeKey(string? code) => Normalize(code);

    public static string FormatMoney(decimal value) => value.ToString("#,0.00", Ru);

    public static decimal? ParseMoney(string? raw)
    {
        var parsed = ParseDecimal(raw);
        return parsed is null ? null : MoneyDecimal.Kopecks(parsed.Value);
    }

    public static decimal? ParseDecimal(string? raw)
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

    /// <summary>Равенство денег после округления до копейки, без допуска.</summary>
    public static bool SameKopecks(decimal left, decimal right) =>
        decimal.Round(left, 2, MidpointRounding.AwayFromZero) == decimal.Round(right, 2, MidpointRounding.AwayFromZero);

    public static string CollapseLines(string? value) =>
        Whitespace().Replace(StripInvisible(value).Replace('\u00A0', ' '), " ").Trim();

    [GeneratedRegex(@"[.,;:!?…·]+")]
    private static partial Regex PunctuationMarks();

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

public readonly record struct SpellingGap(bool LeftMisspelled, bool RightMisspelled);
