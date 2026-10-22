namespace ProverkaRD.Engine;

/// <summary>Пути длиннее 260 символов на Windows открываются через префикс \\?\.</summary>
public static class SafePath
{
    public const int LegacyMax = 260;

    public static string ForIo(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
            return path;
        string full;
        try
        {
            full = Path.GetFullPath(path);
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
        {
            return path;
        }

        if (!OperatingSystem.IsWindows())
            return full;
        return PrefixIfLong(full);
    }

    public static string PrefixIfLong(string fullPath)
    {
        if (string.IsNullOrEmpty(fullPath) || fullPath.StartsWith(@"\\?\", StringComparison.Ordinal))
            return fullPath;
        if (fullPath.Length < LegacyMax)
            return fullPath;
        if (fullPath.StartsWith(@"\\", StringComparison.Ordinal))
            return @"\\?\UNC\" + fullPath[2..];
        return @"\\?\" + fullPath;
    }

    public static string FileName(string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
            return "";
        try
        {
            var trimmed = path.TrimEnd('\\', '/');
            var cut = Math.Max(trimmed.LastIndexOf('\\'), trimmed.LastIndexOf('/'));
            return cut >= 0 ? trimmed[(cut + 1)..] : trimmed;
        }
        catch (ArgumentException)
        {
            return path;
        }
    }
}
