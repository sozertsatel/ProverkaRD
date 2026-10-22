using System.Security.Cryptography;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace ProverkaRD.Engine;

public sealed record ReleaseAsset(string Name, string Url, long Size);

public sealed record ReleaseInfo(
    string Tag,
    string Body,
    string PageUrl,
    ReleaseAsset? Exe,
    ReleaseAsset? Sha256);

/// <summary>Разбор ответа GitHub releases/latest: версия, тело и ассеты exe/sha256.</summary>
public static partial class ReleaseCatalog
{
    public const string ExeName = "ProverkaRD.exe";
    public const string HashName = "ProverkaRD.exe.sha256";

    public static ReleaseInfo? Parse(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
            return null;

        using var document = JsonDocument.Parse(json);
        var root = document.RootElement;
        var tag = ReadString(root, "tag_name");
        if (string.IsNullOrWhiteSpace(tag))
            return null;

        ReleaseAsset? exe = null;
        ReleaseAsset? hash = null;
        if (root.TryGetProperty("assets", out var assets) && assets.ValueKind == JsonValueKind.Array)
        {
            foreach (var asset in assets.EnumerateArray())
            {
                var name = ReadString(asset, "name");
                var url = ReadString(asset, "browser_download_url");
                var size = asset.TryGetProperty("size", out var sizeValue) && sizeValue.TryGetInt64(out var bytes)
                    ? bytes
                    : 0L;
                if (name.Equals(ExeName, StringComparison.OrdinalIgnoreCase))
                    exe = new ReleaseAsset(name, url, size);
                else if (name.Equals(HashName, StringComparison.OrdinalIgnoreCase))
                    hash = new ReleaseAsset(name, url, size);
            }
        }

        return new ReleaseInfo(tag, ReadString(root, "body"), ReadString(root, "html_url"), exe, hash);
    }

    public static string? ReadHash(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return null;
        var match = Hex64().Match(text);
        return match.Success ? match.Value.ToLowerInvariant() : null;
    }

    public static bool HashMatches(byte[] content, string? hashFileText)
    {
        var expected = ReadHash(hashFileText);
        if (expected is null || content is null)
            return false;
        var actual = Convert.ToHexString(SHA256.HashData(content)).ToLowerInvariant();
        return actual == expected;
    }

    public static bool HashMatches(Stream content, string? hashFileText)
    {
        var expected = ReadHash(hashFileText);
        if (expected is null || content is null)
            return false;
        var actual = Convert.ToHexString(SHA256.HashData(content)).ToLowerInvariant();
        return actual == expected;
    }

    private static string ReadString(JsonElement element, string name) =>
        element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString() ?? ""
            : "";

    [GeneratedRegex(@"[0-9a-fA-F]{64}")]
    private static partial Regex Hex64();
}
