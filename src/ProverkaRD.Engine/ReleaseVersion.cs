namespace ProverkaRD.Engine;

/// <summary>
/// Сравнение тега релиза с версией сборки через <see cref="Version"/>.
/// Сырой <see cref="Version"/> считает пропущенную часть равной −1, а −1 меньше 0:
/// «0.1.8» &lt; «0.1.8.0». Префикс «v» и суффикс «+hash» он вообще не разбирает.
/// Здесь недостающие цифры становятся 0, «v» и хвост после «+», «-» или пробела снимаются.
/// </summary>
public static class ReleaseVersion
{
    public const string CurrentText = "У вас актуальная версия";

    /// <summary>«У вас актуальная версия (0.1.9)». Хвост +hash и лишний .0 в скобки не попадают.</summary>
    public static string CurrentWithVersion(string? installed)
    {
        var shown = Display(installed);
        return shown.Length == 0 ? CurrentText : CurrentText + " (" + shown + ")";
    }

    public static string Display(string? text)
    {
        if (!TryParse(text, out var version))
            return "";
        if (version.Revision > 0)
            return version.ToString();
        return version.Major + "." + version.Minor + "." + version.Build;
    }

    public static bool TryParse(string? text, out Version version)
    {
        version = new Version(0, 0);
        if (string.IsNullOrWhiteSpace(text))
            return false;

        var value = text.Trim();
        if (value[0] is 'v' or 'V')
            value = value[1..];
        var cut = value.IndexOfAny(['-', '+', ' ']);
        if (cut >= 0)
            value = value[..cut];
        if (!Version.TryParse(value, out var parsed) || parsed is null)
            return false;

        version = new Version(
            parsed.Major,
            parsed.Minor,
            parsed.Build < 0 ? 0 : parsed.Build,
            parsed.Revision < 0 ? 0 : parsed.Revision);
        return true;
    }

    /// <summary>Меньше нуля — релиз старше сборки, 0 — тот же, больше нуля — релиз новее. null — строку не разобрать.</summary>
    public static int? Compare(string? remoteTag, string? current)
    {
        if (!TryParse(remoteTag, out var remote) || !TryParse(current, out var local))
            return null;
        return remote.CompareTo(local);
    }

    public static bool IsNewer(string? remoteTag, string? current) => Compare(remoteTag, current) > 0;
}
