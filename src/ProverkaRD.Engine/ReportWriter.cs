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
        sheet.Cell(1, 1).Value = Titled("Проверка рабочей документации", appVersion);
        sheet.Range(1, 1, 1, 8).Merge();
        sheet.Cell(1, 1).Style.Font.Bold = true;
        sheet.Cell(1, 1).Style.Font.FontSize = 16;

        var critical = report.Findings.Count(f => f.Severity == Severity.Critical);
        var formatting = report.Findings.Count(f => f.Severity == Severity.Formatting);
        var warnings = report.Findings.Count(f => f.Severity == Severity.Warning);
        sheet.Cell(2, 1).Value =
            "ВЭРД: " + (report.VerdFileName ?? "не загружена")
            + "    ВЭСР: " + (report.VesrFileName ?? "не загружена")
            + "    Актов: " + report.Acts.Count
            + "    Критично: " + critical
            + "    Ошибок оформления: " + formatting
            + "    Предупреждений: " + warnings;
        sheet.Range(2, 1, 2, 8).Merge();
        sheet.Cell(3, 1).Value =
            "Красный — критичное расхождение. Жёлтый — ошибка оформления: тексты совпадают после нормализации (пробелы, переносы, регистр, точка в конце, «ПК5859» и «ПК 5859», кавычки). "
            + "Оранжевый — предупреждение. Зелёный — совпадает. Деньги сравниваются с допуском 0,01 руб. НДС 22%: столбец «Пересчёт» = сумма без НДС по ВЭСР × 1,22. "
            + "Лист «Замечания» содержит только расхождения и предупреждения.";
        sheet.Range(3, 1, 3, 8).Merge();
        sheet.Row(3).Height = 36;
        sheet.Cell(3, 1).Style.Alignment.WrapText = true;

        var headers = new[]
        {
            "Акт", "Элемент",
            "Наименование в акте", "Наименование в ВЭРД", "Статус наименования",
            "Наименование стоимости в акте", "Наименование в ВЭСР", "Статус наименования стоимости",
            "Код ДГП в акте", "Код ДГП в ВЭРД", "Код есть в ВЭСР", "Статус кода",
            "Без НДС, акт", "Без НДС, ВЭСР", "Статус без НДС",
            "С НДС, акт", "Пересчёт ВЭСР × 1,22", "Статус с НДС",
            "Комплектов в акте", "Комплектов в ВЭРД", "Комплектность", "Статус комплектности",
            "Итог",
        };
        const int headerRow = 5;
        for (var i = 0; i < headers.Length; i++)
            sheet.Cell(headerRow, i + 1).Value = headers[i];
        StyleHeader(sheet.Range(headerRow, 1, headerRow, headers.Length));

        var row = headerRow + 1;
        foreach (var act in report.Acts)
        {
            var col = 1;
            sheet.Cell(row, col++).Value = act.FileName;
            sheet.Cell(row, col++).Value = act.ElementNo;
            sheet.Cell(row, col++).Value = act.ActElementName;
            sheet.Cell(row, col++).Value = act.VerdElementName;
            Status(sheet.Cell(row, col++), act.NameStatus);
            sheet.Cell(row, col++).Value = act.ActCostName;
            sheet.Cell(row, col++).Value = act.VesrCostName;
            Status(sheet.Cell(row, col++), act.CostNameStatus);
            sheet.Cell(row, col++).Value = act.ActCode;
            sheet.Cell(row, col++).Value = act.VerdCode;
            sheet.Cell(row, col++).Value = act.CodeFoundInVesr ? "да" : "нет";
            Status(sheet.Cell(row, col++), act.CodeStatus);
            Money(sheet.Cell(row, col++), act.ActNet);
            Money(sheet.Cell(row, col++), act.VesrNet);
            Status(sheet.Cell(row, col++), act.NetStatus);
            Money(sheet.Cell(row, col++), act.ActGross);
            Money(sheet.Cell(row, col++), act.GrossRecalculated);
            Status(sheet.Cell(row, col++), act.GrossStatus);
            sheet.Cell(row, col++).Value = act.ActKitCount;
            sheet.Cell(row, col++).Value = act.VerdKitCount;
            sheet.Cell(row, col++).Value = act.CompletenessText;
            Status(sheet.Cell(row, col++), act.CompletenessStatus);
            Status(sheet.Cell(row, col), act.Overall);
            sheet.Row(row).Style.Alignment.Vertical = XLAlignmentVerticalValues.Center;
            row++;
        }

        var totalRow = row;
        sheet.Cell(totalRow, 1).Value = "Итого";
        sheet.Cell(totalRow, 1).Style.Font.Bold = true;
        Money(sheet.Cell(totalRow, 13), Sum(report.Acts.Select(a => a.ActNet)));
        Money(sheet.Cell(totalRow, 14), Sum(report.Acts.Select(a => a.VesrNet)));
        Money(sheet.Cell(totalRow, 16), Sum(report.Acts.Select(a => a.ActGross)));
        Money(sheet.Cell(totalRow, 17), Sum(report.Acts.Select(a => a.GrossRecalculated)));
        sheet.Range(totalRow, 1, totalRow, headers.Length).Style.Font.Bold = true;
        sheet.Range(totalRow, 1, totalRow, headers.Length).Style.Fill.BackgroundColor = XLColor.FromHtml("#D6DCE4");

        sheet.SheetView.FreezeRows(headerRow);
        if (report.Acts.Count > 0)
            sheet.Range(headerRow, 1, totalRow - 1, headers.Length).SetAutoFilter();
        Widths(sheet, headers.Length);
        sheet.Column(3).Width = 46;
        sheet.Column(4).Width = 46;
        sheet.Column(6).Width = 46;
        sheet.Column(7).Width = 46;
        sheet.Column(21).Width = 42;
        sheet.Rows(headerRow + 1, Math.Max(headerRow + 1, totalRow)).Style.Alignment.WrapText = true;
    }

    private static void WriteKits(XLWorkbook workbook, CheckReport report, string? appVersion)
    {
        var sheet = workbook.AddWorksheet("Комплекты");
        sheet.Cell(1, 1).Value = Titled("Комплекты рабочей документации", appVersion);
        sheet.Cell(1, 1).Style.Font.Bold = true;
        sheet.Cell(1, 1).Style.Font.FontSize = 16;
        sheet.Range(1, 1, 1, 6).Merge();
        sheet.Cell(2, 1).Value = "Строка — один шифр. Фамилии собраны с титульных листов, из основной надписи чертежей и с первого листа ведомости объёмов работ. Подписанты — фамилии из сертификатов файла .sig, без удостоверяющих центров.";
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
        sheet.Cell(1, 1).Value = Titled("Замечания", appVersion);
        sheet.Cell(1, 1).Style.Font.Bold = true;
        sheet.Cell(1, 1).Style.Font.FontSize = 16;
        sheet.Cell(2, 1).Value = "Каждая строка — одно расхождение или предупреждение. Совпавшие проверки на этот лист не выводятся: они зелёные на листах «Сводка» и «Комплекты».";
        sheet.Range(2, 1, 2, 6).Merge();

        var headers = new[] { "№", "Акт", "Комплект", "Что проверялось", "Значение A", "Значение B", "Критичность", "Пояснение" };
        const int headerRow = 4;
        for (var i = 0; i < headers.Length; i++)
            sheet.Cell(headerRow, i + 1).Value = headers[i];
        StyleHeader(sheet.Range(headerRow, 1, headerRow, headers.Length));

        var row = headerRow + 1;
        var number = 1;
        foreach (var finding in report.Findings)
        {
            sheet.Cell(row, 1).Value = number++;
            sheet.Cell(row, 2).Value = finding.Act;
            sheet.Cell(row, 3).Value = finding.Kit;
            sheet.Cell(row, 4).Value = finding.CheckName;
            sheet.Cell(row, 5).Value = finding.ValueA;
            sheet.Cell(row, 6).Value = finding.ValueB;
            Status(sheet.Cell(row, 7), finding.Severity);
            sheet.Cell(row, 8).Value = finding.Explanation;
            row++;
        }

        sheet.SheetView.FreezeRows(headerRow);
        if (report.Findings.Count > 0)
            sheet.Range(headerRow, 1, row - 1, headers.Length).SetAutoFilter();
        Widths(sheet, headers.Length);
        sheet.Column(4).Width = 42;
        sheet.Column(5).Width = 55;
        sheet.Column(6).Width = 55;
        sheet.Column(8).Width = 78;
        if (row > headerRow + 1)
            sheet.Rows(headerRow + 1, row - 1).Style.Alignment.WrapText = true;
        sheet.Column(1).Width = 6;
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

    private static void Money(IXLCell cell, decimal? value)
    {
        if (value is null)
            return;
        cell.Value = value.Value;
        cell.Style.NumberFormat.Format = "#,##0.00";
    }

    private static decimal? Sum(IEnumerable<decimal?> values)
    {
        decimal? sum = null;
        foreach (var value in values)
        {
            if (value is null)
                continue;
            sum = (sum ?? 0m) + value.Value;
        }
        return sum;
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
