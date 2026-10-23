using System.Text.RegularExpressions;
using ProverkaRD.Engine;
using ClosedXML.Excel;

namespace ProverkaRD.Engine.Parsing;

public static partial class VerdParser
{
    public static VerdBook Parse(string path)
    {
        var book = new VerdBook();
        try
        {
            using var stream = SharedRead.Stream(path);
            using var wb = new XLWorkbook(stream);
            var named = wb.Worksheets.FirstOrDefault(w =>
                w.Name.Contains("База", StringComparison.OrdinalIgnoreCase));
            if (named is null)
                book.Notices.Add(new Notice("ВЭРД", SafePath.FileName(path),
                    "Лист «База» не найден. Читается лист «" + wb.Worksheets.First().Name + "»."));
            var sheet = named ?? wb.Worksheets.First();
            var header = FindHeader(sheet);
            if (header is null)
            {
                book.Errors.Add("В ВЭРД не найдена строка заголовков (ожидались «Номер элемента» и «Шифр РД»).");
                return book;
            }

            var (row, cols) = header.Value;
            VerdElement? current = null;
            var last = sheet.LastRowUsed()?.RowNumber() ?? row;
            for (var r = row + 1; r <= last; r++)
            {
                var number = ElementNumber(sheet, r, cols.Number);
                var name = Text(sheet, r, cols.Name);
                var cipher = Text(sheet, r, cols.Cipher);
                var kitName = Text(sheet, r, cols.KitName);
                var dgp = Text(sheet, r, cols.Dgp);
                var pd = Text(sheet, r, cols.Pd);

                if (IndexRow().IsMatch(number) && IndexRow().IsMatch(name))
                {
                    book.Notices.Add(new Notice("ВЭРД", "строка " + r, "Пропущена строка-номер " + number + "."));
                    continue;
                }

                var startedElement = false;
                if (ElementNo().IsMatch(number.Trim()))
                {
                    current = new VerdElement
                    {
                        Number = number.Trim(),
                        Name = name,
                        DgpCode = dgp.Trim(),
                    };
                    book.Elements.Add(current);
                    startedElement = true;
                }

                if (current is not null && LooksLikeCipher(cipher))
                {
                    current.Kits.Add(new VerdKit
                    {
                        Cipher = TextMatch.CollapseLines(cipher),
                        Name = kitName,
                        PdCipher = TextMatch.CollapseLines(pd),
                    });
                    if (current.DgpCode.Length == 0 && dgp.Length > 0)
                        current.DgpCode = dgp.Trim();
                    continue;
                }

                if (!startedElement && HasText(number, name, cipher, kitName, dgp, pd))
                    book.Notices.Add(new Notice("ВЭРД", "строка " + r, RowSkip.Reason(number, name, cipher, kitName, dgp, pd)));
            }
        }
        catch (Exception ex)
        {
            book.Errors.Add(FileProblems.Describe(path, ex));
        }
        return book;
    }

    private static (int Row, Columns Cols)? FindHeader(IXLWorksheet sheet)
    {
        var last = Math.Min(sheet.LastRowUsed()?.RowNumber() ?? 1, 30);
        var maxCol = Math.Min(sheet.LastColumnUsed()?.ColumnNumber() ?? 1, 20);
        for (var r = 1; r <= last; r++)
        {
            var map = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            for (var c = 1; c <= maxCol; c++)
            {
                var text = Text(sheet, r, c);
                if (text.Length > 0)
                    map[text] = c;
            }
            var joined = string.Join(" | ", map.Keys);
            if (!joined.Contains("Шифр", StringComparison.OrdinalIgnoreCase))
                continue;
            if (!joined.Contains("элемент", StringComparison.OrdinalIgnoreCase)
                && !joined.Contains("Наименование", StringComparison.OrdinalIgnoreCase))
                continue;

            int Col(params string[] parts)
            {
                foreach (var (key, index) in map)
                {
                    if (parts.All(p => key.Contains(p, StringComparison.OrdinalIgnoreCase)))
                        return index;
                }
                return -1;
            }

            var cols = new Columns(
                Col("Номер", "элемент") is var n && n > 0 ? n : 1,
                Col("Наименование", "элемент") is var en && en > 0 ? en : 2,
                Col("Шифр", "ПД") is var pd && pd > 0 ? pd : 3,
                Col("Шифр", "РД") is var rd && rd > 0 ? rd : 4,
                Col("Наименование", "документац") is var kn && kn > 0 ? kn : 5,
                Col("ДГП") is var g && g > 0 ? g : 7);
            return (r, cols);
        }
        return null;
    }

    private static string ElementNumber(IXLWorksheet sheet, int row, int col)
    {
        if (col <= 0)
            return "";
        var cell = sheet.Cell(row, col);
        if (cell.IsEmpty())
            return "";
        if (cell.DataType == XLDataType.Number)
            return MoneyDecimal.ExcelNumberText(cell.GetDouble());

        var text = Text(sheet, row, col);
        return CommaElement().IsMatch(text) ? text.Replace(',', '.') : text;
    }

    private static string Text(IXLWorksheet sheet, int row, int col)
    {
        if (col <= 0)
            return "";
        var cell = sheet.Cell(row, col);
        if (cell.IsEmpty())
            return "";
        return TextMatch.StripInvisible(cell.GetFormattedString()).Trim();
    }

    private static bool LooksLikeCipher(string value) =>
        value.Contains('-') && CipherHint().IsMatch(value);

    private static bool HasText(params string[] values) =>
        values.Any(value => !string.IsNullOrWhiteSpace(value));

    private readonly record struct Columns(int Number, int Name, int Pd, int Cipher, int KitName, int Dgp);

    [GeneratedRegex(@"^\d+\.\d+$")]
    private static partial Regex ElementNo();

    [GeneratedRegex(@"^\d+,\d+$")]
    private static partial Regex CommaElement();

    [GeneratedRegex(@"^\d+$")]
    private static partial Regex IndexRow();

    [GeneratedRegex(@"\d{2,}-\d{2}-")]
    private static partial Regex CipherHint();
}

public static partial class VesrParser
{
    public static VesrBook Parse(string path)
    {
        var book = new VesrBook();
        try
        {
            using var stream = SharedRead.Stream(path);
            using var wb = new XLWorkbook(stream);
            var named = wb.Worksheets.FirstOrDefault(w =>
                w.Name.Trim().StartsWith("ВЭ", StringComparison.OrdinalIgnoreCase));
            if (named is null)
                book.Notices.Add(new Notice("ВЭСР", SafePath.FileName(path),
                    "Лист «ВЭ» не найден. Читается лист «" + wb.Worksheets.First().Name + "»."));
            var sheet = named ?? wb.Worksheets.First();
            var header = FindHeader(sheet);
            if (header is null)
            {
                book.Errors.Add("В ВЭСР не найдена строка заголовков (ожидались «№ Элемента» и «Наименование»).");
                return book;
            }

            var (row, cols) = header.Value;
            var last = sheet.LastRowUsed()?.RowNumber() ?? row;
            for (var r = row + 1; r <= last; r++)
            {
                var code = Text(sheet, r, cols.Code);
                var name = Text(sheet, r, cols.Name);
                var unitText = Text(sheet, r, cols.Unit);
                var qtyText = cols.Qty > 0 ? Text(sheet, r, cols.Qty) : "";
                var priceText = cols.Price > 0 ? Text(sheet, r, cols.Price) : "";
                var totalText = cols.Total > 0 ? Text(sheet, r, cols.Total) : "";
                if (code.Length == 0 && name.Length == 0 && unitText.Length == 0
                    && qtyText.Length == 0 && priceText.Length == 0 && totalText.Length == 0)
                    continue;
                if (code.Length == 0 && name.Length == 0)
                {
                    book.Notices.Add(new Notice("ВЭСР", "строка " + r, RowSkip.Reason(code, name, unitText, qtyText, priceText, totalText)));
                    continue;
                }
                if (code.Contains("элемент", StringComparison.OrdinalIgnoreCase))
                {
                    book.Notices.Add(new Notice("ВЭСР", "строка " + r, "Повтор шапки пропущен."));
                    continue;
                }
                if (IndexRow().IsMatch(code) && IndexRow().IsMatch(name))
                {
                    book.Notices.Add(new Notice("ВЭСР", "строка " + r, "Пропущена строка-номер " + code + "."));
                    continue;
                }
                if (!StartsWithDigit().IsMatch(code))
                {
                    book.Notices.Add(new Notice("ВЭСР", "строка " + r, RowSkip.Reason(code, name, unitText, qtyText, priceText, totalText)));
                    continue;
                }

                var rowData = new VesrRow
                {
                    Code = code.Trim(),
                    Name = name,
                    Unit = Text(sheet, r, cols.Unit),
                    Quantity = cols.Qty > 0 ? MoneyDecimal.ReadQuantity(sheet.Cell(r, cols.Qty)) : null,
                    UnitPrice = ReadMoneyCell(book, sheet, r, cols.Price),
                    TotalNet = ReadMoneyCell(book, sheet, r, cols.Total),
                };
                var key = TextMatch.CodeKey(rowData.Code);
                if (key.Length == 0)
                {
                    book.Notices.Add(new Notice("ВЭСР", "строка " + r, "Код элемента пустой после нормализации."));
                    continue;
                }
                if (book.ByCode.ContainsKey(key))
                {
                    book.Notices.Add(new Notice("ВЭСР", "строка " + r, "Повтор кода " + code.Trim() + " пропущен, оставлена первая строка."));
                    continue;
                }
                book.ByCode[key] = rowData;
            }
        }
        catch (Exception ex)
        {
            book.Errors.Add(FileProblems.Describe(path, ex));
        }
        return book;
    }

    private static (int Row, Columns Cols)? FindHeader(IXLWorksheet sheet)
    {
        var last = Math.Min(sheet.LastRowUsed()?.RowNumber() ?? 1, 40);
        var maxCol = Math.Min(sheet.LastColumnUsed()?.ColumnNumber() ?? 1, 12);
        for (var r = 1; r <= last; r++)
        {
            string Cell(int c) => Text(sheet, r, c);
            var joined = string.Join(" | ", Enumerable.Range(1, maxCol).Select(Cell));
            if (!joined.Contains("Наименование", StringComparison.OrdinalIgnoreCase))
                continue;
            if (!joined.Contains("Элемент", StringComparison.OrdinalIgnoreCase)
                && !joined.Contains("конструктив", StringComparison.OrdinalIgnoreCase))
                continue;

            var totalCol = 6;
            var unitCol = 3;
            var qtyCol = 4;
            var priceCol = 5;
            for (var look = r; look <= Math.Min(r + 2, last); look++)
            {
                for (var c = 1; c <= maxCol; c++)
                {
                    var text = Text(sheet, look, c);
                    if (text.Contains("Всего", StringComparison.OrdinalIgnoreCase))
                        totalCol = c;
                    else if (text.Contains("Единица", StringComparison.OrdinalIgnoreCase))
                        unitCol = c;
                    else if (text.Contains("Количество", StringComparison.OrdinalIgnoreCase) && !text.Contains("Всего", StringComparison.OrdinalIgnoreCase))
                        qtyCol = c;
                    else if (text.Contains("единицу", StringComparison.OrdinalIgnoreCase))
                        priceCol = c;
                }
            }

            return (r, new Columns(1, 2, unitCol, qtyCol, priceCol, totalCol));
        }
        return null;
    }

    private static string Text(IXLWorksheet sheet, int row, int col)
    {
        if (col <= 0)
            return "";
        var cell = sheet.Cell(row, col);
        if (cell.IsEmpty())
            return "";
        return TextMatch.StripInvisible(cell.GetFormattedString()).Replace('\u00A0', ' ').Trim();
    }

    private static decimal? ReadMoneyCell(VesrBook book, IXLWorksheet sheet, int row, int col)
    {
        if (col <= 0)
            return null;
        var cell = sheet.Cell(row, col);
        if (cell.IsEmpty())
            return null;
        try
        {
            var money = MoneyDecimal.ReadMoney(cell);
            if (money is null)
                book.Notices.Add(new Notice("ВЭСР", cell.Address.ToString() ?? "", "Ячейка не разобрана как сумма."));
            return money;
        }
        catch (Exception ex)
        {
            book.Notices.Add(new Notice("ВЭСР", cell.Address.ToString() ?? "", "Ячейка не разобрана как сумма: " + ex.Message));
            return null;
        }
    }

    private readonly record struct Columns(int Code, int Name, int Unit, int Qty, int Price, int Total);

    [GeneratedRegex(@"^\d+$")]
    private static partial Regex IndexRow();

    [GeneratedRegex(@"^\d")]
    private static partial Regex StartsWithDigit();
}

internal static class RowSkip
{
    internal static string Reason(params string[] cells)
    {
        var parts = cells
            .Select(cell => (cell ?? "").Trim())
            .Where(cell => cell.Length > 0 && !IsBlank(cell))
            .ToList();
        if (parts.Count == 0)
            return "Пропущена пустая строка.";
        var folded = TextMatch.Normalize(string.Join(' ', parts));
        if (folded.Contains("итого", StringComparison.Ordinal)
            || folded.Contains("всего", StringComparison.Ordinal)
            || folded.Contains("промежуточ", StringComparison.Ordinal))
            return "Пропущена строка-итог.";
        if (parts.Any(part => part.Any(char.IsLetter)))
            return "Пропущена строка: примечание или текст.";
        return "Пропущена строка: нечисловое значение.";
    }

    private static bool IsBlank(string value)
    {
        var trimmed = value.Trim();
        return trimmed is "-" or "—" or "–" or ".";
    }
}
