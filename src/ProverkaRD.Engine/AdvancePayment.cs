using System.Globalization;
using System.Text.RegularExpressions;

namespace ProverkaRD.Engine;

/// <summary>
/// Сумма из строки «Расчет погашенной части аванса … рублей … копейки без учета НДС».
/// Ни с чем не сверяется.
/// </summary>
public static partial class AdvancePayment
{
    public static decimal? Parse(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return null;

        var source = text.Replace('\u00A0', ' ').Replace('\u202F', ' ').Replace('\u2009', ' ');
        var match = Sentence().Match(source);
        if (!match.Success)
            return null;

        var rubles = DigitsOnly().Replace(match.Groups["rub"].Value, "");
        if (rubles.Length == 0 || rubles.Length > 15)
            return null;
        if (!decimal.TryParse(rubles, NumberStyles.None, CultureInfo.InvariantCulture, out var rub))
            return null;
        if (!int.TryParse(match.Groups["kop"].Value, NumberStyles.None, CultureInfo.InvariantCulture, out var kop))
            return null;
        if (kop is < 0 or > 99)
            return null;
        return MoneyDecimal.Kopecks(rub + kop / 100m);
    }

    [GeneratedRegex(
        @"аванса\s+в\s+размере\s+(?<rub>\d(?:[\d\s]*\d)?)\s*(?:\([^)]*\))?\s*рубл(?:ей|я|ь)\s+(?<kop>\d{1,2})\s+копе(?:ек|йки|йка)",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex Sentence();

    [GeneratedRegex(@"\D")]
    private static partial Regex DigitsOnly();
}
