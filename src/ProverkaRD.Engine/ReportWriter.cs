using ClosedXML.Excel;

namespace ProverkaRD.Engine;

public static class ReportWriter
{
    public static void Write(CheckReport report, string path, string? appVersion = null)
    {
        using var workbook = new XLWorkbook();
        WriteSummary(workbook, report, appVersion);
        WriteKits(workbook, report, appVersion);
        WriteFindings(workbook, report, appVersion);
        var directory = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(directory))
            Directory.CreateDirectory(directory);
        workbook.SaveAs(path);
    }

    private static void WriteSummary(XLWorkbook workbook, CheckReport report, string? appVersion)
    {
        var sheet = workbook.AddWorksheet("Сводка");
        var headers = new[]
        {
            "Элемент", "Итог", "Критично", "Оформление", "Предупреждения", "Комплектность", "Акт",
        };
        sheet.Cell(1, 1).Value = Titled("Проверка рабочей документации", appVersion);
        sheet.Range(1, 1, 1, headers.Length).Merge();
        sheet.Cell(1, 1).Style.Font.Bold = true;
        sheet.Cell(1, 1).Style.Font.FontSize = 16;

        var rows = report.Acts
            .Select(act => (Act: act, Score: ActScore.Score(act, report.Findings, report.Kits)))
            .ToList();
        var critical = rows.Sum(row => row.Score.Critical);
        var formatting = rows.Sum(row => row.Score.Formatting);
        var warnings = rows.Sum(row => row.Score.Warnings);
        sheet.Cell(2, 1).Value =
            "ВЭРД: " + (report.VerdFileName ?? "не загружена")
            + "    ВЭСР: " + (report.VesrFileName ?? "не загружена")
            + "    Актов: " + report.Acts.Count
            + "    Критично: " + critical
            + "    Оформление: " + formatting
            + "    Предупреждения: " + warnings;
        sheet.Range(2, 1, 2, headers.Length).Merge();
        sheet.Cell(3, 1).Value = "Красный — критично. Жёлтый — ошибка оформления. Оранжевый — предупреждение. Зелёный — совпадает. Итог акта — худший статус замечаний, комплектов, комплектности и сумм.";
        sheet.Range(3, 1, 3, headers.Length).Merge();
        sheet.Cell(3, 1).Style.Alignment.WrapText = true;

        const int headerRow = 5;
        for (var i = 0; i < headers.Length; i++)
            sheet.Cell(headerRow, i + 1).Value = headers[i];
        StyleHeader(sheet.Range(headerRow, 1, headerRow, headers.Length));

        var row = headerRow + 1;
        foreach (var (act, score) in rows)
        {
            var overall = SeverityText.Worst(score.Overall, act.Overall);
            sheet.Cell(row, 1).Value = act.ElementNo;
            sheet.Cell(row, 3).Value = score.Critical;
            sheet.Cell(row, 4).Value = score.Formatting;
            sheet.Cell(row, 5).Value = score.Warnings;
            sheet.Cell(row, 6).Value = act.CompletenessText;
            Paint(sheet.Cell(row, 6), act.CompletenessStatus);
            sheet.Cell(row, 6).Value = act.CompletenessText;
            sheet.Cell(row, 7).Value = act.FileName;
            Status(sheet.Cell(row, 2), overall);
            row++;
        }

        var totalRow = row;
        sheet.Cell(totalRow, 1).Value = "Итого";
        sheet.Cell(totalRow, 3).Value = critical;
        sheet.Cell(totalRow, 4).Value = formatting;
        sheet.Cell(totalRow, 5).Value = warnings;
        sheet.Range(totalRow, 1, totalRow, headers.Length).Style.Font.Bold = true;
        sheet.Range(totalRow, 1, totalRow, headers.Length).Style.Fill.BackgroundColor = XLColor.FromHtml("#D6DCE4");

        sheet.SheetView.FreezeRows(headerRow);
        if (report.Acts.Count > 0)
            sheet.Range(headerRow, 1, totalRow - 1, headers.Length).SetAutoFilter();
        Widths(sheet, headers.Length);
        sheet.Column(6).Width = 56;
        sheet.Column(7).Width = 36;
        if (totalRow > headerRow)
            sheet.Rows(headerRow + 1, totalRow).Style.Alignment.WrapText = true;
        for (var i = 0; i < rows.Count; i++)
        {
            var overall = SeverityText.Worst(rows[i].Score.Overall, rows[i].Act.Overall);
            Status(sheet.Cell(headerRow + 1 + i, 2), overall);
        }
    }

    private static void WriteKits(XLWorkbook workbook, CheckReport report, string? appVersion)
    {
        var sheet = workbook.AddWorksheet("Комплекты");
        const int columns = 21;
        sheet.Cell(1, 1).Value = Titled("Комплекты рабочей документации", appVersion);
        sheet.Cell(1, 1).Style.Font.Bold = true;
        sheet.Cell(1, 1).Style.Font.FontSize = 16;
        sheet.Range(1, 1, 1, columns).Merge();
        sheet.Cell(2, 1).Value = "Строка — шифр. Сверка идёт по блокам слева направо. Экземпляры не выводятся. Погашение аванса — сумма из акта, без сверки.";
        sheet.Range(2, 1, 2, columns).Merge();
        sheet.Cell(2, 1).Style.Alignment.WrapText = true;
        sheet.Row(2).Height = 28;

        const int groupRow = 4;
        const int headerRow = 5;
        Group(sheet, groupRow, 1, 3, "Акт", "#334155");
        Group(sheet, groupRow, 4, 7, "1. Шифр", "#1F4E79");
        Group(sheet, groupRow, 8, 11, "2. Наименование комплекта", "#215C3A");
        Group(sheet, groupRow, 12, 15, "3. Наименование элемента", "#6B3F1D");
        Group(sheet, groupRow, 16, 17, "4. Файлы", "#5C3B6E");
        Group(sheet, groupRow, 18, 20, "5. Подписи", "#8C3A3A");
        Group(sheet, groupRow, 21, 21, "6. Итог", "#1F4E79");

        var headers = new[]
        {
            "Элемент", "Акт", "Погашение аванса без НДС",
            "В акте", "В ВЭРД", "На титуле РД", "Статус",
            "В акте", "В ВЭРД", "В РД", "Статус",
            "В акте", "В ВЭРД", "В РД", "Статус",
            "PDF", ".sig",
            "Фамилии из тома", "Нет в ЭЦП", "Статус",
            "Итог",
        };
        for (var i = 0; i < headers.Length; i++)
            sheet.Cell(headerRow, i + 1).Value = headers[i];
        StyleHeader(sheet.Range(headerRow, 1, headerRow, headers.Length));

        var row = headerRow + 1;
        foreach (var kit in report.Kits
                     .OrderBy(item => item.ElementNo, Comparer<string>.Create(ElementOrder.Compare))
                     .ThenBy(item => item.ActNo, Comparer<string>.Create(ElementOrder.Compare))
                     .ThenBy(item => item.Cipher, StringComparer.CurrentCultureIgnoreCase))
        {
            var col = 1;
            sheet.Cell(row, col++).Value = kit.ElementNo;
            sheet.Cell(row, col++).Value = kit.ActNo.Length > 0 ? kit.ActNo : "—";
            WriteAdvance(sheet.Cell(row, col++), kit.AdvanceNet);
            sheet.Cell(row, col++).Value = kit.ActCipher;
            sheet.Cell(row, col++).Value = kit.VerdCipher;
            sheet.Cell(row, col++).Value = kit.TitleCipher;
            Status(sheet.Cell(row, col++), kit.CipherStatus);
            sheet.Cell(row, col++).Value = kit.ActKitName;
            sheet.Cell(row, col++).Value = kit.VerdKitName;
            sheet.Cell(row, col++).Value = kit.RdKitName;
            Status(sheet.Cell(row, col++), kit.NameStatus);
            sheet.Cell(row, col++).Value = kit.ActElementName;
            sheet.Cell(row, col++).Value = kit.VerdElementName;
            sheet.Cell(row, col++).Value = kit.RdElementName;
            Status(sheet.Cell(row, col++), kit.ElementNameStatus);
            YesNo(sheet.Cell(row, col++), kit.PdfState, Severity.Warning);
            YesNo(sheet.Cell(row, col++), kit.SigState, Severity.Critical);
            sheet.Cell(row, col++).Value = kit.Surnames;
            sheet.Cell(row, col++).Value = kit.MissingSigners;
            Status(sheet.Cell(row, col++), kit.SurnameStatus);
            Status(sheet.Cell(row, col), kit.Overall);
            row++;
        }

        sheet.SheetView.FreezeRows(headerRow);
        if (report.Kits.Count > 0)
            sheet.Range(headerRow, 1, row - 1, columns).SetAutoFilter();
        Widths(sheet, columns);
        sheet.Column(3).Width = 22;
        foreach (var wide in new[] { 6, 8, 9, 10, 12, 13, 14, 18, 19 })
            sheet.Column(wide).Width = 36;
        if (row > headerRow + 1)
        {
            sheet.Rows(headerRow + 1, row - 1).Style.Alignment.WrapText = true;
            for (var r = headerRow + 1; r < row; r++)
            {
                var advance = sheet.Cell(r, 3);
                if (advance.DataType == XLDataType.Number)
                    advance.Style.NumberFormat.Format = "0,00";
            }
        }
    }

    private static void WriteFindings(XLWorkbook workbook, CheckReport report, string? appVersion)
    {
        var sheet = workbook.AddWorksheet("Замечания");
        var headers = new[]
        {
            "№", "Элемент", "Критичность", "Что проверялось",
            "В акте", "В РД", "В ВЭРД", "В ВЭСР", "Где",
        };
        sheet.Cell(1, 1).Value = Titled("Замечания", appVersion);
        sheet.Cell(1, 1).Style.Font.Bold = true;
        sheet.Cell(1, 1).Style.Font.FontSize = 16;
        sheet.Range(1, 1, 1, headers.Length).Merge();
        sheet.Cell(2, 1).Value = "Одно поле — одна строка: значения из акта, РД, ВЭРД и ВЭСР рядом. Отличающееся значение выделено. В столбце «Где» только шифры.";
        sheet.Range(2, 1, 2, headers.Length).Merge();

        const int headerRow = 4;
        for (var i = 0; i < headers.Length; i++)
            sheet.Cell(headerRow, i + 1).Value = headers[i];
        StyleHeader(sheet.Range(headerRow, 1, headerRow, headers.Length));

        var row = headerRow + 1;
        var number = 1;
        foreach (var finding in report.Findings)
        {
            var values = new[] { finding.InAct, finding.InRd, finding.InVerd, finding.InVesr };
            sheet.Cell(row, 1).Value = number++;
            sheet.Cell(row, 2).Value = finding.ElementNo;
            Status(sheet.Cell(row, 3), finding.Severity);
            sheet.Cell(row, 4).Value = finding.CheckName;
            for (var i = 0; i < values.Length; i++)
                WriteCompared(sheet.Cell(row, 5 + i), values[i], values);
            sheet.Cell(row, 9).Value = finding.Places;
            row++;
        }

        sheet.SheetView.FreezeRows(headerRow);
        if (report.Findings.Count > 0)
            sheet.Range(headerRow, 1, row - 1, headers.Length).SetAutoFilter();
        Widths(sheet, headers.Length);
        sheet.Column(1).Width = 6;
        sheet.Column(4).Width = 32;
        sheet.Column(5).Width = 36;
        sheet.Column(6).Width = 36;
        sheet.Column(7).Width = 36;
        sheet.Column(8).Width = 36;
        sheet.Column(9).Width = 42;
        if (row > headerRow + 1)
            sheet.Rows(headerRow + 1, row - 1).Style.Alignment.WrapText = true;
    }

    private static string Titled(string title, string? version) =>
        string.IsNullOrWhiteSpace(version) ? title : title + ", версия " + version.Trim();

    private static void Group(IXLWorksheet sheet, int row, int from, int to, string title, string color)
    {
        var range = sheet.Range(row, from, row, to);
        range.Merge();
        range.Value = title;
        range.Style.Font.Bold = true;
        range.Style.Font.FontColor = XLColor.White;
        range.Style.Fill.BackgroundColor = XLColor.FromHtml(color);
        range.Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;
        range.Style.Alignment.Vertical = XLAlignmentVerticalValues.Center;
        sheet.Row(row).Height = 22;
    }

    private static void StyleHeader(IXLRange range)
    {
        range.Style.Font.Bold = true;
        range.Style.Font.FontColor = XLColor.White;
        range.Style.Fill.BackgroundColor = XLColor.FromHtml("#1F4E79");
        range.Style.Alignment.WrapText = true;
        range.Style.Alignment.Vertical = XLAlignmentVerticalValues.Center;
        range.Worksheet.Row(range.FirstRow().RowNumber()).Height = 32;
    }

    private static void Status(IXLCell cell, Severity severity)
    {
        cell.Value = SeverityText.ToRu(severity);
        Paint(cell, severity);
        cell.Style.Font.Bold = severity != Severity.Ok;
    }

    private static void Paint(IXLCell cell, Severity severity)
    {
        cell.Style.Fill.BackgroundColor = severity switch
        {
            Severity.Critical => XLColor.FromHtml("#F4C7C3"),
            Severity.Formatting => XLColor.FromHtml("#FFE08A"),
            Severity.Warning => XLColor.FromHtml("#F8CBAD"),
            _ => XLColor.FromHtml("#C6EFCE"),
        };
    }

    private static void YesNo(IXLCell cell, string text, Severity whenMissing)
    {
        cell.Value = text;
        if (text == "нет")
            Paint(cell, whenMissing);
        else if (text == "есть")
            Paint(cell, Severity.Ok);
    }

    private static void WriteAdvance(IXLCell cell, decimal? value)
    {
        if (value is null)
        {
            cell.Value = "нет";
            return;
        }

        cell.Value = value.Value;
        cell.Style.NumberFormat.Format = "0,00";
    }

    private static void WriteCompared(IXLCell cell, string text, IReadOnlyList<string> peers)
    {
        WriteValue(cell, text);
        if (!Differs(text, peers))
            return;
        cell.Style.Font.FontColor = XLColor.FromHtml("#7F1D1D");
        cell.Style.Font.Bold = true;
        cell.Style.Fill.BackgroundColor = XLColor.FromHtml("#F4C7C3");
    }

    private static bool Differs(string text, IReadOnlyList<string> peers)
    {
        if (string.IsNullOrWhiteSpace(text))
            return false;
        var present = peers.Where(value => !string.IsNullOrWhiteSpace(value)).ToList();
        if (present.Count < 2)
            return false;
        var keys = present.Select(TextMatch.VisualKey).ToList();
        var largest = keys.GroupBy(key => key).Max(group => group.Count());
        if (largest == keys.Count)
        {
            return present.Select(value => value.Trim()).Distinct(StringComparer.Ordinal).Count() > 1;
        }

        if (largest == 1)
            return true;
        var majority = keys.GroupBy(key => key).OrderByDescending(group => group.Count()).First().Key;
        return TextMatch.VisualKey(text) != majority;
    }

    private static void WriteValue(IXLCell cell, string text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return;
        var money = TextMatch.ParseMoney(text);
        if (money is not null && LooksLikeMoney(text))
        {
            cell.Value = money.Value;
            cell.Style.NumberFormat.Format = "#,##0.00";
            return;
        }
        cell.Value = text;
    }

    private static bool LooksLikeMoney(string text)
    {
        var digits = text.Count(char.IsDigit);
        if (digits < 3)
            return false;
        if (text.Any(ch => char.IsLetter(ch)))
            return false;
        return text.Contains(',') || text.Contains(' ') || digits >= 4;
    }

    private static void Widths(IXLWorksheet sheet, int columns)
    {
        for (var i = 1; i <= columns; i++)
        {
            if (sheet.Column(i).Width < 16)
                sheet.Column(i).Width = 18;
        }
        sheet.SheetView.ZoomScale = 110;
    }
}
