namespace ProverkaRD.Engine;

/// <summary>Сравнение тега релиза vX.Y.Z с версией сборки.</summary>
public static class ReleaseVersion
{
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
        if (!Version.TryParse(value, out var parsed))
            return false;

        version = new Version(
            parsed.Major,
            parsed.Minor,
            parsed.Build < 0 ? 0 : parsed.Build,
            parsed.Revision < 0 ? 0 : parsed.Revision);
        return true;
    }

    public static bool IsNewer(string? remoteTag, string? current) =>
        TryParse(remoteTag, out var remote)
        && TryParse(current, out var local)
        && remote > local;
}
