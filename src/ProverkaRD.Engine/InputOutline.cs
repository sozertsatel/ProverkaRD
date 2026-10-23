using System.Text.RegularExpressions;
using ProverkaRD.Engine.Parsing;

namespace ProverkaRD.Engine;

/// <summary>
/// Дерево «акт → файлы» до проверки. Папка с номером элемента и шифр в имени файла
/// относят PDF к акту. Расхождение «папка не равна шифру» отдельно не выводится.
/// </summary>
public static partial class InputOutline
{
    public const string OrphanTitle = "Без акта";

    public sealed record FileEntry(string Path, bool HasSignature);

    public sealed record Node(
        string Title,
        string Kind,
        string Path,
        bool Problem,
        bool Expand,
        IReadOnlyList<Node> Children);

    public sealed record Model(string Summary, IReadOnlyList<Node> Nodes);

    /// <summary>
    /// Файлы дерева: переданные комплекты плюс .СВОР и _ИЗМ из папок и из каталога каждого файла.
    /// Пустые списки ничего с диска не подхватывают — «Очистить» поэтому убирает и «Без акта».
    /// </summary>
    public static IReadOnlyList<FileEntry> Discover(
        IEnumerable<FileEntry> files,
        IEnumerable<string> folders,
        IEnumerable<string>? hidden = null)
    {
        var skip = new HashSet<string>(hidden ?? [], StringComparer.OrdinalIgnoreCase);
        var result = new List<FileEntry>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var listed = files.Where(file => !string.IsNullOrWhiteSpace(file.Path)).ToList();
        foreach (var file in listed)
        {
            if (skip.Contains(file.Path) || !seen.Add(file.Path))
                continue;
            result.Add(file);
        }

        foreach (var folder in folders)
        {
            if (!string.IsNullOrWhiteSpace(folder))
                AddAuxiliary(folder, recursive: true, result, seen, skip);
        }

        foreach (var file in listed)
        {
            var parent = Path.GetDirectoryName(file.Path);
            if (!string.IsNullOrEmpty(parent))
                AddAuxiliary(parent, recursive: false, result, seen, skip);
        }

        return result;
    }

    public static Model Build(IReadOnlyList<ActDocument> acts, IReadOnlyList<FileEntry> files)
    {
        var described = acts.Select(Describe).ToList();
        var entries = files
            .Where(file => !string.IsNullOrWhiteSpace(file.Path))
            .GroupBy(file => file.Path, StringComparer.OrdinalIgnoreCase)
            .Select(group => group.First())
            .Select(file => Classify(file))
            .ToList();

        var nodes = new List<Node>();
        foreach (var act in described
                     .OrderBy(act => act.Element, Comparer<string>.Create(ElementOrder.Compare))
                     .ThenBy(act => act.Label, StringComparer.CurrentCultureIgnoreCase))
        {
            var children = entries
                .Where(file => Matches(act, file))
                .OrderBy(file => file.Name, StringComparer.CurrentCultureIgnoreCase)
                .Select(file => new Node(ChildTitle(file), "file", file.Path, false, false, []))
                .ToList();
            var pdfs = entries.Count(file => Matches(act, file) && file.IsPdf && !file.Auxiliary);
            var missing = pdfs == 0;
            var element = act.Element.Length == 0 ? "без номера" : act.Element;
            var tail = missing ? "комплекты не добавлены" : pdfs + " PDF";
            nodes.Add(new Node(
                element + " · " + act.Label + " · " + tail,
                "act",
                act.Path,
                missing,
                missing,
                children));
        }

        var orphans = entries
            .Where(file => !described.Any(act => Matches(act, file)))
            .OrderBy(file => file.Name, StringComparer.CurrentCultureIgnoreCase)
            .Select(file => new Node(ChildTitle(file), "file", file.Path, false, false, []))
            .ToList();
        if (orphans.Count > 0)
        {
            nodes.Add(new Node(OrphanTitle, "group", "", true, true, orphans));
        }

        var missingActs = nodes.Count(node => node.Kind == "act" && node.Problem);
        return new Model(Summarize(described.Count, missingActs, orphans.Count), nodes);
    }

    public static string Summarize(int acts, int withoutSets, int unmatched)
    {
        if (acts == 0 && unmatched == 0)
            return "Акты и комплекты не добавлены";

        var text = acts == 0
            ? "Акты не добавлены"
            : CountPhrase(acts, "акт", "акта", "актов");
        if (acts > 0)
        {
            text += withoutSets == 0
                ? ", все с комплектами ✓"
                : withoutSets == 1
                    ? ", 1 без комплекта"
                    : ", " + withoutSets + " без комплектов";
        }

        if (unmatched > 0)
            text += ", " + CountPhrase(unmatched, "файл без акта", "файла без акта", "файлов без акта");
        return text;
    }

    public static string CountPhrase(int count, string one, string few, string many)
    {
        var mod100 = count % 100;
        var mod10 = count % 10;
        var word = mod100 is >= 11 and <= 14
            ? many
            : mod10 == 1
                ? one
                : mod10 is >= 2 and <= 4
                    ? few
                    : many;
        return count + " " + word;
    }

    private static string ChildTitle(Stored file)
    {
        var mark = AuxiliaryMark(file.Name);
        if (mark.Length > 0)
            return file.Name + " · " + mark;
        return file.Name + " · " + (file.HasSignature ? "подпись рядом" : "нет .sig");
    }

    private static string AuxiliaryMark(string name)
    {
        var parts = new List<string>();
        if (name.Contains(".СВОР", StringComparison.OrdinalIgnoreCase))
            parts.Add("СВОР");
        if (name.Contains("_ИЗМ", StringComparison.OrdinalIgnoreCase))
            parts.Add("ИЗМ");
        return string.Join(", ", parts);
    }

    private static bool Matches(ActInfo act, Stored file)
    {
        if (act.ElementKey.Length > 0
            && file.ElementKey.Length > 0
            && string.Equals(act.ElementKey, file.ElementKey, StringComparison.Ordinal))
            return true;
        if (file.CipherKey.Length == 0)
            return false;
        return act.CipherKeys.Contains(file.CipherKey);
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
        return new ActInfo(act.FilePath, element, TextMatch.CodeKey(element), act.DisplayName, ciphers);
    }

    private static string First(params string?[] values) =>
        values.FirstOrDefault(value => !string.IsNullOrWhiteSpace(value))?.Trim() ?? "";

    private static Stored Classify(FileEntry file)
    {
        var name = Path.GetFileName(file.Path);
        var folder = ElementFolder(file.Path);
        var cipher = RdParser.FindCipher(Path.GetFileNameWithoutExtension(file.Path)) ?? "";
        var auxiliary = RdFileRules.IsAuxiliary(file.Path);
        return new Stored(
            file.Path,
            name,
            folder,
            TextMatch.CodeKey(folder),
            TextMatch.CodeKey(cipher),
            file.HasSignature,
            auxiliary,
            name.EndsWith(".pdf", StringComparison.OrdinalIgnoreCase));
    }

    private static void AddAuxiliary(
        string directory,
        bool recursive,
        List<FileEntry> files,
        HashSet<string> seen,
        HashSet<string> skip)
    {
        var root = SafePath.ForIo(directory);
        if (!Directory.Exists(root))
            return;
        IEnumerable<string> found;
        try
        {
            found = Directory.EnumerateFiles(root, "*", recursive ? SearchOption.AllDirectories : SearchOption.TopDirectoryOnly);
        }
        catch (Exception)
        {
            return;
        }

        foreach (var path in found)
        {
            if (!RdFileRules.IsAuxiliary(path) || skip.Contains(path) || !seen.Add(path))
                continue;
            files.Add(new FileEntry(path, false));
        }
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

    private sealed record ActInfo(string Path, string Element, string ElementKey, string Label, HashSet<string> CipherKeys);

    private sealed record Stored(
        string Path,
        string Name,
        string ElementFolder,
        string ElementKey,
        string CipherKey,
        bool HasSignature,
        bool Auxiliary,
        bool IsPdf);

    [GeneratedRegex(@"^\d+\.\d+$")]
    private static partial Regex ElementFolderName();
}
