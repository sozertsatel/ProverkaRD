namespace ProverkaRD.Engine;

/// <summary>Соседние пропуски с одной причиной сжимаются в «строки 552–571 (20)».</summary>
public static class NoticeRanges
{
    public static List<Notice> Group(IEnumerable<LedgerRowIssue> issues)
    {
        var result = new List<Notice>();
        foreach (var source in issues.GroupBy(issue => issue.Source))
        {
            var ordered = source.OrderBy(issue => issue.Row).ToList();
            var start = 0;
            while (start < ordered.Count)
            {
                var end = start;
                while (end + 1 < ordered.Count
                       && ordered[end + 1].Row == ordered[end].Row + 1
                       && string.Equals(ordered[end + 1].Reason, ordered[start].Reason, StringComparison.Ordinal))
                    end++;
                var count = end - start + 1;
                var place = count == 1
                    ? "строка " + ordered[start].Row
                    : "строки " + ordered[start].Row + "–" + ordered[end].Row + " (" + count + ")";
                result.Add(new Notice(ordered[start].Source, place, ordered[start].Reason));
                start = end + 1;
            }
        }

        return result;
    }

    public static IEnumerable<LedgerRowIssue> ForActs(
        IEnumerable<LedgerRowIssue> issues,
        string source,
        IReadOnlySet<string> owners)
    {
        foreach (var issue in issues)
        {
            if (!string.Equals(issue.Source, source, StringComparison.Ordinal))
                continue;
            var owner = TextMatch.CodeKey(issue.Owner);
            if (owner.Length == 0 || !owners.Contains(owner))
                continue;
            yield return issue;
        }
    }
}
