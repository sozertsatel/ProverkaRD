using System.Text;

namespace ProverkaRD.Engine;

/// <summary>
/// Сравнение фамилии штампа и подписанта сертификата:
/// ё/е, регистр, лишние пробелы и инициалы не мешают совпадению.
/// Если инициалы есть с обеих сторон и они разные — это не тот человек.
/// </summary>
public static class SignerNames
{
    public static bool Matches(string? document, string? signer)
    {
        if (string.IsNullOrWhiteSpace(document) || string.IsNullOrWhiteSpace(signer))
            return false;
        if (SurnameStem.Same(document, signer))
            return InitialsCompatible(document, signer);

        var left = Surnames(document);
        var right = Surnames(signer);
        if (left.Count == 0 || right.Count == 0)
            return false;
        var same = left.Any(name => right.Any(other => SurnameStem.Same(name, other)));
        return same && InitialsCompatible(document, signer);
    }

    private static bool InitialsCompatible(string? document, string? signer)
    {
        var left = Initials(document);
        var right = Initials(signer);
        if (left.Count == 0 || right.Count == 0)
            return true;
        if (left.Count != right.Count)
            return false;
        for (var i = 0; i < left.Count; i++)
        {
            if (!string.Equals(left[i], right[i], StringComparison.Ordinal))
                return false;
        }

        return true;
    }

    private static List<string> Surnames(string? value) =>
        Tokens(value).Where(token => token.Length >= 3).ToList();

    private static List<string> Initials(string? value) =>
        Tokens(value).Where(token => token.Length == 1).ToList();

    private static List<string> Tokens(string? value)
    {
        var folded = Fold(value);
        if (folded.Length == 0)
            return [];
        return folded.Split(' ', StringSplitOptions.RemoveEmptyEntries).ToList();
    }

    private static string Fold(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return "";
        var sb = new StringBuilder(value.Length);
        foreach (var ch in value)
        {
            var lower = char.ToLowerInvariant(ch);
            if (lower == 'ё')
                lower = 'е';
            if (char.IsLetter(lower))
                sb.Append(lower);
            else if (lower == '-')
                sb.Append('-');
            else
                sb.Append(' ');
        }

        return string.Join(' ', sb.ToString().Split(' ', StringSplitOptions.RemoveEmptyEntries));
    }
}
