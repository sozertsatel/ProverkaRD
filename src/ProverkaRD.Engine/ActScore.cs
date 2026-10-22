namespace ProverkaRD.Engine;

public readonly record struct ActScoreResult(Severity Overall, int Critical, int Formatting, int Warnings);

/// <summary>Итог акта — худший статус его замечаний и комплектов.</summary>
public static class ActScore
{
    public static ActScoreResult Score(ActResult act, IEnumerable<Finding> findings, IEnumerable<KitResult> kits)
    {
        var severities = new List<Severity>
        {
            act.NameStatus,
            act.CostNameStatus,
            act.CodeStatus,
            act.NetStatus,
            act.GrossStatus,
            act.CompletenessStatus,
        };
        var critical = 0;
        var formatting = 0;
        var warnings = 0;
        foreach (var finding in findings)
        {
            if (!Concerns(act, finding))
                continue;
            severities.Add(finding.Severity);
            switch (finding.Severity)
            {
                case Severity.Critical: critical++; break;
                case Severity.Formatting: formatting++; break;
                case Severity.Warning: warnings++; break;
            }
        }

        foreach (var kit in kits)
        {
            if (Concerns(act, kit))
                severities.Add(kit.Overall);
        }

        return new ActScoreResult(SeverityText.Worst(severities), critical, formatting, warnings);
    }

    public static bool Concerns(ActResult act, Finding finding)
    {
        if (Hit(act, finding.Act, finding.ElementNo, finding.ActPath))
            return true;
        return finding.Locations.Any(place => Hit(act, place.Act, place.ElementNo, place.ActPath));
    }

    public static bool Concerns(ActResult act, KitResult kit) =>
        Hit(act, kit.ActName, kit.ElementNo, kit.SourcePath);

    public static bool Concerns(ActResult act, string? actPath) =>
        !string.IsNullOrWhiteSpace(act.SourcePath)
        && !string.IsNullOrWhiteSpace(actPath)
        && string.Equals(act.SourcePath, actPath, StringComparison.OrdinalIgnoreCase);

    private static bool Hit(ActResult act, string? actName, string? elementNo) =>
        Hit(act, actName, elementNo, "");

    private static bool Hit(ActResult act, string? actName, string? elementNo, string? actPath)
    {
        if (Concerns(act, actPath))
            return true;
        if (SameActName(act.FileName, actName))
            return true;
        return SameElement(act.ElementNo, elementNo);
    }

    private static bool SameActName(string? left, string? right)
    {
        if (string.IsNullOrWhiteSpace(left) || string.IsNullOrWhiteSpace(right) || right == "—")
            return false;
        var a = OfficeName(left);
        var b = OfficeName(right);
        return string.Equals(a, b, StringComparison.OrdinalIgnoreCase);
    }

    private static string OfficeName(string value)
    {
        var name = value.Trim();
        foreach (var extension in new[] { ".docx", ".doc", ".pdf", ".xlsx", ".xlsm" })
        {
            if (name.EndsWith(extension, StringComparison.OrdinalIgnoreCase))
                return name[..^extension.Length];
        }
        return name;
    }

    private static bool SameElement(string? actElement, string? other)
    {
        var left = TextMatch.CodeKey(actElement);
        if (left.Length == 0 || string.IsNullOrWhiteSpace(other))
            return false;
        foreach (var part in other.Split([',', ';'], StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries))
        {
            if (string.Equals(TextMatch.CodeKey(part), left, StringComparison.Ordinal))
                return true;
        }
        return false;
    }
}
