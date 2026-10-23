using System.IO.Compression;
using System.Text;
using ClosedXML.Excel;

namespace ProverkaRD.Engine;

public static class ReportWriter
{
    public static void Write(CheckReport report, string path, string? appVersion = null)
    {
        using var workbook = new XLWorkbook();
        workbook.Style.Font.FontName = SeverityPalette.FontName;
        workbook.Style.Font.FontSize = 11;
        workbook.Style.Font.SetFontScheme(XLFontScheme.None);
        WriteSummary(workbook, report, appVersion);
        WriteKits(workbook, report, appVersion);
        WriteFindings(workbook, report, appVersion);
        WriteNotices(workbook, report, appVersion);
        WriteRegistry(workbook, report, appVersion);
        foreach (var sheet in workbook.Worksheets)
        {
            StampFooter(sheet, appVersion);
            var used = sheet.RangeUsed();
            if (used is null)
                continue;
            used.Style.Font.FontName = SeverityPalette.FontName;
            used.Style.Font.SetFontScheme(XLFontScheme.None);
        }
        var directory = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(directory))
            Directory.CreateDirectory(directory);
        workbook.SaveAs(path);
        UseSegoeInPackage(path);
    }

    /// <summary>
    /// ClosedXML оставляет нулевой шрифт книги Calibri, даже если стиль ячеек уже Segoe UI.
    /// В пакете нулевой шрифт и шрифт темы заменяются, чтобы вся книга была одним шрифтом.
    /// </summary>
    private static void UseSegoeInPackage(string path)
    {
        using var zip = ZipFile.Open(path, ZipArchiveMode.Update);
        ReplaceEntry(zip, "xl/styles.xml", xml => ReplaceFirst(xml, "val=\"Calibri\"", "val=\"" + SeverityPalette.FontName + "\""));
        ReplaceEntry(zip, "xl/theme/theme1.xml", xml => xml.Replace("typeface=\"Calibri\"", "typeface=\"" + SeverityPalette.FontName + "\"", StringComparison.Ordinal));
    }

    private static void ReplaceEntry(ZipArchive zip, string name, Func<string, string> edit)
    {
        var entry = zip.GetEntry(name);
        if (entry is null)
            return;
        string xml;
        using (var stream = entry.Open())
        using (var reader = new StreamReader(stream, Encoding.UTF8))
            xml = reader.ReadToEnd();
        var updated = edit(xml);
        if (updated == xml)
            return;
        entry.Delete();
        var created = zip.CreateEntry(name, CompressionLevel.Optimal);
        using var output = created.Open();
        using var writer = new StreamWriter(output, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
        writer.Write(updated);
    }

    private static string ReplaceFirst(string text, string oldValue, string newValue)
    {
        var index = text.IndexOf(oldValue, StringComparison.Ordinal);
        return index < 0 ? text : text[..index] + newValue + text[(index + oldValue.Length)..];
    }

    private static void WriteSummary(XLWorkbook workbook, CheckReport report, string? appVersion)
    {
        var sheet = workbook.AddWorksheet("Сводка");
        UseFont(sheet);
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
        var loose = CountUnassigned(report);
        var critical = rows.Sum(row => row.Score.Critical) + loose.Critical;
        var formatting = rows.Sum(row => row.Score.Formatting) + loose.Formatting;
        var warnings = rows.Sum(row => row.Score.Warnings) + loose.Warnings;
        sheet.Cell(2, 1).Value =
            "ВЭРД: " + (report.VerdFileName ?? "не загружена")
            + "    ВЭСР: " + (report.VesrFileName ?? "не загружена")
            + "    Актов: " + report.Acts.Count
            + "    Критично: " + critical
            + "    Оформление: " + formatting
            + "    Предупреждения: " + warnings;
        sheet.Range(2, 1, 2, headers.Length).Merge();
        sheet.Cell(3, 1).Value = "Критично — красный. Оформление — жёлтый. Предупреждение — оранжевый. ОК — зелёный. Итог акта — худший статус замечаний, комплектов, комплектности и сумм. Пропуски — на листе «Пропуски», без обрезки списка.";
        sheet.Range(3, 1, 3, headers.Length).Merge();
        sheet.Cell(3, 1).Style.Alignment.WrapText = true;

        sheet.Cell(4, 1).Value = report.Load.Text;
        sheet.Range(4, 1, 4, headers.Length).Merge();
        sheet.Cell(4, 1).Style.Alignment.WrapText = true;
        sheet.Cell(4, 1).Style.Alignment.Vertical = XLAlignmentVerticalValues.Center;
        sheet.Row(4).Height = 36;

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
        Outline(sheet, headerRow, 1, Math.Max(totalRow, headerRow), headers.Length);
    }

    private static void WriteKits(XLWorkbook workbook, CheckReport report, string? appVersion)
    {
        var sheet = workbook.AddWorksheet("Комплекты");
        UseFont(sheet);
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
        Group(sheet, groupRow, 1, 2, "Акт", "#334155");
        Group(sheet, groupRow, 3, 6, "1. Шифр", "#1F4E79");
        Group(sheet, groupRow, 7, 10, "2. Наименование комплекта", "#215C3A");
        Group(sheet, groupRow, 11, 14, "3. Наименование элемента", "#6B3F1D");
        Group(sheet, groupRow, 15, 16, "4. Файлы", "#5C3B6E");
        Group(sheet, groupRow, 17, 19, "5. Подписи", "#8C3A3A");
        Group(sheet, groupRow, 20, 20, "Аванс", "#334155");
        Group(sheet, groupRow, 21, 21, "6. Итог", "#1F4E79");

        var headers = new[]
        {
            "Элемент", "Акт",
            "В акте", "В ВЭРД", "На титуле РД", "Статус",
            "В акте", "В ВЭРД", "В РД", "Статус",
            "В акте", "В ВЭРД", "В РД", "Статус",
            "PDF", ".sig",
            "Фамилии из тома", "Нет в ЭЦП", "Статус",
            "Погашение аванса без НДС",
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
            sheet.Cell(row, col++).Value = kit.ActCipher;
            sheet.Cell(row, col++).Value = kit.VerdCipher;
            sheet.Cell(row, col++).Value = kit.TitleCipher;
            Status(sheet.Cell(row, col++), kit.CipherStatus, kit.CipherBlame);
            sheet.Cell(row, col++).Value = kit.ActKitName;
            sheet.Cell(row, col++).Value = kit.VerdKitName;
            sheet.Cell(row, col++).Value = kit.RdKitName;
            Status(sheet.Cell(row, col++), kit.NameStatus, kit.NameBlame);
            sheet.Cell(row, col++).Value = kit.ActElementName;
            sheet.Cell(row, col++).Value = kit.VerdElementName;
            sheet.Cell(row, col++).Value = kit.RdElementName;
            Status(sheet.Cell(row, col++), kit.ElementNameStatus, kit.ElementNameBlame);
            YesNo(sheet.Cell(row, col++), kit.PdfState, Severity.Warning);
            YesNo(sheet.Cell(row, col++), kit.SigState, Severity.Critical);
            sheet.Cell(row, col++).Value = kit.Surnames;
            sheet.Cell(row, col++).Value = kit.MissingSigners;
            Status(sheet.Cell(row, col++), kit.SurnameStatus);
            WriteAdvance(sheet.Cell(row, col++), kit.AdvanceNet);
            Status(sheet.Cell(row, col), kit.Overall);
            row++;
        }

        sheet.SheetView.Freeze(headerRow, 3);
        if (report.Kits.Count > 0)
            sheet.Range(headerRow, 1, row - 1, columns).SetAutoFilter();
        Widths(sheet, columns);
        sheet.Column(20).Width = 22;
        foreach (var wide in new[] { 5, 7, 8, 9, 11, 12, 13, 17, 18 })
            sheet.Column(wide).Width = 36;
        if (row > headerRow + 1)
        {
            sheet.Rows(headerRow + 1, row - 1).Style.Alignment.WrapText = true;
            for (var r = headerRow + 1; r < row; r++)
            {
                var advance = sheet.Cell(r, 20);
                if (advance.DataType == XLDataType.Number)
                    advance.Style.NumberFormat.Format = "0,00";
            }
        }
        var blockEnd = Math.Max(row - 1, headerRow);
        Outline(sheet, groupRow, 1, blockEnd, 2);
        Outline(sheet, groupRow, 3, blockEnd, 6);
        Outline(sheet, groupRow, 7, blockEnd, 10);
        Outline(sheet, groupRow, 11, blockEnd, 14);
        Outline(sheet, groupRow, 15, blockEnd, 16);
        Outline(sheet, groupRow, 17, blockEnd, 19);
        Outline(sheet, groupRow, 20, blockEnd, 20);
        Outline(sheet, groupRow, 21, blockEnd, 21);
    }

    private static (int Critical, int Formatting, int Warnings) CountUnassigned(CheckReport report)
    {
        var critical = 0;
        var formatting = 0;
        var warnings = 0;
        foreach (var finding in report.Findings)
        {
            if (report.Acts.Any(act => ActScore.Concerns(act, finding)))
                continue;
            switch (finding.Severity)
            {
                case Severity.Critical: critical++; break;
                case Severity.Formatting: formatting++; break;
                case Severity.Warning: warnings++; break;
            }
        }

        return (critical, formatting, warnings);
    }

    private static void WriteFindings(XLWorkbook workbook, CheckReport report, string? appVersion)
    {
        var sheet = workbook.AddWorksheet("Замечания");
        UseFont(sheet);
        var headers = new[]
        {
            "№", "Элемент", "Критичность", "Что проверялось",
            "В акте", "В РД", "В ВЭРД", "В ВЭСР", "Где", "Где ошибка",
        };
        sheet.Cell(1, 1).Value = Titled("Замечания", appVersion);
        sheet.Cell(1, 1).Style.Font.Bold = true;
        sheet.Cell(1, 1).Style.Font.FontSize = 16;
        sheet.Range(1, 1, 1, headers.Length).Merge();
        sheet.Cell(2, 1).Value = "Одно поле — одна строка: значения из акта, РД, ВЭРД и ВЭСР рядом. Отличающееся значение выделено. В столбце «Где» только шифры. «Где ошибка» заполняется, если два источника совпадают, а третий нет.";
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
            sheet.Cell(row, 10).Value = finding.ErrorWhere;
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
        sheet.Column(10).Width = 22;
        if (row > headerRow + 1)
            sheet.Rows(headerRow + 1, row - 1).Style.Alignment.WrapText = true;
        Outline(sheet, headerRow, 1, Math.Max(row - 1, headerRow), headers.Length);
    }

    private static void WriteNotices(XLWorkbook workbook, CheckReport report, string? appVersion)
    {
        var sheet = workbook.AddWorksheet("Пропуски");
        UseFont(sheet);
        var headers = new[] { "Источник", "Место", "Сообщение" };
        sheet.Cell(1, 1).Value = Titled("Пропуски и ошибки чтения", appVersion);
        sheet.Cell(1, 1).Style.Font.Bold = true;
        sheet.Cell(1, 1).Style.Font.FontSize = 16;
        sheet.Range(1, 1, 1, headers.Length).Merge();
        sheet.Cell(2, 1).Value = "Каждый непрочитанный файл, пропущенная строка и сбой разбора. Список не обрезается.";
        sheet.Range(2, 1, 2, headers.Length).Merge();
        const int headerRow = 4;
        for (var i = 0; i < headers.Length; i++)
            sheet.Cell(headerRow, i + 1).Value = headers[i];
        StyleHeader(sheet.Range(headerRow, 1, headerRow, headers.Length));
        var row = headerRow + 1;
        foreach (var notice in report.Notices)
        {
            sheet.Cell(row, 1).Value = notice.Source;
            sheet.Cell(row, 2).Value = notice.Place;
            sheet.Cell(row, 3).Value = notice.Message;
            row++;
        }
        sheet.SheetView.FreezeRows(headerRow);
        sheet.Column(1).Width = 22;
        sheet.Column(2).Width = 42;
        sheet.Column(3).Width = 88;
        if (row > headerRow + 1)
            sheet.Rows(headerRow + 1, row - 1).Style.Alignment.WrapText = true;
        Outline(sheet, headerRow, 1, Math.Max(row - 1, headerRow), headers.Length);
    }

    private static void WriteRegistry(XLWorkbook workbook, CheckReport report, string? appVersion)
    {
        var sheet = workbook.AddWorksheet("Опись комплекта");
        UseFont(sheet);
        var headers = new[]
        {
            "№ п/п", "Элемент", "Шифр комплекта", "Наименование", "Файл PDF", "Подпись (.sig)", "Подписанты", "Статус подписи",
        };
        sheet.Cell(1, 1).Value = Titled("Опись комплекта", appVersion);
        sheet.Cell(1, 1).Style.Font.Bold = true;
        sheet.Cell(1, 1).Style.Font.FontSize = 16;
        sheet.Range(1, 1, 1, headers.Length).Merge();
        sheet.Cell(2, 1).Value = "Опись PDF. Номер и итог — только у комплектов. Посторонние файлы ниже итога, без номера. Статус подписи — по подписи и сертификату.";
        sheet.Range(2, 1, 2, headers.Length).Merge();
        sheet.Cell(2, 1).Style.Alignment.WrapText = true;
        sheet.Row(2).Height = 32;

        const int headerRow = 4;
        for (var i = 0; i < headers.Length; i++)
            sheet.Cell(headerRow, i + 1).Value = headers[i];
        StyleHeader(sheet.Range(headerRow, 1, headerRow, headers.Length));

        var sets = report.Registry
            .Where(item => !item.Foreign)
            .OrderBy(item => item.Element, Comparer<string>.Create(ElementOrder.Compare))
            .ThenBy(item => item.Cipher, StringComparer.CurrentCultureIgnoreCase)
            .ThenBy(item => item.FileName, StringComparer.CurrentCultureIgnoreCase)
            .ToList();
        var foreign = report.Registry
            .Where(item => item.Foreign)
            .OrderBy(item => item.FileName, StringComparer.CurrentCultureIgnoreCase)
            .ToList();

        var row = headerRow + 1;
        var number = 1;
        foreach (var item in sets)
        {
            WriteRegistryRow(sheet, row, item, number++);
            row++;
        }

        var signed = sets.Count(item => item.HasSignature);
        sheet.Cell(row, 1).Value = "Комплектов: " + sets.Count
            + " | Подписано: " + signed
            + " | Посторонних файлов: " + foreign.Count;
        sheet.Range(row, 1, row, headers.Length).Merge();
        sheet.Range(row, 1, row, headers.Length).Style.Font.Bold = true;
        var totalRow = row;
        row++;

        var foreignStart = 0;
        if (foreign.Count > 0)
        {
            sheet.Cell(row, 1).Value = "Посторонние файлы";
            sheet.Range(row, 1, row, headers.Length).Merge();
            sheet.Cell(row, 1).Style.Font.Bold = true;
            foreignStart = row;
            row++;
            foreach (var item in foreign)
            {
                WriteRegistryRow(sheet, row, item, null);
                row++;
            }
        }

        sheet.PageSetup.PageOrientation = XLPageOrientation.Landscape;
        sheet.PageSetup.PagesWide = 1;
        sheet.PageSetup.SetRowsToRepeatAtTop(headerRow, headerRow);
        sheet.SheetView.FreezeRows(headerRow);
        sheet.PageSetup.FitToPages(1, 0);
        Widths(sheet, headers.Length);
        sheet.Column(1).Width = 12;
        sheet.Column(2).Width = 28;
        sheet.Column(3).Width = 36;
        sheet.Column(4).Width = 42;
        sheet.Column(5).Width = 42;
        sheet.Column(6).Width = 28;
        sheet.Column(7).Width = 36;
        sheet.Column(8).Width = 20;
        var lastData = Math.Max(totalRow, row - 1);
        if (lastData > headerRow)
            sheet.Rows(headerRow + 1, lastData).Style.Alignment.WrapText = true;
        Outline(sheet, headerRow, 1, totalRow, headers.Length);
        if (foreignStart > 0)
            Outline(sheet, foreignStart, 1, row - 1, headers.Length);
    }

    private static void WriteRegistryRow(IXLWorksheet sheet, int row, RegistryRow item, int? number)
    {
        if (number is not null)
            sheet.Cell(row, 1).Value = number.Value;
        sheet.Cell(row, 2).Value = item.Element;
        sheet.Cell(row, 3).Value = item.Cipher;
        sheet.Cell(row, 4).Value = item.Name;
        sheet.Cell(row, 5).Value = item.FileName;
        sheet.Cell(row, 6).Value = item.SignatureName;
        sheet.Cell(row, 7).Value = item.Signers;
        Status(sheet.Cell(row, 8), item.SignatureColumn);
    }

    private static string Titled(string title, string? version)
    {
        var named = ProductBrand.Name + ". " + title;
        return string.IsNullOrWhiteSpace(version) ? named : named + ", версия " + version.Trim();
    }

    private static void StampFooter(IXLWorksheet sheet, string? appVersion)
    {
        sheet.PageSetup.Footer.Left.AddText(ProductBrand.Name, XLHFOccurrence.AllPages);
        if (!string.IsNullOrWhiteSpace(appVersion))
            sheet.PageSetup.Footer.Right.AddText("версия " + appVersion.Trim(), XLHFOccurrence.AllPages);
    }

    private static void Group(IXLWorksheet sheet, int row, int from, int to, string title, string color)
    {
        var range = sheet.Range(row, from, row, to);
        range.Merge();
        range.Value = title;
        range.Style.Font.Bold = true;
        range.Style.Font.FontName = SeverityPalette.FontName;
        range.Style.Font.SetFontScheme(XLFontScheme.None);
        range.Style.Font.FontColor = XLColor.White;
        range.Style.Fill.BackgroundColor = XLColor.FromHtml(color);
        range.Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;
        range.Style.Alignment.Vertical = XLAlignmentVerticalValues.Center;
        sheet.Row(row).Height = 22;
    }

    private static void StyleHeader(IXLRange range)
    {
        range.Style.Font.Bold = true;
        range.Style.Font.FontName = SeverityPalette.FontName;
        range.Style.Font.SetFontScheme(XLFontScheme.None);
        range.Style.Font.FontColor = XLColor.White;
        range.Style.Fill.BackgroundColor = XLColor.FromHtml(SeverityPalette.Header);
        range.Style.Alignment.WrapText = true;
        range.Style.Alignment.Vertical = XLAlignmentVerticalValues.Center;
        range.Worksheet.Row(range.FirstRow().RowNumber()).Height = 32;
    }

    private static void Status(IXLCell cell, Severity severity, string? blame = null)
    {
        var text = SeverityText.ToRu(severity);
        if (severity != Severity.Ok && !string.IsNullOrWhiteSpace(blame))
            text += ", " + blame.Trim();
        cell.Value = text;
        Paint(cell, severity);
        cell.Style.Font.Bold = severity != Severity.Ok;
    }

    private static void Paint(IXLCell cell, Severity severity)
    {
        cell.Style.Font.FontName = SeverityPalette.FontName;
        cell.Style.Font.SetFontScheme(XLFontScheme.None);
        cell.Style.Fill.BackgroundColor = XLColor.FromHtml(SeverityPalette.For(severity));
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
        cell.Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Right;
    }

    private static void WriteCompared(IXLCell cell, string text, IReadOnlyList<string> peers)
    {
        WriteValue(cell, text);
        if (!Differs(text, peers))
            return;
        cell.Style.Font.FontColor = XLColor.FromHtml("#7F1D1D");
        cell.Style.Font.Bold = true;
        cell.Style.Fill.BackgroundColor = XLColor.FromHtml(SeverityPalette.Critical);
    }

    private static void UseFont(IXLWorksheet sheet)
    {
        sheet.Style.Font.FontName = SeverityPalette.FontName;
        sheet.Style.Font.FontSize = 11;
        sheet.Style.Font.SetFontScheme(XLFontScheme.None);
    }

    private static void Outline(IXLWorksheet sheet, int fromRow, int fromCol, int toRow, int toCol)
    {
        var range = sheet.Range(fromRow, fromCol, toRow, toCol);
        range.Style.Border.OutsideBorder = XLBorderStyleValues.Thick;
        range.Style.Border.OutsideBorderColor = XLColor.FromHtml(SeverityPalette.Ink);
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
            cell.Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Right;
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
