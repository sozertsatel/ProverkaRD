namespace ProverkaRD.Engine;

/// <summary>
/// Подпись «где ошибка». Два источника совпадают, если после нормализации
/// (пунктуация, пробелы, невидимые символы, регистр) текст один.
/// Опечатка в 1–2 буквы помечает каждый источник, где она есть, а не верное написание.
/// Критичность от этой подписи не меняется.
/// </summary>
public static class SourceBlame
{
    public const string InAct = "ошибка в акте";
    public const string InRd = "ошибка в РД";
    public const string InVerd = "ошибка в ВЭРД";

    public static string Of(string? act, string? rd, string? verd)
    {
        if (string.IsNullOrWhiteSpace(act) || string.IsNullOrWhiteSpace(rd) || string.IsNullOrWhiteSpace(verd))
            return "";

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
