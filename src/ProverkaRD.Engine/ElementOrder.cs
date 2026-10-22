using System.Text.RegularExpressions;

namespace ProverkaRD.Engine;

public static partial class ElementOrder
{
    public static int[] Key(string? value)
    {
        var text = value ?? "";
        var match = Number().Match(text);
        var source = match.Success ? match.Value : text;
        var parts = source.Split('.', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (parts.Length == 0)
            return [int.MaxValue];
        return parts.Select(part => int.TryParse(part.TrimEnd('.'), out var number) ? number : int.MaxValue).ToArray();
    }

    public static int Compare(string? left, string? right)
    {
        var a = Key(left);
        var b = Key(right);
        var n = Math.Min(a.Length, b.Length);
        for (var i = 0; i < n; i++)
        {
            var c = a[i].CompareTo(b[i]);
            if (c != 0)
                return c;
        }
        return a.Length.CompareTo(b.Length);
    }

    public static string First(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return "";
        var comma = value.IndexOf(',');
        return (comma < 0 ? value : value[..comma]).Trim();
    }

    [GeneratedRegex(@"\d+\.\d+")]
    private static partial Regex Number();
}
