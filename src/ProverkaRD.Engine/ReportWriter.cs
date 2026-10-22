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

        var critical = report.Acts.Sum(a => a.CriticalCount);
        var formatting = report.Acts.Sum(a => a.FormattingCount);
        var warnings = report.Acts.Sum(a => a.WarningCount);
        sheet.Cell(2, 1).Value =
            "ВЭРД: " + (report.VerdFileName ?? "не загружена")
            + "    ВЭСР: " + (report.VesrFileName ?? "не загружена")
            + "    Актов: " + report.Acts.Count
            + "    Критично: " + critical
            + "    Оформление: " + formatting
            + "    Предупреждения: " + warnings;
        sheet.Range(2, 1, 2, headers.Length).Merge();
        sheet.Cell(3, 1).Value = "Красный — критично. Жёлтый — ошибка оформления. Оранжевый — предупреждение. Зелёный — совпадает. Итог акта — худший статус его замечаний и комплектов.";
        sheet.Range(3, 1, 3, headers.Length).Merge();
        sheet.Cell(3, 1).Style.Alignment.WrapText = true;

        const int headerRow = 5;
        for (var i = 0; i < headers.Length; i++)
            sheet.Cell(headerRow, i + 1).Value = headers[i];
        StyleHeader(sheet.Range(headerRow, 1, headerRow, headers.Length));

        var row = headerRow + 1;
        foreach (var act in report.Acts)
        {
            sheet.Cell(row, 1).Value = act.ElementNo;
            Status(sheet.Cell(row, 2), act.Overall);
            sheet.Cell(row, 3).Value = act.CriticalCount;
            sheet.Cell(row, 4).Value = act.FormattingCount;
            sheet.Cell(row, 5).Value = act.WarningCount;
            sheet.Cell(row, 6).Value = act.CompletenessText;
            Status(sheet.Cell(row, 6), act.CompletenessStatus);
            sheet.Cell(row, 6).Value = act.CompletenessText;
            sheet.Cell(row, 7).Value = act.FileName;
            sheet.Row(row).Style.Alignment.Vertical = XLAlignmentVerticalValues.Center;
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
        sheet.Rows(headerRow + 1, Math.Max(headerRow + 1, totalRow)).Style.Alignment.WrapText = true;
    }

    private static void WriteKits(XLWorkbook workbook, CheckReport report, string? appVersion)
    {
        var sheet = workbook.AddWorksheet("Комплекты");
        sheet.Cell(1, 1).Value = Titled("Комплекты рабочей документации", appVersion);
        sheet.Cell(1, 1).Style.Font.Bold = true;
        sheet.Cell(1, 1).Style.Font.FontSize = 16;
        sheet.Range(1, 1, 1, 6).Merge();
        sheet.Cell(2, 1).Value = "Строка — один шифр. Фамилии читаются из штампа и строк ролей. Подписанты — фамилии из файла .sig, без удостоверяющих центров.";
        sheet.Range(2, 1, 2, 8).Merge();
        sheet.Cell(2, 1).Style.Alignment.WrapText = true;
        sheet.Row(2).Height = 32;

        var headers = new[]
        {
            "Элемент", "Акт", "Шифр", "Код ДГП",
            "Наименование в акте", "Наименование в ВЭРД", "Статус наименования",
            "Бумажных экз.", "Электронных экз.",
            "Файл РД", "Наличие файла",
            "Шифр на титуле", "Наименование в РД", "Статус наименования РД",
            "Элемент в РД", "Наименование элемента в РД", "Статус элемента РД",
            "Фамилии в документе", "Подписанты", "Статус фамилий",
            "Итог",
        };
        const int headerRow = 4;
        for (var i = 0; i < headers.Length; i++)
            sheet.Cell(headerRow, i + 1).Value = headers[i];
        StyleHeader(sheet.Range(headerRow, 1, headerRow, headers.Length));

        var row = headerRow + 1;
        foreach (var kit in report.Kits)
        {
            var col = 1;
            sheet.Cell(row, col++).Value = kit.ElementNo;
            sheet.Cell(row, col++).Value = kit.ActName;
            sheet.Cell(row, col++).Value = kit.Cipher;
            sheet.Cell(row, col++).Value = kit.DgpCode;
            sheet.Cell(row, col++).Value = kit.ActKitName;
            sheet.Cell(row, col++).Value = kit.VerdKitName;
            Status(sheet.Cell(row, col++), kit.NameStatus);
            sheet.Cell(row, col++).Value = kit.PaperCopies;
            sheet.Cell(row, col++).Value = kit.ElectronicCopies;
            sheet.Cell(row, col++).Value = kit.RdFileName;
            sheet.Cell(row, col++).Value = kit.Presence;
            sheet.Cell(row, col++).Value = kit.RdCipher;
            sheet.Cell(row, col++).Value = kit.RdKitName;
            Status(sheet.Cell(row, col++), kit.RdFileName.Length == 0 ? Severity.Ok : kit.RdNameStatus);
            sheet.Cell(row, col++).Value = kit.RdElementNo;
            sheet.Cell(row, col++).Value = kit.RdElementName;
            Status(sheet.Cell(row, col++), kit.RdFileName.Length == 0 ? Severity.Ok : kit.RdElementStatus);
            sheet.Cell(row, col++).Value = kit.Surnames;
            sheet.Cell(row, col++).Value = kit.Signers;
            Status(sheet.Cell(row, col++), kit.RdFileName.Length == 0 ? Severity.Ok : kit.SurnameStatus);
            Status(sheet.Cell(row, col), kit.Overall);
            row++;
        }

        sheet.SheetView.FreezeRows(headerRow);
        if (report.Kits.Count > 0)
            sheet.Range(headerRow, 1, row - 1, headers.Length).SetAutoFilter();
        Widths(sheet, headers.Length);
        sheet.Column(5).Width = 42;
        sheet.Column(6).Width = 42;
        sheet.Column(13).Width = 42;
        sheet.Column(16).Width = 42;
        sheet.Column(18).Width = 36;
        sheet.Column(19).Width = 36;
        if (row > headerRow + 1)
            sheet.Rows(headerRow + 1, row - 1).Style.Alignment.WrapText = true;
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
        sheet.Cell(2, 1).Value = "Одинаковые замечания собраны в одну строку. Совпавшие проверки на этот лист не выводятся.";
        sheet.Range(2, 1, 2, headers.Length).Merge();

        const int headerRow = 4;
        for (var i = 0; i < headers.Length; i++)
            sheet.Cell(headerRow, i + 1).Value = headers[i];
        StyleHeader(sheet.Range(headerRow, 1, headerRow, headers.Length));

        var row = headerRow + 1;
        var number = 1;
        foreach (var finding in report.Findings)
        {
            sheet.Cell(row, 1).Value = number++;
            sheet.Cell(row, 2).Value = finding.ElementNo;
            Status(sheet.Cell(row, 3), finding.Severity);
            sheet.Cell(row, 4).Value = finding.CheckName;
            WriteValue(sheet.Cell(row, 5), finding.InAct);
            WriteValue(sheet.Cell(row, 6), finding.InRd);
            WriteValue(sheet.Cell(row, 7), finding.InVerd);
            WriteValue(sheet.Cell(row, 8), finding.InVesr);
            sheet.Cell(row, 9).Value = finding.Places.Length > 0
                ? finding.Places
                : string.Join(" / ", new[] { finding.Act, finding.Kit }.Where(s => !string.IsNullOrWhiteSpace(s) && s != "—"));
            row++;
        }

        sheet.SheetView.FreezeRows(headerRow);
        if (report.Findings.Count > 0)
            sheet.Range(headerRow, 1, row - 1, headers.Length).SetAutoFilter();
        Widths(sheet, headers.Length);
        sheet.Column(1).Width = 6;
        sheet.Column(4).Width = 42;
        sheet.Column(5).Width = 36;
        sheet.Column(6).Width = 36;
        sheet.Column(7).Width = 36;
        sheet.Column(8).Width = 36;
        sheet.Column(9).Width = 48;
        if (row > headerRow + 1)
            sheet.Rows(headerRow + 1, row - 1).Style.Alignment.WrapText = true;
    }

    private static string Titled(string title, string? version) =>
        string.IsNullOrWhiteSpace(version) ? title : title + ", версия " + version.Trim();

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
        cell.Style.Fill.BackgroundColor = severity switch
        {
            Severity.Critical => XLColor.FromHtml("#F4C7C3"),
            Severity.Formatting => XLColor.FromHtml("#FFE08A"),
            Severity.Warning => XLColor.FromHtml("#F8CBAD"),
            _ => XLColor.FromHtml("#C6EFCE"),
        };
        cell.Style.Font.Bold = severity != Severity.Ok;
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

    private static void Money(IXLCell cell, decimal? value)
    {
        if (value is null)
            return;
        cell.Value = value.Value;
        cell.Style.NumberFormat.Format = "#,##0.00";
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
