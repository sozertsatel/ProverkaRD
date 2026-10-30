using System.Globalization;
using System.Text.RegularExpressions;

namespace ProverkaRD.Engine;

/// <summary>
/// Фраза под таблицей акта: «Стоимость выполненных работ составляет N (прописью) рубля K копеек,
/// в том числе НДС (P%) – V (прописью) рублей K копеек».
/// </summary>
public sealed class ActMoneyClause
{
    public decimal Gross { get; init; }
    public decimal Vat { get; init; }
    public bool HasVat { get; init; }
    public decimal? VatPercent { get; init; }
    public bool GrossWordsPresent { get; init; }
    public bool GrossWordsMatch { get; init; }
    public string GrossWords { get; init; } = "";
    public bool VatWordsPresent { get; init; }
    public bool VatWordsMatch { get; init; }
    public string VatWords { get; init; } = "";
}

public static partial class ActMoneyText
{
    public static ActMoneyClause? Parse(string? prose)
    {
        if (string.IsNullOrWhiteSpace(prose))
            return null;

        var source = prose
            .Replace('\u00A0', ' ')
            .Replace('\u202F', ' ')
            .Replace('\u2009', ' ');
        var match = Sentence().Match(source);
        if (!match.Success)
            return null;

        if (!TryAmount(match.Groups["grossRub"].Value, match.Groups["grossKop"].Value, out var gross))
            return null;

        var grossWords = match.Groups["grossWords"].Value.Trim();
        var grossWordsPresent = grossWords.Length > 0;
        var hasVat = match.Groups["vatRub"].Success && match.Groups["vatRub"].Value.Trim().Length > 0;
        decimal vat = 0;
        if (hasVat && !TryAmount(match.Groups["vatRub"].Value, match.Groups["vatKop"].Value, out vat))
            hasVat = false;

        var vatWords = match.Groups["vatWords"].Value.Trim();
        var vatWordsPresent = hasVat && vatWords.Length > 0;
        decimal? percent = null;
        var rate = match.Groups["rate"].Success && match.Groups["rate"].Value.Length > 0
            ? match.Groups["rate"].Value
            : match.Groups["rate2"].Value;
        if (rate.Length > 0
            && decimal.TryParse(rate.Replace(',', '.'), NumberStyles.Number, CultureInfo.InvariantCulture, out var parsedRate))
            percent = parsedRate;

        return new ActMoneyClause
        {
            Gross = gross,
            Vat = vat,
            HasVat = hasVat,
            VatPercent = percent,
            GrossWordsPresent = grossWordsPresent,
            GrossWordsMatch = !grossWordsPresent || WordsMatch(grossWords, gross),
            GrossWords = grossWords,
            VatWordsPresent = vatWordsPresent,
            VatWordsMatch = !vatWordsPresent || WordsMatch(vatWords, vat),
            VatWords = vatWords,
        };
    }

    /// <summary>Фраза только цифрами, без прописи. Ею синтетические акты закрывают сверку.</summary>
    public static string DigitsSentence(decimal gross, decimal vat, decimal vatPercent = 22m)
    {
        return "Стоимость выполненных работ составляет "
            + Spell(gross)
            + ", в том числе НДС ("
            + vatPercent.ToString("0.##", CultureInfo.InvariantCulture)
            + "%) – "
            + Spell(vat)
            + ".";
    }

    public static bool WordsMatch(string? words, decimal amount)
    {
        if (!MoneyWords.TryParse(words, out var spoken))
            return false;
        var rubles = decimal.Truncate(MoneyDecimal.Kopecks(amount));
        return spoken == rubles;
    }

    private static bool TryAmount(string rublesText, string kopecksText, out decimal amount)
    {
        amount = 0;
        var rubles = Digits().Replace(rublesText, "");
        if (rubles.Length == 0 || rubles.Length > 15)
            return false;
        if (!decimal.TryParse(rubles, NumberStyles.None, CultureInfo.InvariantCulture, out var rub))
            return false;
        if (!int.TryParse(kopecksText, NumberStyles.None, CultureInfo.InvariantCulture, out var kop))
            return false;
        if (kop is < 0 or > 99)
            return false;
        amount = MoneyDecimal.Kopecks(rub + kop / 100m);
        return true;
    }

    private static string Spell(decimal value)
    {
        var rounded = MoneyDecimal.Kopecks(value);
        var rub = decimal.Truncate(rounded);
        var kop = (int)((rounded - rub) * 100m);
        return rub.ToString("0", CultureInfo.InvariantCulture)
            + " " + RubleWord(rub)
            + " " + kop.ToString("00", CultureInfo.InvariantCulture)
            + " " + KopeckWord(kop);
    }

    private static string RubleWord(decimal rubles)
    {
        var mod100 = (int)(Math.Abs(rubles) % 100);
        var mod10 = mod100 % 10;
        if (mod100 is >= 11 and <= 14)
            return "рублей";
        if (mod10 == 1)
            return "рубль";
        if (mod10 is >= 2 and <= 4)
            return "рубля";
        return "рублей";
    }

    private static string KopeckWord(int kop)
    {
        var mod100 = kop % 100;
        var mod10 = mod100 % 10;
        if (mod100 is >= 11 and <= 14)
            return "копеек";
        if (mod10 == 1)
            return "копейка";
        if (mod10 is >= 2 and <= 4)
            return "копейки";
        return "копеек";
    }

    [GeneratedRegex(
        @"стоимость\s+выполненных\s+работ\s+составляет\s+(?<grossRub>\d[\d \t]*)(?:\s*\((?<grossWords>[^)]*)\))?\s*руб(?:лей|ля|ль|\.)\s+(?<grossKop>\d{1,2})\s+копе(?:ек|йки|йка|\.)(?:\s*,?\s*в\s+том\s+числе\s+НДС\s*(?:\(\s*(?<rate>\d+(?:[.,]\d+)?)\s*%\s*\)|(?<rate2>\d+(?:[.,]\d+)?)\s*%)?\s*[–—-]?\s*(?<vatRub>\d[\d \t]*)(?:\s*\((?<vatWords>[^)]*)\))?\s*руб(?:лей|ля|ль|\.)\s+(?<vatKop>\d{1,2})\s+копе(?:ек|йки|йка|\.))?",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex Sentence();

    [GeneratedRegex(@"\D")]
    private static partial Regex Digits();
}
