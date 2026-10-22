namespace ProverkaRD.Engine;

public static class FindingGrouper
{
    public const string SignerMissing = "Подписант не найден в документе";

    public static List<Finding> Apply(IReadOnlyList<Finding> findings, CheckOptions? options)
    {
        options ??= new CheckOptions();
        var plain = new List<Finding>();
        var signerGroups = new Dictionary<string, List<Finding>>(StringComparer.Ordinal);
        var duplicateGroups = new Dictionary<string, List<Finding>>(StringComparer.Ordinal);

        foreach (var finding in findings)
        {
            var located = WithLocation(finding);
            if (options.GroupSignerWarnings
                && finding.Severity == Severity.Warning
                && finding.CheckName == SignerMissing)
            {
                var key = SurnameStem.Of(finding.ValueA);
                if (key.Length == 0)
                    key = TextMatch.Normalize(finding.ValueA);
                AddTo(signerGroups, key, located);
                continue;
            }

            if (options.GroupDuplicateFindings && finding.Severity != Severity.Ok)
            {
                var key = finding.CheckName
                          + "\n" + finding.Severity
                          + "\n" + TextMatch.Normalize(finding.InAct)
                          + "\n" + TextMatch.Normalize(finding.InRd)
                          + "\n" + TextMatch.Normalize(finding.InVerd)
                          + "\n" + TextMatch.Normalize(finding.InVesr)
                          + "\n" + TextMatch.Normalize(finding.ValueA)
                          + "\n" + TextMatch.Normalize(finding.ValueB);
                AddTo(duplicateGroups, key, located);
                continue;
            }

            plain.Add(located);
        }

        var result = new List<Finding>(plain);
        foreach (var group in signerGroups.Values)
            result.Add(Merge(group, signer: true));
        foreach (var group in duplicateGroups.Values)
            result.Add(group.Count == 1 ? group[0] : Merge(group, signer: false));
        return result;
    }

    private static void AddTo(Dictionary<string, List<Finding>> groups, string key, Finding finding)
    {
        if (!groups.TryGetValue(key, out var list))
            groups[key] = list = [];
        list.Add(finding);
    }

    private static Finding WithLocation(Finding finding)
    {
        if (finding.Locations.Count > 0)
            return finding;
        return finding with
        {
            Locations =
            [
                new FindingPlace(finding.Act, finding.ElementNo, finding.Kit, finding.ActPath),
            ],
        };
    }

    private static Finding Merge(List<Finding> group, bool signer)
    {
        var first = group[0];
        var locations = group.SelectMany(f => f.Locations).Distinct().ToList();
        var elements = locations
            .Select(p => p.ElementNo)
            .Where(s => !string.IsNullOrWhiteSpace(s))
            .Distinct(StringComparer.Ordinal)
            .ToList();
        elements.Sort(static (a, b) => ElementOrder.Compare(a, b));
        var kits = locations
            .Select(p => p.Kit)
            .Where(s => !string.IsNullOrWhiteSpace(s) && s != "—")
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
        var acts = locations
            .Select(p => p.Act)
            .Where(s => !string.IsNullOrWhiteSpace(s) && s != "—")
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        return first with
        {
            Act = acts.Count == 0 ? first.Act : string.Join(", ", acts),
            Kit = kits.Count == 0 ? first.Kit : string.Join(", ", kits),
            ElementNo = elements.Count == 0 ? first.ElementNo : string.Join(", ", elements),
            ValueB = signer ? "" : first.ValueB,
            Explanation = "",
            Locations = locations,
        };
    }
}
