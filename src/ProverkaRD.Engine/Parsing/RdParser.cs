using ProverkaRD.Engine;
using System.Text;
using System.Text.RegularExpressions;
using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Wordprocessing;
using UglyToad.PdfPig;
using UglyToad.PdfPig.Content;

namespace ProverkaRD.Engine.Parsing;

public static partial class RdParser
{
    private static readonly HashSet<string> SurnameStop = new(StringComparer.Ordinal)
    {
        "россия", "москва", "москвы", "документ", "документация", "версия", "лист", "листы", "листов",
        "проект", "проекта", "раздел", "раздела", "инженер", "главный", "заказчик", "подрядчик",
        "согласовано", "начальник", "подпись", "подп", "дата", "объем", "объём", "работ",
        "создание", "строительство", "комплект", "комплектов", "чертеж", "чертежей", "основной", "рабочая",
        "ведомость", "ведомости", "фасад", "наименование", "должность", "составил", "составитель", "проверил", "проверила", "инв",
        "общие", "общее", "общая", "тепловая", "тепловой", "телефон", "разработал", "разработала", "разраб",
        "данные", "спецификация", "конструкция", "конструкции", "проверка", "стадия", "изменения", "изменение",
        "формат", "масса", "масштаб", "объект", "адрес", "отопление", "вентиляция", "водоснабжение",
        "канализация", "электрика", "электроснабжение", "автоматизация", "архитектура", "конструктив",
        "пояснительная", "записка", "гип", "нормоконтроль", "отдел", "группа", "утвердил", "утвердил",
        "исполнил", "руководитель", "заказчика", "подрядчика", "котельная",
        "детали", "соединительные", "все", "рабочие", "проектируемый", "проектируемая", "проектируемых",
        "обозначение", "организация", "сооружение", "сооружения", "здание", "здания", "площадка",
        "примечание", "чертежи", "содержание", "схема", "схемы", "план", "разрез", "узел", "узлы",
        "сечение", "подлинник", "копии", "взамен",
    };

    private static readonly string[] RoleMarkers =
    [
        "разраб", "проверил", "пров.", "гип", "главный инженер", "н.контр", "н. контр", "нормоконтр", "норм.контр",
        "норм. контр", "нач.отд", "нач. отд", "начальник отдела", "начальник", "составил", "утверд", "утв.",
        "гл. спец", "гл.спец", "исполнил", "исп.", "согласован",
    ];

    public static RdDocument Parse(string path)
    {
        var draft = ReadDraft(path);
        var facts = draft.Stray ? SignatureFacts.None : ReadSignature(path);
        return Assemble(draft, facts);
    }

    internal static RdDraft ReadDraft(string path)
    {
        var errors = new List<string>();
        var notices = new List<Notice>();
        var cipherFromName = FirstCipher(Path.GetFileNameWithoutExtension(path));
        string titleText = "";
        string fullText = "";
        List<string> surnames = [];
        List<string> coordinateBare = [];
        List<string> adjectiveBare = [];
        try
        {
            var ext = Path.GetExtension(path);
            if (ext.Equals(".pdf", StringComparison.OrdinalIgnoreCase))
                (titleText, fullText, surnames, coordinateBare, adjectiveBare) = ReadPdf(path);
            else if (ext.Equals(".docx", StringComparison.OrdinalIgnoreCase))
            {
                titleText = fullText = ReadDocx(path);
                surnames = CollectSurnames([new RdPage(1, titleText, "")]);
            }
            else
                errors.Add("Формат комплекта не поддержан: " + ext);
        }
        catch (Exception ex)
        {
            errors.Add(FileProblems.Describe(path, ex));
        }

        var title = ExtractTitle(titleText);
        if (title.Cipher is null)
            title = ExtractTitle(fullText);
        return new RdDraft
        {
            FilePath = path,
            CipherFromFileName = cipherFromName,
            CipherFromText = title.Cipher,
            Cipher = title.Cipher ?? cipherFromName ?? "",
            KitName = title.KitName,
            ElementNo = title.ElementNo,
            ElementName = title.ElementName,
            Surnames = surnames,
            CoordinateBare = coordinateBare,
            AdjectiveBare = adjectiveBare,
            Errors = errors,
            Notices = notices,
            Stray = string.IsNullOrWhiteSpace(cipherFromName) && string.IsNullOrWhiteSpace(title.Cipher),
        };
    }

    internal static SignatureFacts ReadSignature(string path)
    {
        try
        {
            var located = SignatureParser.Locate(path);
            if (located is null)
                return SignatureFacts.None;

            var readSigners = located.Value.CoveringName is not null || !located.Value.ExactRejected;
            if (!readSigners)
            {
                return new SignatureFacts(
                    null,
                    false,
                    true,
                    located.Value.ExactName,
                    null,
                    [],
                    [],
                    Severity.Ok,
                    "",
                    false,
                    null);
            }

            var read = SignatureParser.Read(located.Value.Path);
            return new SignatureFacts(
                located.Value.Path,
                located.Value.NameDiffers,
                located.Value.ExactRejected,
                located.Value.ExactName,
                located.Value.CoveringName,
                DistinctSurnames(read.Surnames),
                read.People,
                read.CertStatus,
                read.CertValidityText,
                read.Error is null,
                read.Error);
        }
        catch (Exception ex)
        {
            return SignatureFacts.None with { Error = ex.Message };
        }
    }

    internal static RdDocument Assemble(RdDraft draft, SignatureFacts facts)
    {
        var surnames = ConfirmCoordinateSurnames(
            draft.Surnames,
            draft.CoordinateBare,
            draft.AdjectiveBare,
            facts.Signers,
            facts.CertificateSigners);
        return new RdDocument
        {
            FilePath = draft.FilePath,
            CipherFromFileName = draft.CipherFromFileName,
            CipherFromText = draft.CipherFromText,
            Cipher = draft.Cipher,
            KitName = draft.KitName,
            ElementNo = draft.ElementNo,
            ElementName = draft.ElementName,
            Surnames = surnames,
            SignaturePath = facts.Path,
            SignatureNameDiffers = facts.NameDiffers,
            SignatureRejected = facts.Rejected,
            RejectedSignatureName = facts.RejectedName,
            CoveringSignatureName = facts.CoveringName,
            Signers = facts.Signers,
            CertificateSigners = facts.CertificateSigners,
            CertStatus = facts.CertStatus,
            CertValidityText = facts.CertValidityText,
            SignatureParsed = facts.Parsed,
            SignatureError = facts.Error,
            Errors = draft.Errors,
            Notices = draft.Notices,
        };
    }

    private static (string Title, string Full, List<string> Surnames, List<string> CoordinateBare, List<string> AdjectiveBare) ReadPdf(string path)
    {
        using var stream = SharedRead.Stream(path);
        using var pdf = PdfDocument.Open(stream);
        var pages = new List<RdPage>();
        var title = new StringBuilder();
        var full = new StringBuilder();
        var index = 0;
        foreach (var page in pdf.GetPages())
        {
            index++;
            string plain;
            try
            {
                plain = page.Text ?? "";
            }
            catch
            {
                plain = "";
            }

            var (lines, stamp, cells) = PageLayout(page, plain);
            var text = lines.Count == 0 ? plain : string.Join("\n", lines);
            pages.Add(new RdPage(index, text, stamp, plain, cells));
            full.AppendLine(text);
            if (index <= 2)
                title.AppendLine(text);
        }

        var harvest = Harvest(pages);
        return (title.ToString(), full.ToString(), harvest.Surnames, harvest.CoordinateBare, harvest.AdjectiveBare);
    }

    private static (List<string> Lines, string Stamp, List<string> Cells) PageLayout(Page page, string fallback)
    {
        try
        {
            var words = page.GetWords()
                .Where(word => !string.IsNullOrWhiteSpace(word.Text) && IsHorizontal(word))
                .ToList();
            var rotated = ReadRotated(page.Letters);
            if (words.Count == 0 && rotated.Length == 0)
                return (SplitLines(fallback), Tail(fallback), []);

            var lines = GroupLines(words);
            var stampWords = words.Where(word => InStamp(word.BoundingBox, page.Width, page.Height)).ToList();
            var stamp = stampWords.Count == 0 ? "" : string.Join("\n", GroupLines(stampWords));
            if (rotated.Length > 0)
                stamp = stamp.Length == 0 ? rotated : stamp + "\n" + rotated;
            if (stamp.Length == 0)
                stamp = Tail(fallback);
            var cells = StampForm3.ReadCells(
                words.Select(word => new StampForm3.StampWord(
                    word.Text.Trim(),
                    word.BoundingBox.Left,
                    word.BoundingBox.Bottom,
                    word.BoundingBox.Right,
                    word.BoundingBox.Top)).ToList(),
                page.Width,
                page.Height);
            return (lines, stamp, cells);
        }
        catch
        {
            return (SplitLines(fallback), Tail(fallback), []);
        }
    }

    private static bool IsHorizontal(Word word) =>
        word.Letters.Count == 0
        || word.Letters.All(letter => letter.TextOrientation is TextOrientation.Horizontal or TextOrientation.Rotate180);

    /// <summary>
    /// Вертикальный штамп PdfPig не собирает в слова: фамилия остаётся набором букв.
    /// Рамку из отдельных повёрнутых букв в строки титула не подмешиваем.
    /// </summary>
    private static string ReadRotated(IEnumerable<Letter> letters)
    {
        var rotated = letters
            .Where(letter => letter.TextOrientation is TextOrientation.Rotate90 or TextOrientation.Rotate270)
            .Where(letter => !string.IsNullOrEmpty(letter.Value))
            .ToList();
        if (rotated.Count == 0)
            return "";

        var columns = new List<List<Letter>>();
        foreach (var letter in rotated.OrderBy(item => item.Location.X))
        {
            var column = columns.FirstOrDefault(existing => Math.Abs(existing[0].Location.X - letter.Location.X) <= 3.5);
            if (column is null)
                columns.Add([letter]);
            else
                column.Add(letter);
        }

        var lines = new List<string>();
        foreach (var column in columns)
        {
            var upward = column[0].TextOrientation == TextOrientation.Rotate270;
            var ordered = upward
                ? column.OrderBy(letter => letter.Location.Y).ToList()
                : column.OrderByDescending(letter => letter.Location.Y).ToList();
            var sb = new StringBuilder();
            double previous = double.NaN;
            foreach (var letter in ordered)
            {
                var position = letter.Location.Y;
                if (!double.IsNaN(previous) && Math.Abs(position - previous) > 8)
                    sb.Append('\n');
                sb.Append(letter.Value);
                previous = position;
            }

            foreach (var line in sb.ToString().Split('\n'))
            {
                var trimmed = line.Trim();
                if (trimmed.Length > 0)
                    lines.Add(trimmed);
            }
        }

        return string.Join("\n", lines);
    }

    private static bool InStamp(UglyToad.PdfPig.Core.PdfRectangle box, double width, double height)
    {
        var bottomBand = box.Bottom <= height * 0.36;
        var corner = box.Left >= width * 0.55 && box.Bottom <= height * 0.58;
        return bottomBand || corner;
    }

    private static List<string> GroupLines(List<UglyToad.PdfPig.Content.Word> words)
    {
        var ordered = words.OrderByDescending(word => word.BoundingBox.Bottom).ToList();
        var lines = new List<List<UglyToad.PdfPig.Content.Word>>();
        foreach (var word in ordered)
        {
            var line = lines.FirstOrDefault(existing =>
                Math.Abs(existing[0].BoundingBox.Bottom - word.BoundingBox.Bottom) <= 3.5);
            if (line is null)
                lines.Add([word]);
            else
                line.Add(word);
        }

        return lines
            .Select(line => string.Join(" ", line.OrderBy(word => word.BoundingBox.Left).Select(word => word.Text.Trim())))
            .Where(text => text.Length > 0)
            .ToList();
    }

    private static List<string> SplitLines(string text) =>
        text.Replace('\r', '\n').Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).ToList();

    private static string Tail(string text)
    {
        var lines = SplitLines(text);
        if (lines.Count == 0)
            return "";
        return string.Join("\n", lines.TakeLast(Math.Min(12, lines.Count)));
    }

    private static string ReadDocx(string path)
    {
        using var stream = SharedRead.Stream(path);
        using var doc = WordprocessingDocument.Open(stream, false);
        var body = doc.MainDocumentPart?.Document?.Body;
        if (body is null)
            return "";
        var sb = new StringBuilder();
        foreach (var paragraph in body.Descendants<Paragraph>())
        {
            var text = new StringBuilder();
            foreach (var element in paragraph.Descendants<OpenXmlElement>())
            {
                switch (element)
                {
                    case Text t:
                        text.Append(t.Text);
                        break;
                    case Break:
                        text.Append('\n');
                        break;
                }
            }
            var line = text.ToString().Trim();
            if (line.Length > 0)
                sb.AppendLine(line);
        }
        return sb.ToString();
    }

    private static TitleInfo ExtractTitle(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return new TitleInfo(null, "", "", "");

        string? cipher = null;
        var kitName = "";
        foreach (Match head in WorkingDocumentation().Matches(text))
        {
            var after = text[(head.Index + head.Length)..];
            var found = FindCipherHit(after);
            if (found is null)
                continue;
            var raw = after[..found.Value.Index];
            raw = BoilerplateRegex().Replace(raw, " ");
            var name = TextMatch.CollapseLines(raw);
            if (name.Length > kitName.Length)
            {
                kitName = name;
                cipher = found.Value.Cipher;
            }
            break;
        }

        if (cipher is not null)
            kitName = AppendTitleContinuation(text, cipher, kitName);

        var elementNo = "";
        var elementName = "";
        foreach (Match element in StageElement().Matches(text))
        {
            var number = element.Groups[1].Value;
            var name = TextMatch.CollapseLines(element.Groups[2].Value);
            name = WrappedHyphen().Replace(name, "$1-$2");
            name = TrimElementTail(name);
            if (name.Length > elementName.Length)
            {
                elementNo = number;
                elementName = name;
            }
        }

        return new TitleInfo(cipher, StripSignatory(kitName), elementNo, StripSignatory(elementName));
    }

    private static string AppendTitleContinuation(string text, string cipher, string kitName)
    {
        var lines = text.Replace('\r', '\n').Split('\n')
            .Select(line => line.Trim())
            .Where(line => line.Length > 0)
            .ToList();
        var index = lines.FindIndex(line => line.Contains(cipher, StringComparison.OrdinalIgnoreCase));
        if (index < 0)
            return kitName;

        var extra = new List<string>();
        for (var i = index + 1; i < lines.Count && extra.Count < 3; i++)
        {
            var line = lines[i];
            if (IsTitleStop(line))
                break;
            if (BoilerplateRegex().IsMatch(line))
                continue;
            var lineCipher = FindCipher(line);
            if (lineCipher is not null && line.Length <= lineCipher.Length + 4)
                continue;
            if (kitName.Contains(line, StringComparison.OrdinalIgnoreCase))
                continue;
            extra.Add(line);
        }

        return extra.Count == 0
            ? kitName
            : TextMatch.CollapseLines(kitName + " " + string.Join(" ", extra));
    }

    private static bool IsTitleStop(string line)
    {
        var normalized = TextMatch.Normalize(line);
        if (normalized.StartsWith("этап ", StringComparison.Ordinal))
            return true;
        if (normalized.Contains("главный инженер", StringComparison.Ordinal))
            return true;
        if (normalized.StartsWith("соответствует", StringComparison.Ordinal))
            return true;
        if (normalized.Contains("инв", StringComparison.Ordinal) && normalized.Contains("подл", StringComparison.Ordinal))
            return true;
        return IsRoleLine(line);
    }

    private static string TrimElementTail(string name)
    {
        name = ArchiveTail().Replace(name, "").Trim();
        name = WrappedHyphen().Replace(name, "$1-$2");
        return name.Trim(' ', '.', '-', '–');
    }

    /// <summary>С титульного наименования снимаются «И.О.Фамилия» и «Фамилия И.О.».</summary>
    private static string StripSignatory(string name)
    {
        if (string.IsNullOrWhiteSpace(name))
            return "";
        var stripped = InitialsThenSurname().Replace(name, " ");
        stripped = SurnameThenInitials().Replace(stripped, " ");
        return TextMatch.CollapseLines(stripped).Trim(' ', ',', ';', ':');
    }

    /// <param name="Raw">Текст PdfPig до сборки слов: в штампе фамилия склеена с первой буквой инициала.</param>
    public sealed record RdPage(
        int Number,
        string Text,
        string Stamp,
        string Raw = "",
        IReadOnlyList<string>? CoordinateCells = null);

    /// <summary>
    /// Фамилии тома: подписные строки двух титулов, графы штампа листов до первого ВОР
    /// включительно и роли первого листа ВОР. Листы после ВОР не читаются.
    /// «ИвановаМ» и «.В.» — фамилия без хвостовой заглавной.
    /// «Иванова К .А.» (буква инициала отдельным словом) — тоже фамилия.
    /// </summary>
    public static List<string> CollectSurnames(IReadOnlyList<RdPage> pages) =>
        Harvest(pages).Surnames;

    private readonly record struct SurnameHarvest(
        List<string> Surnames,
        List<string> CoordinateBare,
        List<string> AdjectiveBare);

    private readonly record struct CoordinateNames(
        List<string> WithInitials,
        List<string> Bare,
        List<string> Adjectives);

    /// <summary>
    /// Текст и координаты сливаются. Фамилия берётся рядом с инициалами или на строке подписи.
    /// Голая фамилия только из графы, без инициалов и без совпадения с сертификатом,
    /// в итог разбора не попадает: её отсекает ConfirmCoordinateSurnames.
    /// «Ведомости» и «Фасад» — слова штампа, не фамилии.
    /// </summary>
    private static SurnameHarvest Harvest(IReadOnlyList<RdPage> pages)
    {
        var lexicon = TitleNameWords(pages);
        var found = new List<string>();
        var bare = new List<string>();
        var adjectives = new List<string>();
        var passedVor = false;
        foreach (var page in pages)
        {
            if (passedVor)
                break;

            var cells = ReadCoordinate(page.CoordinateCells, lexicon);
            found.AddRange(cells.WithInitials);
            bare.AddRange(cells.Bare);
            adjectives.AddRange(cells.Adjectives);

            if (page.Number <= 2)
            {
                found.AddRange(SurnamesFromRoleLines(page.Text, lexicon));
                found.AddRange(SurnamesFromSignatureLines(page.Text, lexicon));
                found.AddRange(SurnamesFromAnyLine(page.Text, lexicon));
                found.AddRange(GluedSurnames(page.Text));
            }

            found.AddRange(GluedSurnames(page.Stamp));
            found.AddRange(GluedSurnames(page.Raw));

            var vor = IsVorPage(page.Text);
            if (vor)
            {
                found.AddRange(SurnamesFromRoleLines(page.Text, lexicon));
                found.AddRange(SurnamesFromRoleLines(page.Stamp, lexicon));
                found.AddRange(ProtectedStampSurnames(page.Stamp, lexicon));
                passedVor = true;
            }
            else
            {
                found.AddRange(SurnamesFromRoleLines(page.Stamp, lexicon));
                found.AddRange(ProtectedStampSurnames(page.Stamp, lexicon));
            }
        }

        var textKeys = new HashSet<string>(StringComparer.Ordinal);
        foreach (var name in found)
            textKeys.Add(TextMatch.Normalize(name));
        var bareOnly = bare.Where(name => !textKeys.Contains(TextMatch.Normalize(name))).ToList();
        found.AddRange(bareOnly);
        return new SurnameHarvest(DistinctSurnames(found), DistinctSurnames(bareOnly), DistinctSurnames(adjectives));
    }

    /// <summary>
    /// Фамилия из координатной графы без инициалов остаётся, только если она есть в сертификате .sig.
    /// Прилагательные на -ый/-ий/-ые/-ие без инициалов и без сертификата отбрасываются.
    /// Фамилия с инициалами, в том числе на -ий/-ая/-их, сохраняется.
    /// </summary>
    private static List<string> ConfirmCoordinateSurnames(
        List<string> surnames,
        IReadOnlyList<string> coordinateBare,
        IReadOnlyList<string> adjectiveBare,
        IReadOnlyList<string> signers,
        IReadOnlyList<CertificateSigner> people)
    {
        var bareKeys = new HashSet<string>(coordinateBare.Select(TextMatch.Normalize), StringComparer.Ordinal);
        var kept = new List<string>();
        foreach (var name in surnames)
        {
            if (bareKeys.Contains(TextMatch.Normalize(name)) && !MatchesSigner(name, signers, people))
                continue;
            kept.Add(name);
        }

        foreach (var name in adjectiveBare)
        {
            if (MatchesSigner(name, signers, people))
                kept.Add(name);
        }

        return DistinctSurnames(kept);
    }

    private static bool MatchesSigner(string name, IReadOnlyList<string> signers, IReadOnlyList<CertificateSigner> people)
    {
        if (signers.Any(item => SignerNames.Matches(name, item)))
            return true;
        foreach (var person in people)
        {
            if (SignerNames.Matches(name, person.Surname))
                return true;
            if (person.MatchKey.Length > 0 && SignerNames.Matches(name, person.MatchKey))
                return true;
            if (person.DisplayName.Length > 0 && SignerNames.Matches(name, person.DisplayName))
                return true;
        }

        return false;
    }

    public static List<string> ExtractSurnames(string text) =>
        DistinctSurnames(SurnamesFromRoleLines(text, null));

    /// <summary>
    /// Графа фамилии формы 3. Текст штампа по-прежнему разбирается отдельно и сливается с этим списком.
    /// </summary>
    private static CoordinateNames ReadCoordinate(IReadOnlyList<string>? cells, ISet<string> lexicon)
    {
        var withInitials = new List<string>();
        var bare = new List<string>();
        var adjectives = new List<string>();
        if (cells is null)
            return new CoordinateNames(withInitials, bare, adjectives);
        foreach (var cell in cells)
        {
            if (string.IsNullOrWhiteSpace(cell))
                continue;
            var patterned = SurnamesOnLine(cell, lexicon);
            var glued = GluedSurnames(cell);
            withInitials.AddRange(patterned);
            withInitials.AddRange(glued);
            var known = new HashSet<string>(StringComparer.Ordinal);
            foreach (var name in patterned.Concat(glued))
                known.Add(TextMatch.Normalize(name));
            foreach (var raw in cell.Split([' ', '\t', '\r', '\n'], StringSplitOptions.RemoveEmptyEntries))
            {
                var value = raw.Trim().Trim('"', '«', '»', '.', ',', ';', ':');
                if (!BareSurname().IsMatch(value) || IsRoleLine(value) || IsListedName(value, lexicon))
                    continue;
                var key = TextMatch.Normalize(value);
                if (key.Length < 3 || SurnameStop.Contains(key) || known.Contains(key))
                    continue;
                if (IsStampAdjective(key))
                    adjectives.Add(value);
                else
                    bare.Add(value);
            }
        }

        return new CoordinateNames(withInitials, bare, adjectives);
    }

    /// <summary>Обычные прилагательные и множественное число. Фамилия на -ий проходит, если рядом инициалы или она есть в сертификате.</summary>
    private static bool IsStampAdjective(string key) =>
        key.Length >= 4
        && (key.EndsWith("ые", StringComparison.Ordinal)
            || key.EndsWith("ие", StringComparison.Ordinal)
            || key.EndsWith("ый", StringComparison.Ordinal)
            || key.EndsWith("ий", StringComparison.Ordinal));

    private static bool IsVorPage(string text)
    {
        var normalized = TextMatch.Normalize(text);
        return normalized.Contains("объемов работ", StringComparison.Ordinal)
               || normalized.Contains("объем работ", StringComparison.Ordinal)
               || normalized.Contains("ведомость объемов", StringComparison.Ordinal);
    }

    private static HashSet<string> TitleNameWords(IReadOnlyList<RdPage> pages)
    {
        var words = new HashSet<string>(StringComparer.Ordinal);
        foreach (var page in pages)
        {
            if (page.Number > 2)
                continue;
            foreach (var line in SplitLines(page.Text).Concat(SplitLines(page.Stamp)))
            {
                if (IsRoleLine(line))
                    continue;
                var rest = InitialsThenSurname().Replace(line, " ");
                rest = SurnameThenInitials().Replace(rest, " ");
                foreach (var token in TextMatch.Normalize(rest).Split(' ', StringSplitOptions.RemoveEmptyEntries))
                {
                    foreach (var part in token.Split('-', StringSplitOptions.RemoveEmptyEntries))
                    {
                        if (part.Length >= 3)
                            words.Add(part);
                    }
                }
            }
        }

        return words;
    }

    private static List<string> SurnamesFromRoleLines(string text, ISet<string>? lexicon)
    {
        var found = new List<string>();
        if (string.IsNullOrWhiteSpace(text))
            return found;

        var lines = text.Replace('\r', '\n').Split('\n');
        for (var i = 0; i < lines.Length; i++)
        {
            if (!IsRoleLine(lines[i]))
                continue;
            var onLine = SurnamesOnLine(lines[i], lexicon);
            found.AddRange(onLine);
            if (onLine.Count == 0 && i + 1 < lines.Length && !IsRoleLine(lines[i + 1]))
                found.AddRange(SurnamesOnLine(lines[i + 1], lexicon));
        }

        return found;
    }

    private static List<string> SurnamesFromAnyLine(string text, ISet<string> lexicon)
    {
        var found = new List<string>();
        foreach (var line in text.Replace('\r', '\n').Split('\n'))
            found.AddRange(SurnamesOnLine(line, lexicon));
        return found;
    }

    /// <summary>
    /// В штампе без графы роли оставляем только фамилии на -ая/-ина/-их:
    /// их съедал фильтр наименований, а обычное слово вроде «Котельная» по-прежнему отсекается.
    /// </summary>
    private static List<string> ProtectedStampSurnames(string stamp, ISet<string> lexicon)
    {
        var found = new List<string>();
        if (string.IsNullOrWhiteSpace(stamp))
            return found;
        foreach (var line in stamp.Replace('\r', '\n').Split('\n'))
        {
            if (IsRoleLine(line))
                continue;
            foreach (var name in SurnamesOnLine(line, lexicon))
            {
                if (IsProtectedSurname(name))
                    found.Add(name);
            }
        }

        return found;
    }

    private static bool IsProtectedSurname(string surname)
    {
        var key = TextMatch.Normalize(surname);
        if (key.Length < 5)
            return false;
        return key.EndsWith("ая", StringComparison.Ordinal)
               || key.EndsWith("яя", StringComparison.Ordinal)
               || key.EndsWith("ина", StringComparison.Ordinal)
               || key.EndsWith("ына", StringComparison.Ordinal)
               || key.EndsWith("их", StringComparison.Ordinal)
               || key.EndsWith("ых", StringComparison.Ordinal);
    }

    /// <summary>
    /// Точное слово наименования. Фамилии на -ая/-ина/-их не отбрасываются.
    /// Родительный падеж в титуле не отменяет подпись «Фамилия И.О.».
    /// </summary>
    private static bool IsListedName(string value, ISet<string> lexicon)
    {
        var key = TextMatch.Normalize(value);
        if (IsProtectedSurname(value))
            return false;
        return lexicon.Contains(key);
    }

    /// <summary>
    /// Штамп: «ИвановаМ» сразу перед «.В.» (таб, пробел или точка между инициалами).
    /// Фамилия — слово без хвостовой заглавной. Строка роли не обязательна.
    /// </summary>
    private static List<string> GluedSurnames(string text)
    {
        var found = new List<string>();
        if (string.IsNullOrWhiteSpace(text))
            return found;
        foreach (Match match in GluedSurnameInitials().Matches(text))
            found.Add(match.Groups[1].Value);
        return found;
    }

    private static List<string> SurnamesFromSignatureLines(string text, ISet<string> lexicon)
    {
        var found = new List<string>();
        foreach (var line in text.Replace('\r', '\n').Split('\n'))
        {
            if (IsRoleLine(line) || !IsSignatureLine(line))
                continue;
            found.AddRange(SurnamesOnLine(line, lexicon));
        }

        return found;
    }

    private static bool IsSignatureLine(string line)
    {
        if (!InitialsThenSurname().IsMatch(line) && !SurnameThenInitials().IsMatch(line))
            return false;
        var rest = InitialsThenSurname().Replace(line, " ");
        rest = SurnameThenInitials().Replace(rest, " ");
        var tokens = TextMatch.Normalize(rest).Split(' ', StringSplitOptions.RemoveEmptyEntries);
        return tokens.All(IsSignatureLabel);
    }

    private static bool IsSignatureLabel(string token)
    {
        var plain = token.Replace(".", "");
        return plain is "фио" or "подпись" or "подп" or "подписи";
    }

    internal static bool IsRoleLine(string line)
    {
        var normalized = TextMatch.Normalize(line);
        foreach (var marker in RoleMarkers)
        {
            var token = TextMatch.Normalize(marker);
            if (token.Length == 0)
                continue;
            var index = normalized.IndexOf(token, StringComparison.Ordinal);
            if (index < 0)
                continue;
            var before = index == 0 || !char.IsLetter(normalized[index - 1]);
            var after = index + token.Length >= normalized.Length || !char.IsLetter(normalized[index + token.Length]);
            var prefix = token is "разраб" or "утверд" or "исполн" or "нормоконтр" or "составил" or "проверил" or "начальник" or "согласован";
            if (before && (after || prefix))
                return true;
        }
        return false;
    }

    private static List<string> SurnamesOnLine(string line, ISet<string>? lexicon)
    {
        var found = new List<string>();
        foreach (Match match in InitialsThenSurname().Matches(line))
            Take(match);
        foreach (Match match in SurnameThenInitials().Matches(line))
            Take(match);
        foreach (Match match in SurnameThenSplitInitials().Matches(line))
            Take(match);
        return found;

        void Take(Match match)
        {
            var group = match.Groups[1];
            var value = group.Value;
            var start = group.Index;
            var end = start + group.Length;
            if (start > 0 && IsNameFragment(line[start - 1]))
                return;
            if (end < line.Length && IsNameFragment(line[end]))
                return;
            if (lexicon is not null && IsListedName(value, lexicon))
                return;
            found.Add(value);
        }
    }

    private static bool IsNameFragment(char value) => value == '-' || char.IsLetter(value);

    public static List<string> DistinctSurnames(IEnumerable<string> surnames)
    {
        var seen = new HashSet<string>(StringComparer.Ordinal);
        var result = new List<string>();
        foreach (var raw in surnames)
        {
            var surname = raw.Trim().Trim('"');
            if (surname.Length < 3)
                continue;
            var key = TextMatch.Normalize(surname);
            if (key.Length < 3 || SurnameStop.Contains(key) || !seen.Add(key))
                continue;
            result.Add(surname);
        }
        result.Sort(StringComparer.Create(new System.Globalization.CultureInfo("ru-RU"), ignoreCase: true));
        return result;
    }

    public static string? FindCipher(string text) => FindCipherHit(text)?.Cipher;

    private static string? FirstCipher(string text) => FindCipher(text);

    private static CipherHit? FindCipherHit(string text)
    {
        CipherHit? best = null;
        foreach (Match match in LooseCipher().Matches(text))
        {
            var value = match.Value.Trim().TrimEnd('.', ',', ';', ':');
            if (!HasCyrillic().IsMatch(value))
                continue;
            if (value.Count(ch => ch == '-') < 3)
                continue;
            if (best is null || value.Length > best.Value.Cipher.Length)
                best = new CipherHit(match.Index, value);
        }
        return best;
    }

    private readonly record struct CipherHit(int Index, string Cipher);
    private readonly record struct TitleInfo(string? Cipher, string KitName, string ElementNo, string ElementName);

    [GeneratedRegex(@"Р\s*А\s*Б\s*О\s*Ч\s*А\s*Я\s+Д\s*О\s*К\s*У\s*М\s*Е\s*Н\s*Т\s*А\s*Ц\s*И\s*Я", RegexOptions.IgnoreCase)]
    private static partial Regex WorkingDocumentation();

    [GeneratedRegex(@"основной\s+комплект\s+(?:рабочих\s+)?чертежей", RegexOptions.IgnoreCase)]
    private static partial Regex BoilerplateRegex();

    [GeneratedRegex(@"\d{3}-\d{2}-\d{3,6}(?:-[0-9A-Za-zА-Яа-яЁё]+(?:\.[0-9A-Za-zА-Яа-яЁё]+)*)+")]
    private static partial Regex LooseCipher();

    [GeneratedRegex(@"[А-Яа-яЁё]")]
    private static partial Regex HasCyrillic();

    [GeneratedRegex(@"Этап\s+(\d+\.\d+)\s+(.+?)(?=Главный\s+инженер|Акционерное\s+общество|Инв\.\s*№|Взам\.\s*инв|$)", RegexOptions.Singleline | RegexOptions.IgnoreCase)]
    private static partial Regex StageElement();

    [GeneratedRegex(@"\d{3}-\d{2}-\d{4,}-\d+\.\d+")]
    private static partial Regex ArchiveTail();

    [GeneratedRegex(@"([\p{L}])-\s+([\p{L}])")]
    private static partial Regex WrappedHyphen();

    [GeneratedRegex(@"(?<![\p{L}])[А-ЯЁ]\.\s*[А-ЯЁ]\.\s*([А-ЯЁ][а-яё]{2,})")]
    private static partial Regex InitialsThenSurname();

    [GeneratedRegex(@"(?:ФИО\s*)?([А-ЯЁ][а-яё]{2,})\s+[А-ЯЁ]\.\s*[А-ЯЁ]\.")]
    private static partial Regex SurnameThenInitials();

    // Фамилия на строчные, сразу одна заглавная инициала, затем «.X.» с пробелами или табами.
    [GeneratedRegex(@"(?<![\p{L}\-])([А-ЯЁ][а-яё]{2,})[А-ЯЁ]\s*\.?\s*\.\s*[А-ЯЁ]\s*\.")]
    private static partial Regex GluedSurnameInitials();

    // PdfPig режет штамп на «Фамилия», отдельную букву и «.X.».
    [GeneratedRegex(@"(?<![\p{L}\-])([А-ЯЁ][а-яё]{2,})\s+[А-ЯЁ]\s+\.\s*[А-ЯЁ]\s*\.")]
    private static partial Regex SurnameThenSplitInitials();

    [GeneratedRegex(@"^[А-ЯЁ][а-яё]{2,}(?:-[А-ЯЁ][а-яё]{2,})?$")]
    private static partial Regex BareSurname();
}

internal sealed class RdDraft
{
    public required string FilePath { get; init; }
    public string? CipherFromFileName { get; init; }
    public string? CipherFromText { get; init; }
    public string Cipher { get; init; } = "";
    public string KitName { get; init; } = "";
    public string ElementNo { get; init; } = "";
    public string ElementName { get; init; } = "";
    public List<string> Surnames { get; init; } = [];
    public List<string> CoordinateBare { get; init; } = [];
    public List<string> AdjectiveBare { get; init; } = [];
    public List<string> Errors { get; init; } = [];
    public List<Notice> Notices { get; init; } = [];
    public bool Stray { get; init; }
    public string FileName => Path.GetFileName(FilePath);
}

internal sealed record SignatureFacts(
    string? Path,
    bool NameDiffers,
    bool Rejected,
    string? RejectedName,
    string? CoveringName,
    IReadOnlyList<string> Signers,
    IReadOnlyList<CertificateSigner> CertificateSigners,
    Severity CertStatus,
    string CertValidityText,
    bool Parsed,
    string? Error)
{
    public static SignatureFacts None { get; } = new(
        null, false, false, null, null, [], [], Severity.Ok, "", false, null);
}
