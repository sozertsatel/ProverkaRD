using System.IO.Compression;
using System.Security.Cryptography;

namespace ProverkaRD.Engine;

/// <summary>
/// Zip-архивы и повторные копии комплекта. Одинаковые имя и содержимое читаются один раз.
/// </summary>
public static class RdIntake
{
    public static bool IsZip(string? path) =>
        !string.IsNullOrWhiteSpace(path) && path.EndsWith(".zip", StringComparison.OrdinalIgnoreCase);

    public static string ContentHash(string path)
    {
        using var stream = File.Open(SafePath.ForIo(path), FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        return Convert.ToHexString(SHA256.HashData(stream));
    }

    /// <summary>Имя файла и хеш содержимого. Разные пути с тем же именем и теми же байтами — один ключ.</summary>
    public static string DedupeKey(string path) =>
        Path.GetFileName(path).ToUpperInvariant() + "\n" + ContentHash(path);

    public static List<string> Deduplicate(IEnumerable<string> paths)
    {
        var seen = new HashSet<string>(StringComparer.Ordinal);
        var result = new List<string>();
        foreach (var path in paths)
        {
            if (string.IsNullOrWhiteSpace(path))
                continue;
            string key;
            try
            {
                key = DedupeKey(path);
            }
            catch
            {
                key = "path\n" + path;
            }

            if (seen.Add(key))
                result.Add(path);
        }

        return result;
    }

    /// <summary>PDF и DOCX из архива. Имя записи сохраняется, чтобы совпасть с распакованной копией.</summary>
    public static List<string> ExpandZip(string zipPath)
    {
        var root = Path.Combine(Path.GetTempPath(), "proverkard-zip", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        var files = new List<string>();
        using var archive = ZipFile.OpenRead(SafePath.ForIo(zipPath));
        var index = 0;
        foreach (var entry in archive.Entries)
        {
            var name = Path.GetFileName(entry.Name);
            if (string.IsNullOrEmpty(name))
                continue;
            if (name.StartsWith("~$", StringComparison.Ordinal) || name.EndsWith(".sig", StringComparison.OrdinalIgnoreCase))
                continue;
            if (RdFileRules.IsAuxiliary(name))
                continue;
            var extension = Path.GetExtension(name);
            if (!extension.Equals(".pdf", StringComparison.OrdinalIgnoreCase)
                && !extension.Equals(".docx", StringComparison.OrdinalIgnoreCase))
                continue;

            var folder = Path.Combine(root, index.ToString("000"));
            Directory.CreateDirectory(folder);
            var dest = Path.Combine(folder, name);
            entry.ExtractToFile(dest, overwrite: true);
            files.Add(dest);
            index++;
        }

        return files;
    }
}
