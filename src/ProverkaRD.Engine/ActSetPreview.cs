using System.Text.RegularExpressions;
using ProverkaRD.Engine.Parsing;

namespace ProverkaRD.Engine;

/// <summary>
/// До проверки: сколько PDF нашлось на акт. Папка с именем элемента и шифр в имени файла
/// считаются совпадением. Расхождение «папка не равна шифру» отдельно не выводится.
/// </summary>
public static partial class ActSetPreview
{
    public sealed record Line(string Element, string Act, int PdfCount, bool Missing, string Text);

    public sealed record Result(IReadOnlyList<Line> Lines, IReadOnlyList<string> Unmatched);

    public static Result Build(IReadOnlyList<ActDocument> acts, IReadOnlyList<string> rdPaths)
    {
        var pdfs = CollectPdfs(rdPaths);
        var described = acts.Select(Describe).ToList();
        var used = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var lines = new List<Line>();
        foreach (var act in described)
        {
            var count = 0;
            foreach (var pdf in pdfs)
            {
                if (!Matches(act, pdf))
                    continue;
                count++;
                used.Add(pdf.Path);
            }

            var missing = count == 0;
            var element = act.Element.Length == 0 ? "без номера" : act.Element;
            var text = element + " · " + act.Label + " · " + (missing
                ? "комплекты не добавлены"
                : count + " PDF");
            lines.Add(new Line(element, act.Label, count, missing, text));
        }

        var actElements = described
            .Select(act => act.ElementKey)
            .Where(key => key.Length > 0)
            .ToHashSet(StringComparer.Ordinal);
        var unmatched = new List<string>();
        foreach (var folder in pdfs
                     .Select(pdf => pdf.ElementFolder)
                     .Where(name => name.Length > 0)
                     .Distinct(StringComparer.OrdinalIgnoreCase)
                     .OrderBy(name => name, StringComparer.CurrentCultureIgnoreCase))
        {
            if (!actElements.Contains(TextMatch.CodeKey(folder)))
                unmatched.Add("Папка " + folder + " — нет акта");
        }

        foreach (var pdf in pdfs.OrderBy(pdf => pdf.Name, StringComparer.CurrentCultureIgnoreCase))
        {
            if (used.Contains(pdf.Path))
                continue;
            if (pdf.ElementFolder.Length > 0 && !actElements.Contains(TextMatch.CodeKey(pdf.ElementFolder)))
                continue;
            unmatched.Add(pdf.Name + " — нет акта");
        }

        return new Result(lines, unmatched);
    }

    private static bool Matches(ActInfo act, PdfFile pdf)
    {
        if (act.ElementKey.Length > 0
            && pdf.ElementKey.Length > 0
            && string.Equals(act.ElementKey, pdf.ElementKey, StringComparison.Ordinal))
            return true;
        if (pdf.CipherKey.Length == 0)
            return false;
        return act.CipherKeys.Contains(pdf.CipherKey);
    }

    private static ActInfo Describe(ActDocument act)
    {
        var element = First(
            act.ElementNoFromFileName,
            act.ElementNoFromHeading,
            act.Kits.Select(kit => kit.ElementNo).FirstOrDefault(value => !string.IsNullOrWhiteSpace(value)));
        var ciphers = act.Kits
            .Select(kit => TextMatch.CodeKey(kit.Cipher))
            .Where(key => key.Length > 0)
            .ToHashSet(StringComparer.Ordinal);
        return new ActInfo(
            element,
            TextMatch.CodeKey(element),
            act.DisplayName,
            ciphers);
    }

    private static string First(params string?[] values) =>
        values.FirstOrDefault(value => !string.IsNullOrWhiteSpace(value))?.Trim() ?? "";

    private static List<PdfFile> CollectPdfs(IReadOnlyList<string> rdPaths)
    {
        var files = new List<PdfFile>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var path in rdPaths)
        {
            if (string.IsNullOrWhiteSpace(path))
                continue;
            var io = SafePath.ForIo(path);
            if (Directory.Exists(io))
            {
                IEnumerable<string> found;
                try
                {
                    found = Directory.EnumerateFiles(io, "*.pdf", SearchOption.AllDirectories);
                }
                catch (Exception)
                {
                    continue;
                }

                foreach (var file in found)
                    Add(files, seen, file);
                continue;
            }

            if (path.EndsWith(".pdf", StringComparison.OrdinalIgnoreCase))
                Add(files, seen, io);
        }

        return files;
    }

    private static void Add(List<PdfFile> files, HashSet<string> seen, string path)
    {
        if (RdFileRules.IsAuxiliary(path) || !seen.Add(path))
            return;
        var folder = ElementFolder(path);
        var cipher = RdParser.FindCipher(Path.GetFileNameWithoutExtension(path)) ?? "";
        files.Add(new PdfFile(
            path,
            Path.GetFileName(path),
            folder,
            TextMatch.CodeKey(folder),
            TextMatch.CodeKey(cipher)));
    }

    private static string ElementFolder(string path)
    {
        var dir = Path.GetDirectoryName(path);
        while (!string.IsNullOrEmpty(dir))
        {
            var name = Path.GetFileName(dir).Replace(',', '.');
            if (ElementFolderName().IsMatch(name))
                return name;
            var parent = Path.GetDirectoryName(dir);
            if (string.Equals(parent, dir, StringComparison.OrdinalIgnoreCase))
                break;
            dir = parent;
        }

        return "";
    }

    private sealed record ActInfo(string Element, string ElementKey, string Label, HashSet<string> CipherKeys);

    private sealed record PdfFile(string Path, string Name, string ElementFolder, string ElementKey, string CipherKey);

    [GeneratedRegex(@"^\d+\.\d+$")]
    private static partial Regex ElementFolderName();
}
