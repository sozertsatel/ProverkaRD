namespace ProverkaRD.Engine;

/// <summary>
/// Если два из трёх текстов совпадают точно, подпись указывает на отличающийся источник.
/// Совпадение — только «Совпадает», не ошибка оформления. Критичность при этом не меняется.
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

        var actRd = TextMatch.Compare(act, rd) == Severity.Ok;
        var actVerd = TextMatch.Compare(act, verd) == Severity.Ok;
        var rdVerd = TextMatch.Compare(rd, verd) == Severity.Ok;
        if (actRd && actVerd && rdVerd)
            return "";
        if (rdVerd && !actRd && !actVerd)
            return InAct;
        if (actVerd && !actRd && !rdVerd)
            return InRd;
        if (actRd && !actVerd && !rdVerd)
            return InVerd;
        return "";
    }
}
