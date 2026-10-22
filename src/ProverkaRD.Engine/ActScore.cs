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
        if (Hit(act, finding.Act, finding.ElementNo))
            return true;
        return finding.Locations.Any(place => Hit(act, place.Act, place.ElementNo));
    }

    public static bool Concerns(ActResult act, KitResult kit) =>
        Hit(act, kit.ActName, kit.ElementNo);

    private static bool Hit(ActResult act, string? actName, string? elementNo)
    {
        if (!string.IsNullOrWhiteSpace(actName)
            && actName != "—"
            && string.Equals(actName, act.FileName, StringComparison.OrdinalIgnoreCase))
            return true;
        if (string.IsNullOrWhiteSpace(elementNo) || string.IsNullOrWhiteSpace(act.ElementNo))
            return false;
        foreach (var part in elementNo.Split([',', ';'], StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries))
        {
            if (string.Equals(TextMatch.CodeKey(part), TextMatch.CodeKey(act.ElementNo), StringComparison.Ordinal))
                return true;
        }
        return false;
    }
}
