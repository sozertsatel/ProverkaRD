using ProverkaRD.Engine;
using System.Text;
using System.Text.RegularExpressions;
using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Wordprocessing;

namespace ProverkaRD.Engine.Parsing;

public static partial class ActParser
{
    public static ActDocument Parse(string path)
    {
        var errors = new List<string>();
        var notices = new List<Notice>();
        var kits = new List<ActKit>();
        var costs = new List<ActCostLine>();
        string? headingNo = null;
        decimal? advance = null;

        try
        {
            using var stream = SharedRead.Stream(path);
            using var doc = WordprocessingDocument.Open(stream, false);
            var body = doc.MainDocumentPart?.Document?.Body
                ?? throw new InvalidDataException("В документе нет тела.");

            var prose = string.Join("\n", body.Descendants<Paragraph>().Select(ParagraphText));
            advance = AdvancePayment.Parse(prose);
            if (advance is decimal paid)
                advance = MoneyDecimal.Kopecks(paid);
            var paragraphs = body.Elements<Paragraph>().Select(ParagraphText).ToList();
            foreach (var paragraph in paragraphs)
            {
                var match = ActHeading().Match(paragraph);
                if (match.Success)
                {
                    headingNo = match.Groups[1].Value;
                    break;
                }
            }

            var unrecognized = 0;
            foreach (var table in body.Elements<Table>())
            {
                var rows = ReadRows(table);
                if (rows.Count == 0)
                    continue;
                var flat = string.Join("\n", rows.SelectMany(r => r.Select(c => c.Text)));
                if (flat.Contains("Шифр", StringComparison.OrdinalIgnoreCase)
                    && flat.Contains("том", StringComparison.OrdinalIgnoreCase))
                {
                    kits.AddRange(ReadKits(rows, notices));
                }
                else if (flat.Contains("НДС", StringComparison.OrdinalIgnoreCase)
                         && flat.Contains("Наименование", StringComparison.OrdinalIgnoreCase))
                {
                    costs.AddRange(ReadCosts(rows, notices));
                }
                else if (flat.Any(ch => !char.IsWhiteSpace(ch)))
                {
                    unrecognized++;
                }
            }

            // Подпись и служебные таблицы акта не шумят, если комплекты и стоимость уже прочитаны.
            if ((kits.Count == 0 || costs.Count == 0) && unrecognized > 0)
            {
                notices.Add(new Notice("Акт", SafePath.FileName(path), "Таблица не разобрана и не попала ни в комплекты, ни в стоимость."));
            }
        }
        catch (Exception ex)
        {
            errors.Add(FileProblems.Describe(path, ex));
        }

        return new ActDocument
        {
            FilePath = path,
            ElementNoFromFileName = ElementFromFileName(path),
            ElementNoFromHeading = headingNo,
            Kits = kits,
            CostLines = costs,
            AdvanceNet = advance,
            Errors = errors,
            Notices = notices,
        };
    }

    public static string? ElementFromFileName(string path)
    {
        var name = Path.GetFileNameWithoutExtension(path);
        var match = FileElement().Match(name);
        return match.Success ? match.Groups[1].Value : null;
    }

    private static List<ActKit> ReadKits(List<List<Cell>> rows, List<Notice> notices)
    {
        var headerIndex = rows.FindIndex(r => r.Any(c =>
            c.Text.Contains("Шифр", StringComparison.OrdinalIgnoreCase)));
        if (headerIndex < 0)
            return [];

        var header = rows[headerIndex];
        var volumeCol = Column(header, "том", "№");
        var cipherCol = Column(header, "шифр");
        var nameCol = Column(header, "наименование");
        var paperCol = Column(header, "бумаж");
        var electronicCol = Column(header, "электрон");
        if (cipherCol < 0)
            cipherCol = 1;
        if (nameCol < 0)
            nameCol = 2;
        if (volumeCol < 0)
            volumeCol = 0;

        var kits = new List<ActKit>();
        foreach (var row in rows.Skip(headerIndex + 1))
        {
            var cipher = CellText(row, cipherCol);
            if (cipher.Length == 0 || cipher.Contains("шифр", StringComparison.OrdinalIgnoreCase))
            {
                if (row.Any(cell => !string.IsNullOrWhiteSpace(cell.Text)))
                    notices.Add(new Notice("Акт", "комплекты", "Строка без шифра пропущена."));
                continue;
            }
            var volume = CellText(row, volumeCol);
            var codeMatch = LeadingCode().Match(volume.Trim());
            var bracket = BracketNo().Match(volume);
            kits.Add(new ActKit
            {
                VolumeRaw = volume.Trim(),
                DgpCode = codeMatch.Success ? codeMatch.Groups["code"].Value.Trim() : "",
                ElementNo = bracket.Success ? bracket.Groups[1].Value : "",
                Cipher = TextMatch.StrictKey(cipher),
                Name = TextMatch.StrictKey(CellText(row, nameCol)),
                PaperCopies = paperCol >= 0 ? TextMatch.StrictKey(CellText(row, paperCol)) : "",
                ElectronicCopies = electronicCol >= 0 ? TextMatch.StrictKey(CellText(row, electronicCol)) : "",
            });
        }
        return kits;
    }

    private static List<ActCostLine> ReadCosts(List<List<Cell>> rows, List<Notice> notices)
    {
        var headerIndex = -1;
        for (var i = 0; i < rows.Count; i++)
        {
            var joined = string.Join(" ", rows[i].Select(c => c.Text));
            if (joined.Contains("НДС", StringComparison.OrdinalIgnoreCase)
                && (joined.Contains("без", StringComparison.OrdinalIgnoreCase)
                    || joined.Contains("стоим", StringComparison.OrdinalIgnoreCase)))
                headerIndex = i;
        }
        if (headerIndex < 0)
            headerIndex = rows.FindIndex(r => r.Any(c => c.Text.Contains("НДС", StringComparison.OrdinalIgnoreCase)));
        if (headerIndex < 0)
            return [];

        // Шапка часто на двух строках: подписи «без НДС» / «с НДС» могут быть выше или ниже.
        var headerCells = rows.Take(headerIndex + 1).SelectMany(r => r).ToList();
        var nameCol = Column(headerCells, "наименование");
        var unitCol = Column(headerCells, "ед");
        var qtyCol = Column(headerCells, "коли");
        var netCol = Column(headerCells, "без ндс", "безндс");
        var grossCol = Column(headerCells, "с ндс");
        var vatCol = headerCells
            .Where(c => c.Text.Contains("НДС", StringComparison.OrdinalIgnoreCase)
                        && !c.Text.Contains("без", StringComparison.OrdinalIgnoreCase)
                        && !c.Text.Contains("с НДС", StringComparison.OrdinalIgnoreCase)
                        && !c.Text.Contains("с ндс", StringComparison.OrdinalIgnoreCase))
            .Select(c => c.Col)
            .DefaultIfEmpty(-1)
            .First();
        // «Стоимость с НДС» содержит «НДС», поэтому валовую колонку ищем раньше налога.
        if (grossCol < 0)
            grossCol = headerCells.Where(c =>
                    c.Text.Contains("с НДС", StringComparison.OrdinalIgnoreCase)
                    || c.Text.Contains("с ндс", StringComparison.OrdinalIgnoreCase))
                .Select(c => c.Col).DefaultIfEmpty(-1).LastOrDefault();
        if (nameCol < 0)
            nameCol = 0;

        var lines = new List<ActCostLine>();
        foreach (var row in rows.Skip(headerIndex + 1))
        {
            var rawName = CellText(row, nameCol);
            if (rawName.Length == 0)
            {
                if (row.Any(cell => !string.IsNullOrWhiteSpace(cell.Text)))
                    notices.Add(new Notice("Акт", "стоимость", "Строка без наименования пропущена."));
                continue;
            }
            if (rawName.Contains("наименование", StringComparison.OrdinalIgnoreCase))
            {
                notices.Add(new Notice("Акт", "стоимость", "Повтор шапки пропущен."));
                continue;
            }

            var keptName = TextMatch.StrictKey(rawName);
            var isTotal = keptName.Contains("итого", StringComparison.OrdinalIgnoreCase);
            var codeMatch = LeadingCode().Match(keptName);
            var code = codeMatch.Success ? codeMatch.Groups["code"].Value.Trim() : "";
            var name = codeMatch.Success ? codeMatch.Groups["name"].Value.Trim() : keptName;
            name = name.TrimStart('.', ' ').Trim();

            decimal? net = MoneyOf(row, netCol, notices);
            decimal? vat = MoneyOf(row, vatCol, notices);
            decimal? gross = MoneyOf(row, grossCol, notices);
            if (net is null || vat is null || gross is null)
            {
                var moneys = row.Select(c => TextMatch.ParseMoney(c.Text))
                    .Where(v => v is > 50m)
                    .Select(v => v!.Value)
                    .ToList();
                if (moneys.Count >= 3)
                {
                    net ??= moneys[^3];
                    vat ??= moneys[^2];
                    gross ??= moneys[^1];
                }
                else if (moneys.Count == 2)
                {
                    net ??= moneys[0];
                    gross ??= moneys[1];
                }
                else if (moneys.Count == 1)
                {
                    net ??= moneys[0];
                }
            }

            decimal? qty = qtyCol >= 0 ? TextMatch.ParseDecimal(CellText(row, qtyCol)) : null;
            lines.Add(new ActCostLine
            {
                RawName = keptName,
                DgpCode = code,
                Name = name,
                Unit = unitCol >= 0 ? TextMatch.CollapseLines(CellText(row, unitCol)) : "",
                Quantity = qty,
                Net = net,
                Vat = vat,
                Gross = gross,
                IsTotal = isTotal,
            });
        }
        return lines;
    }

    private static decimal? MoneyOf(List<Cell> row, int col, List<Notice> notices)
    {
        if (col < 0)
            return null;
        var text = CellText(row, col);
        var parsed = TextMatch.ParseMoney(text);
        if (parsed is null && UnparsedAmount(text))
            notices.Add(new Notice("Акт", "стоимость", "Ячейка стоимости не разобрана как сумма."));
        return parsed is null ? null : MoneyDecimal.Kopecks(parsed.Value);
    }

    private static bool UnparsedAmount(string text)
    {
        var trimmed = text.Trim();
        return trimmed.Length > 0 && trimmed is not "-" and not "—" and not "–";
    }

    private static int Column(IEnumerable<Cell> header, params string[] needles)
    {
        foreach (var cell in header)
        {
            var compact = TextMatch.Normalize(cell.Text).Replace(" ", "");
            var plain = cell.Text.ToLowerInvariant();
            foreach (var needle in needles)
            {
                if (plain.Contains(needle, StringComparison.OrdinalIgnoreCase)
                    || compact.Contains(needle.Replace(" ", ""), StringComparison.Ordinal))
                    return cell.Col;
            }
        }
        return -1;
    }

    private static string CellText(List<Cell> row, int col)
    {
        if (col < 0)
            return "";
        foreach (var cell in row)
        {
            if (cell.Col == col)
                return cell.Text;
        }
        return "";
    }

    private static List<List<Cell>> ReadRows(Table table)
    {
        var rows = new List<List<Cell>>();
        foreach (var tr in table.Elements<TableRow>())
        {
            var row = new List<Cell>();
            var col = 0;
            foreach (var tc in tr.Elements<TableCell>())
            {
                var span = tc.TableCellProperties?.GridSpan?.Val?.Value ?? 1;
                row.Add(new Cell(col, ParagraphsOf(tc)));
                col += span;
            }
            rows.Add(row);
        }
        return rows;
    }

    private static string ParagraphsOf(TableCell cell)
    {
        var parts = cell.Elements<Paragraph>().Select(ParagraphText).Where(t => t.Length > 0);
        return string.Join("\n", parts);
    }

    private static string ParagraphText(Paragraph paragraph)
    {
        var sb = new StringBuilder();
        foreach (var element in paragraph.Descendants<OpenXmlElement>())
        {
            switch (element)
            {
                case Text text:
                    sb.Append(text.Text);
                    break;
                case Break:
                    sb.Append('\n');
                    break;
                case TabChar:
                    sb.Append('\t');
                    break;
            }
        }
        return TextMatch.StripInvisible(sb.ToString());
    }

    private readonly record struct Cell(int Col, string Text);

    [GeneratedRegex(@"АКТ\s*№?\s*(\d+\.\d+)", RegexOptions.IgnoreCase)]
    private static partial Regex ActHeading();

    [GeneratedRegex(@"РД\s+(\d+\.\d+)")]
    private static partial Regex FileElement();

    [GeneratedRegex(@"^(?<code>\d+(?:\.\d+)+\.?)\s*(?<name>.*)$", RegexOptions.Singleline)]
    private static partial Regex LeadingCode();

    [GeneratedRegex(@"\((\d+\.\d+)\)")]
    private static partial Regex BracketNo();
}
