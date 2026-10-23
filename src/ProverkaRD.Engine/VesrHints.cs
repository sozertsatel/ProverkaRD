namespace ProverkaRD.Engine;

/// <summary>Если код акта в ВЭСР не найден или текст не совпал, ищем то же наименование.</summary>
public static class VesrHints
{
    public const string Phrase = "в ВЭСР этот текст стоит под номером ";

    public static string? Locate(VesrBook? vesr, string? actText, string? exceptCode)
    {
        if (vesr is null || string.IsNullOrWhiteSpace(actText))
            return null;
        var key = TextMatch.Normalize(actText);
        if (key.Length == 0)
            return null;
        var except = TextMatch.CodeKey(exceptCode);
        var numbers = vesr.Rows
            .Where(row => TextMatch.Normalize(row.Name) == key)
            .Select(row => row.Code.Trim())
            .Where(code => code.Length > 0 && TextMatch.CodeKey(code) != except)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
        if (numbers.Count == 0)
            return null;
        return Phrase + string.Join(", ", numbers);
    }
}
