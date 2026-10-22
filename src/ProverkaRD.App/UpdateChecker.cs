using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;
using ProverkaRD.Engine;

namespace ProverkaRD;

public enum UpdateCheckKind
{
    Newer,
    UpToDate,
    Unavailable,
    Offline,
}

public sealed record UpdateCheckResult(
    UpdateCheckKind Kind,
    string? RemoteTag,
    string Message,
    string? Notes = null,
    string? PageUrl = null,
    string? ExeUrl = null,
    long ExeSize = 0,
    string? HashUrl = null);

public static class UpdateChecker
{
    private static readonly HttpClient Http = CreateClient();

    public static async Task<UpdateCheckResult> CheckAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            using var response = await Http.GetAsync(AppInfo.LatestReleaseApi, cancellationToken);
            if (response.StatusCode is HttpStatusCode.NotFound
                or HttpStatusCode.Unauthorized
                or HttpStatusCode.Forbidden)
                return Unavailable();
            if (!response.IsSuccessStatusCode)
                return Unavailable();

            var body = await response.Content.ReadAsStringAsync(cancellationToken);
            ReleaseInfo? release;
            try
            {
                release = ReleaseCatalog.Parse(body);
            }
            catch (JsonException)
            {
                return Unavailable();
            }

            if (release is null || !ReleaseVersion.TryParse(release.Tag, out _))
                return Unavailable();

            var page = string.IsNullOrWhiteSpace(release.PageUrl) ? AppInfo.ReleasesPage : release.PageUrl;
            if (ReleaseVersion.IsNewer(release.Tag, AppInfo.Version))
            {
                return new UpdateCheckResult(
                    UpdateCheckKind.Newer,
                    release.Tag,
                    $"Доступна версия {release.Tag}. Сейчас установлена {AppInfo.Version}.",
                    release.Body,
                    page,
                    release.Exe?.Url,
                    release.Exe?.Size ?? 0,
                    release.Sha256?.Url);
            }

            return new UpdateCheckResult(
                UpdateCheckKind.UpToDate,
                release.Tag,
                $"Установлена актуальная версия {AppInfo.Version}.",
                release.Body,
                page,
                release.Exe?.Url,
                release.Exe?.Size ?? 0,
                release.Sha256?.Url);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or IOException)
        {
            return new UpdateCheckResult(UpdateCheckKind.Offline, null, "");
        }
    }

    private static UpdateCheckResult Unavailable() => new(
        UpdateCheckKind.Unavailable,
        null,
        "Не удалось проверить обновления. Закрытый репозиторий без входа отвечает «не найдено». Страницу релизов можно открыть вручную.");

    private static HttpClient CreateClient()
    {
        var client = new HttpClient { Timeout = TimeSpan.FromSeconds(15) };
        client.DefaultRequestHeaders.UserAgent.ParseAdd("ProverkaRD");
        client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/vnd.github+json"));
        return client;
    }
}
