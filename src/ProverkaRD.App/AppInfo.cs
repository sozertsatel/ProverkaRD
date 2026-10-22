using System.Reflection;

namespace ProverkaRD;

public static class AppInfo
{
    public const string RepoOwner = "sozertsatel";
    public const string RepoName = "ProverkaRD";

    public static string Version { get; } = ReadVersion();

    public static string WindowTitle => "Проверка РД " + Version;

    public static string ReleasesPage =>
        $"https://github.com/{RepoOwner}/{RepoName}/releases/latest";

    public static string LatestReleaseApi =>
        $"https://api.github.com/repos/{RepoOwner}/{RepoName}/releases/latest";

    private static string ReadVersion()
    {
        var informational = Assembly.GetExecutingAssembly()
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion;
        if (!string.IsNullOrWhiteSpace(informational))
        {
            var plus = informational.IndexOf('+');
            if (plus >= 0)
                informational = informational[..plus];
            return informational.Trim();
        }

        var version = Assembly.GetExecutingAssembly().GetName().Version;
        if (version is null)
            return "0.0.1";
        var build = version.Build < 0 ? 0 : version.Build;
        return $"{version.Major}.{version.Minor}.{build}";
    }
}
