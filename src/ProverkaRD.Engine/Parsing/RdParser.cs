using System.Text;
using System.Text.RegularExpressions;
using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Wordprocessing;
using UglyToad.PdfPig;

namespace ProverkaRD.Engine.Parsing;

public static partial class RdParser
{
    private static readonly HashSet<string> SurnameStop = new(StringComparer.Ordinal)
    {
        "россия", "москва", "москвы", "документ", "документация", "версия", "лист", "листы",
        "проект", "проекта", "раздел", "раздела", "инженер", "главный", "заказчик",
        "согласовано", "начальник", "подпись", "дата", "объем", "объём", "работ",
        "создание", "строительство", "комплект", "чертежей", "основной", "рабочая",
        "ведомость", "наименование", "должность", "составил", "проверил", "инв",
    };

    public static RdDocument Parse(string path)
    {
        var errors = new List<string>();
        var cipherFromName = FirstCipher(Path.GetFileNameWithoutExtension(path));
        string titleText = "";
        string fullText = "";
        try
        {
            var ext = Path.GetExtension(path);
            if (ext.Equals(".pdf", StringComparison.OrdinalIgnoreCase))
                (titleText, fullText) = ReadPdf(path);
            else if (ext.Equals(".docx", StringComparison.OrdinalIgnoreCase))
                titleText = fullText = ReadDocx(path);
            else
                errors.Add("Формат комплекта не поддержан: " + ext);
        }
        catch (Exception ex)
        {
            errors.Add("Не удалось прочитать комплект: " + ex.Message);
        }

        var title = ExtractTitle(titleText);
        if (title.Cipher is null)
            title = ExtractTitle(fullText);

        var surnames = ExtractSurnames(fullText);
        string? signaturePath = null;
        IReadOnlyList<string> signers = [];
        string? signatureError = null;
        var parsed = false;
        try
        {
            signaturePath = SignatureParser.FindSignature(path);
            if (signaturePath is not null)
            {
                var read = SignatureParser.Read(signaturePath);
                signers = DistinctSurnames(read.Surnames);
                signatureError = read.Error;
                parsed = read.Error is null;
            }
        }
        catch (Exception ex)
        {
            signatureError = ex.Message;
        }

        var cipher = title.Cipher ?? cipherFromName ?? "";
        return new RdDocument
        {
            FilePath = path,
            CipherFromFileName = cipherFromName,
            CipherFromText = title.Cipher,
            Cipher = cipher,
            KitName = title.KitName,
            ElementNo = title.ElementNo,
            ElementName = title.ElementName,
            Surnames = surnames,
            SignaturePath = signaturePath,
            Signers = signers,
            SignatureParsed = parsed,
            SignatureError = signatureError,
            Errors = errors,
        };
    }

    private static (string Title, string Full) ReadPdf(string path)
    {
        using var pdf = PdfDocument.Open(path);
        var title = new StringBuilder();
        var full = new StringBuilder();
        var index = 0;
        foreach (var page in pdf.GetPages())
        {
            index++;
            string text;
            try
            {
                text = page.Text ?? "";
            }
            catch
            {
                text = "";
            }
            full.AppendLine(text);
            if (index <= 2)
                title.AppendLine(text);
        }
        return (title.ToString(), full.ToString());
    }

    private static string ReadDocx(string path)
    {
        using var doc = WordprocessingDocument.Open(path, false);
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
            var found = FindCipher(after);
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

        var elementNo = "";
        var elementName = "";
        foreach (Match element in StageElement().Matches(text))
        {
            var number = element.Groups[1].Value;
            var name = TextMatch.CollapseLines(element.Groups[2].Value);
            name = TrimElementTail(name);
            if (name.Length > elementName.Length)
            {
                elementNo = number;
                elementName = name;
            }
        }

        return new TitleInfo(cipher, kitName, elementNo, elementName);
    }

    private static string TrimElementTail(string name)
    {
        name = ArchiveTail().Replace(name, "").Trim();
        return name.Trim(' ', '.', '-', '–');
    }

    private static List<string> ExtractSurnames(string text)
    {
        var found = new List<string>();
        if (string.IsNullOrWhiteSpace(text))
            return found;

        foreach (Match match in InitialsThenSurname().Matches(text))
            found.Add(match.Groups[1].Value);
        foreach (Match match in SurnameThenInitials().Matches(text))
            found.Add(match.Groups[1].Value);
        return DistinctSurnames(found);
    }

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

    private static string? FirstCipher(string text)
    {
        var found = FindCipher(text);
        return found?.Cipher;
    }

    private static CipherHit? FindCipher(string text)
    {
        foreach (Match match in LooseCipher().Matches(text))
        {
            var value = match.Value;
            if (!HasCyrillic().IsMatch(value))
                continue;
            if (value.Count(ch => ch == '-') < 3)
                continue;
            return new CipherHit(match.Index, value);
        }
        return null;
    }

    private readonly record struct CipherHit(int Index, string Cipher);
    private readonly record struct TitleInfo(string? Cipher, string KitName, string ElementNo, string ElementName);

    [GeneratedRegex(@"Р\s*А\s*Б\s*О\s*Ч\s*А\s*Я\s+Д\s*О\s*К\s*У\s*М\s*Е\s*Н\s*Т\s*А\s*Ц\s*И\s*Я", RegexOptions.IgnoreCase)]
    private static partial Regex WorkingDocumentation();

    [GeneratedRegex(@"основной\s+комплект\s+(?:рабочих\s+)?чертежей", RegexOptions.IgnoreCase)]
    private static partial Regex BoilerplateRegex();

    [GeneratedRegex(@"\d{3}-\d{2}-\d{3,6}(?:-[0-9A-Za-zА-Яа-яЁё]+)+")]
    private static partial Regex LooseCipher();

    [GeneratedRegex(@"[А-Яа-яЁё]")]
    private static partial Regex HasCyrillic();

    [GeneratedRegex(@"Этап\s+(\d+\.\d+)\s+(.+?)(?=Главный\s+инженер|Акционерное\s+общество|Инв\.\s*№|Взам\.\s*инв|$)", RegexOptions.Singleline | RegexOptions.IgnoreCase)]
    private static partial Regex StageElement();

    [GeneratedRegex(@"\d{3}-\d{2}-\d{4,}-\d+\.\d+")]
    private static partial Regex ArchiveTail();

    [GeneratedRegex(@"(?<![\p{L}])[А-ЯЁ]\.\s*[А-ЯЁ]\.\s*([А-ЯЁ][а-яё]{2,})")]
    private static partial Regex InitialsThenSurname();

    [GeneratedRegex(@"(?:ФИО\s*)?([А-ЯЁ][а-яё]{2,})\s+[А-ЯЁ]\.\s*[А-ЯЁ]\.")]
    private static partial Regex SurnameThenInitials();
}
