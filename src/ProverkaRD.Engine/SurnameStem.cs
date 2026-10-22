namespace ProverkaRD.Engine;

/// <summary>
/// Основа фамилии: падежные окончания снимаются, чтобы «Павлову» совпало с «Павлов»,
/// а «Краснощеком» — с «Краснощек».
/// </summary>
public static class SurnameStem
{
    private static readonly string[] Endings =
    [
        "ого", "его", "ому", "ему", "ыми", "ими",
        "ом", "ем", "ым", "им", "ой", "ей", "ою", "ею",
        "у", "ю", "а", "я",
    ];

    public static string Of(string? surname)
    {
        var stem = TextMatch.Normalize(surname);
        if (stem.Length < 5)
            return stem;

        foreach (var ending in Endings)
        {
            if (stem.Length - ending.Length < 4)
                continue;
            if (!stem.EndsWith(ending, StringComparison.Ordinal))
                continue;
            return stem[..^ending.Length];
        }

        return stem;
    }

    public static bool Same(string? left, string? right)
    {
        var a = Of(left);
        var b = Of(right);
        return a.Length >= 4 && a == b;
    }

    public static bool IsIgnored(string? surname, IEnumerable<string>? ignored)
    {
        if (ignored is null || string.IsNullOrWhiteSpace(surname))
            return false;
        return ignored.Any(item => Same(item, surname) || TextMatch.Normalize(item) == TextMatch.Normalize(surname));
    }
}
