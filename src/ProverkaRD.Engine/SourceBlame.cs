using System.Text.RegularExpressions;

namespace ProverkaRD.Engine;

/// <summary>
/// Подпись «где ошибка». Два источника совпадают, если после нормализации
/// (пунктуация, пробелы, невидимые символы, регистр) текст один.
/// Опечатка в 1–2 буквы помечает каждый источник, где она есть, а не верное написание.
/// Пропущенное «ПК» перед пикетом помечает источники без «ПК», даже если их большинство.
/// Критичность от этой подписи не меняется.
/// </summary>
public static partial class SourceBlame
{
    public const string InAct = "ошибка в акте";
    public const string InRd = "ошибка в РД";
    public const string InVerd = "ошибка в ВЭРД";

    public static string Of(string? act, string? rd, string? verd)
    {
        if (string.IsNullOrWhiteSpace(act) || string.IsNullOrWhiteSpace(rd) || string.IsNullOrWhiteSpace(verd))
            return "";

        var missingPicket = MissingPicket(act, rd, verd);
        if (missingPicket.Length > 0)
            return missingPicket;

        var actRd = Same(act, rd);
        var actVerd = Same(act, verd);
        var rdVerd = Same(rd, verd);
        if (actRd && actVerd && rdVerd)
            return "";
        if (rdVerd && !actRd && !actVerd)
            return Decide(InAct, act, rd, InRd, InVerd);
        if (actVerd && !actRd && !rdVerd)
            return Decide(InRd, rd, act, InAct, InVerd);
        if (actRd && !actVerd && !rdVerd)
            return Decide(InVerd, verd, act, InAct, InRd);
        return "";
    }

    private static bool Same(string left, string right) =>
        TextMatch.WithoutPunctuation(left) == TextMatch.WithoutPunctuation(right);

    /// <summary>
    /// Единственное отличие — «ПК» перед номером пикета. Без «ПК» источник неверный.
    /// </summary>
    private static string MissingPicket(string act, string rd, string verd)
    {
        var actKey = TextMatch.WithoutPunctuation(act);
        var rdKey = TextMatch.WithoutPunctuation(rd);
        var verdKey = TextMatch.WithoutPunctuation(verd);
        var actBare = BarePicket(actKey);
        var rdBare = BarePicket(rdKey);
        var verdBare = BarePicket(verdKey);
        if (actBare.Length == 0 || actBare != rdBare || rdBare != verdBare)
            return "";

        var actHas = actKey != actBare;
        var rdHas = rdKey != rdBare;
        var verdHas = verdKey != verdBare;
        if (actHas == rdHas && rdHas == verdHas)
            return "";

        var labels = new List<string>(3);
        if (!actHas)
            labels.Add(InAct);
        if (!rdHas)
            labels.Add(InRd);
        if (!verdHas)
            labels.Add(InVerd);
        return Join(labels.ToArray());
    }

    private static string BarePicket(string key)
    {
        var stripped = PicketMark().Replace(key, "");
        stripped = LetterDigitGap().Replace(stripped, "");
        return Gap().Replace(stripped, " ").Trim();
    }

    [GeneratedRegex(@"(?<![\p{L}\d])пк(?=\d{2,}(?:\+\d+)?)")]
    private static partial Regex PicketMark();

    [GeneratedRegex(@"(?<=[\p{L}])\s+(?=\d)")]
    private static partial Regex LetterDigitGap();

    [GeneratedRegex(@"\s+")]
    private static partial Regex Gap();

    /// <summary>
    /// pairText совпадает у двух источников. oddText — у третьего.
    /// Если пара содержит опечатку, помечаются оба её источника.
    /// </summary>
    private static string Decide(string oddLabel, string oddText, string pairText, string pairLabelA, string pairLabelB)
    {
        var gap = TextMatch.Spelling(pairText, oddText);
        if (gap.LeftMisspelled && !gap.RightMisspelled)
            return Join(pairLabelA, pairLabelB);
        if (gap.RightMisspelled && !gap.LeftMisspelled)
            return oddLabel;
        if (gap.LeftMisspelled && gap.RightMisspelled)
            return Join(oddLabel, pairLabelA, pairLabelB);
        return oddLabel;
    }

    private static string Join(params string[] labels)
    {
        var order = new[] { InAct, InRd, InVerd };
        return string.Join("; ", order.Where(label => labels.Contains(label, StringComparer.Ordinal)));
    }
}
