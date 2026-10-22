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

public sealed record UpdateCheckResult(UpdateCheckKind Kind, string? RemoteTag, string Message);

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
            string? tag;
            try
            {
                tag = ReadTag(body);
            }
            catch (JsonException)
            {
                return Unavailable();
            }

            if (string.IsNullOrWhiteSpace(tag) || !ReleaseVersion.TryParse(tag, out _))
                return Unavailable();

            if (ReleaseVersion.IsNewer(tag, AppInfo.Version))
            {
                return new UpdateCheckResult(
                    UpdateCheckKind.Newer,
                    tag,
                    $"Доступна версия {tag}. Сейчас установлена {AppInfo.Version}. Откройте страницу релизов, чтобы скачать новую сборку.");
            }

            return new UpdateCheckResult(
                UpdateCheckKind.UpToDate,
                tag,
                $"Установлена актуальная версия {AppInfo.Version}.");
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

    private static string? ReadTag(string json)
    {
        using var document = JsonDocument.Parse(json);
        return document.RootElement.TryGetProperty("tag_name", out var tag) ? tag.GetString() : null;
    }

    private static HttpClient CreateClient()
    {
        var client = new HttpClient { Timeout = TimeSpan.FromSeconds(15) };
        client.DefaultRequestHeaders.UserAgent.ParseAdd("ProverkaRD");
        client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/vnd.github+json"));
        return client;
    }
}
