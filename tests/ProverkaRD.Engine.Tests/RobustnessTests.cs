using System.IO.Compression;
using ClosedXML.Excel;
using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Wordprocessing;
using ProverkaRD.Engine.Parsing;

namespace ProverkaRD.Engine.Tests;

public class RobustnessTests
{
    [Fact]
    public void Money_is_decimal_and_excel_doubles_round_to_kopecks()
    {
        Assert.Equal(0.30m, MoneyDecimal.FromExcelDouble(0.1 + 0.2));
        Assert.Equal(1.01m, MoneyDecimal.Kopecks(1.005m));
        Assert.Equal(1.00m, MoneyDecimal.Kopecks(1.004m));
        Assert.Equal(1234.50m, TextMatch.ParseMoney("1 234,50"));
        Assert.Equal(1556042.07m, TextMatch.ParseMoney("1 556 042,07"));
        Assert.Equal("7.46", MoneyDecimal.ExcelNumberText(7.46));

        using var book = new XLWorkbook();
        var sheet = book.AddWorksheet("ВЭ");
        sheet.Cell(1, 1).Value = 0.1 + 0.2;
        sheet.Cell(2, 1).Value = 1234.50m;
        Assert.Equal(0.30m, MoneyDecimal.ReadMoney(sheet.Cell(1, 1)));
        Assert.Equal(1234.50m, MoneyDecimal.ReadMoney(sheet.Cell(2, 1)));
        Assert.Equal(XLDataType.Number, sheet.Cell(1, 1).DataType);
    }

    [Fact]
    public void Locked_file_is_described_without_reading_it()
    {
        var locked = new IOException("The process cannot access the file", unchecked((int)0x80070020));
        Assert.True(FileProblems.IsLocked(locked));
        Assert.True(FileProblems.IsLocked(new InvalidOperationException("снаружи", locked)));
        Assert.False(FileProblems.IsLocked(new IOException("disk full")));

        var text = FileProblems.Describe(@"C:\папка\ведомость.xlsx", locked);
        Assert.Contains("занят другой программой", text);
        Assert.Contains("ведомость.xlsx", text);
        Assert.DoesNotContain("ведомость.xlsx".Replace("ведомость", "секрет"), text);
    }

    [Fact]
    public void Long_windows_paths_get_the_extended_prefix()
    {
        Assert.Equal(@"C:\short.xlsx", SafePath.PrefixIfLong(@"C:\short.xlsx"));
        var local = @"C:\" + new string('a', 260);
        Assert.True(local.Length >= SafePath.LegacyMax);
        Assert.Equal(@"\\?\" + local, SafePath.PrefixIfLong(local));

        var unc = @"\\server\share\" + new string('b', 260);
        Assert.StartsWith(@"\\?\UNC\server\share\", SafePath.PrefixIfLong(unc));
        Assert.Equal(@"\\?\C:\already", SafePath.PrefixIfLong(@"\\?\C:\already"));
        Assert.Equal("чертёж ё.xlsx", SafePath.FileName(@"\\?\C:\очень длинный\чертёж ё.xlsx"));
    }

    [Fact]
    public void Unusual_and_long_file_names_are_readable()
    {
        var root = Directory.CreateTempSubdirectory("proverkard-long");
        try
        {
            var name = "чертёж ё 'файл' \u05D0 \U0001F600.pdf";
            var current = root.FullName;
            string path;
            try
            {
                path = Nest(current, name);
                File.WriteAllBytes(path, [1, 2, 3, 4]);
            }
            catch (Exception ex) when (ex is IOException or ArgumentException)
            {
                name = "чертёж ё 'файл' \u05D0.pdf";
                path = Nest(current, name);
                File.WriteAllBytes(path, [1, 2, 3, 4]);
            }

            Assert.True(path.Length > 270, path);
            Assert.Equal(new byte[] { 1, 2, 3, 4 }, SharedRead.Bytes(path));
            var parsed = RdParser.Parse(path);
            Assert.Equal(name, parsed.FileName);
            Assert.NotNull(parsed.Errors);
        }
        finally
        {
            root.Delete(recursive: true);
        }
    }

    [Fact]
    public void Skipped_rows_missing_sheet_and_duplicates_are_listed_without_a_cap()
    {
        var dir = Directory.CreateTempSubdirectory("proverkard-notices");
        try
        {
            var vesr = Path.Combine(dir.FullName, "vesr.xlsx");
            WriteNoisyVesr(vesr, extraSkipped: 40);
            var aux = Path.Combine(dir.FullName, "пояснение_ИЗМ.pdf");
            File.WriteAllBytes(aux, "aux"u8.ToArray());
            var sig = Path.Combine(dir.FullName, "пояснение.pdf.sig");
            File.WriteAllBytes(sig, "sig"u8.ToArray());
            var act = Path.Combine(dir.FullName, "РД 4.10.docx");
            WriteBrokenAct(act);

            var steps = new List<CheckProgress>();
            var report = new CheckingEngine().Run(new CheckInput
            {
                VesrPath = vesr,
                ActPaths = [act],
                RdPaths = [aux, sig, dir.FullName],
            }, new SyncProgress(steps));

            Assert.True(report.VerdMissing);
            Assert.Contains(report.Notices, n => n.Message.Contains("ВЭРД не загружена"));
            Assert.Contains(report.Notices, n => n.Source == "ВЭСР" && n.Message.Contains("Лист «ВЭ» не найден"));
            Assert.DoesNotContain(report.Notices, n => n.Message.Contains("Пропущена строка-номер"));
            Assert.Contains(report.Notices, n => n.Message.Contains("Повтор кода"));
            Assert.DoesNotContain(report.Notices, n => n.Message.Contains("примечание или текст"));
            Assert.DoesNotContain(report.Notices, n => n.Message.Contains("Ячейка не разобрана как сумма"));
            Assert.Contains(report.Notices, n => n.Message.Contains("Пропущен вспомогательный файл"));
            Assert.Contains(report.Notices, n => n.Message.Contains("Файл подписи не является комплектом"));
            Assert.Contains(report.Notices, n => n.Message.Contains("Это папка"));
            Assert.Contains(report.Notices, n => n.Message.Contains("Ячейка стоимости не разобрана как сумма"));
            Assert.Contains(report.Notices, n => n.Message.Contains("Таблица не разобрана"));

            var book = VesrParser.Parse(vesr);
            Assert.True(book.ByCode.ContainsKey(TextMatch.CodeKey("4.10.1.1.1")));
            Assert.Equal(1000m, book.ByCode[TextMatch.CodeKey("4.10.1.1.1")].TotalNet);
            Assert.Null(book.ByCode[TextMatch.CodeKey("4.10.1.1.9")].TotalNet);

            Assert.Contains(steps, step => step.Stage == CheckStages.Ledgers && step.FileName == "vesr.xlsx");
            Assert.Contains(steps, step => step.Stage == CheckStages.LoadKits && step.FileName == "пояснение_ИЗМ.pdf");
            Assert.Equal(CheckStages.Report, steps[^1].Stage);
            Assert.Equal(1, steps[^1].Overall, 3);

            var reportPath = Path.Combine(dir.FullName, "report.xlsx");
            ReportWriter.Write(report, reportPath, "0.1.0");
            using var written = new XLWorkbook(reportPath);
            var skips = written.Worksheet("Пропуски");
            var messages = Enumerable.Range(5, skips.LastRowUsed()!.RowNumber() - 4)
                .Select(row => skips.Cell(row, 3).GetString())
                .ToList();
            Assert.Equal(report.Notices.Count, messages.Count);
            Assert.Contains(messages, message => message.Contains("Лист «ВЭ» не найден"));
            Assert.DoesNotContain(messages, message => message.Contains("примечание или текст"));
        }
        finally
        {
            dir.Delete(recursive: true);
        }
    }

    [Fact]
    public void Report_uses_segoe_thick_blocks_and_frozen_identity_columns()
    {
        var dir = Directory.CreateTempSubdirectory("proverkard-layout");
        try
        {
            var verd = Path.Combine(dir.FullName, "verd.xlsx");
            var vesr = Path.Combine(dir.FullName, "vesr.xlsx");
            var act = Path.Combine(dir.FullName, "РД 4.10.docx");
            WritePlainVerd(verd);
            WritePlainVesr(vesr);
            WritePlainAct(act);

            var report = new CheckingEngine().Run(new CheckInput
            {
                VerdPath = verd,
                VesrPath = vesr,
                ActPaths = [act],
            });
            var reportPath = Path.Combine(dir.FullName, "report.xlsx");
            ReportWriter.Write(report, reportPath, "0.1.0");

            using var book = new XLWorkbook(reportPath);
            Assert.Equal(SeverityPalette.FontName, book.Style.Font.FontName);
            foreach (var sheet in book.Worksheets)
                Assert.Equal(SeverityPalette.FontName, sheet.Style.Font.FontName);

            var summary = book.Worksheet("Сводка");
            Assert.Equal(ProductBrand.Name + ". Проверка рабочей документации, версия 0.1.0", summary.Cell(1, 1).GetString());
            Assert.Equal(ProductBrand.Name, summary.PageSetup.Footer.Left.GetText(XLHFOccurrence.OddPages));
            Assert.Equal("версия 0.1.0", summary.PageSetup.Footer.Right.GetText(XLHFOccurrence.OddPages));
            Assert.Equal("Элемент", summary.Cell(5, 1).GetString());
            Assert.Equal(XLBorderStyleValues.Thick, summary.Cell(5, 1).Style.Border.TopBorder);
            foreach (var sheet in book.Worksheets)
            {
                Assert.StartsWith(ProductBrand.Name + ". ", sheet.Cell(1, 1).GetString());
                Assert.Contains("версия 0.1.0", sheet.Cell(1, 1).GetString());
                Assert.Equal(ProductBrand.Name, sheet.PageSetup.Footer.Left.GetText(XLHFOccurrence.OddPages));
                Assert.Equal("версия 0.1.0", sheet.PageSetup.Footer.Right.GetText(XLHFOccurrence.OddPages));
            }

            var kits = book.Worksheet("Комплекты");
            Assert.Equal("1. Шифр", kits.Cell(4, 3).GetString());
            Assert.Equal("Элемент", kits.Cell(5, 1).GetString());
            Assert.Equal("Акт", kits.Cell(5, 2).GetString());
            Assert.Equal("В акте", kits.Cell(5, 3).GetString());
            Assert.Equal("Погашение аванса без НДС", kits.Cell(5, 20).GetString());
            Assert.Equal(5, kits.SheetView.SplitRow);
            Assert.Equal(3, kits.SheetView.SplitColumn);
            Assert.Equal(XLBorderStyleValues.Thick, kits.Cell(4, 1).Style.Border.LeftBorder);
            Assert.Equal(XLBorderStyleValues.Thick, kits.Cell(4, 1).Style.Border.TopBorder);
            Assert.Equal(XLBorderStyleValues.Thick, kits.Cell(4, 3).Style.Border.LeftBorder);
            Assert.Equal(XLBorderStyleValues.Thick, kits.Cell(4, 6).Style.Border.RightBorder);
            Assert.Equal(XLBorderStyleValues.Thick, kits.Cell(4, 21).Style.Border.RightBorder);

            var dataRow = 6;
            Assert.Equal(XLBorderStyleValues.Thick, kits.Cell(dataRow, 3).Style.Border.LeftBorder);
            if (kits.Cell(dataRow, 20).DataType == XLDataType.Number)
                Assert.Equal("0,00", kits.Cell(dataRow, 20).Style.NumberFormat.Format);

            var findings = book.Worksheet("Замечания");
            Assert.Equal("Элемент", findings.Cell(4, 2).GetString());
            Assert.Equal(XLBorderStyleValues.Thick, findings.Cell(4, 1).Style.Border.TopBorder);

            var cipherStatus = kits.Cell(dataRow, 6);
            Assert.Equal("ОК", cipherStatus.GetString());
            Assert.Equal(SeverityPalette.Ok, Html(cipherStatus.Style.Fill.BackgroundColor));
            var overall = kits.Cell(dataRow, 21);
            Assert.Equal(SeverityPalette.For(SeverityFromRu(overall.GetString())), Html(overall.Style.Fill.BackgroundColor));
        }
        finally
        {
            dir.Delete(recursive: true);
        }
    }

    private static string Html(XLColor color)
    {
        var rgb = color.Color;
        return $"#{rgb.R:X2}{rgb.G:X2}{rgb.B:X2}";
    }

    private static Severity SeverityFromRu(string text) => text switch
    {
        "Критично" => Severity.Critical,
        "Оформление" => Severity.Formatting,
        "Предупреждение" => Severity.Warning,
        "ОК" => Severity.Ok,
        _ => throw new InvalidOperationException(text),
    };

    [Fact]
    public void Frozen_pane_is_stored_in_the_worksheet_xml()
    {
        var dir = Directory.CreateTempSubdirectory("proverkard-freeze");
        try
        {
            var reportPath = Path.Combine(dir.FullName, "report.xlsx");
            ReportWriter.Write(new CheckReport(), reportPath, "0.1.0");
            using var zip = ZipFile.OpenRead(reportPath);
            var xml = zip.Entries
                .Where(entry => entry.FullName.StartsWith("xl/worksheets/sheet", StringComparison.Ordinal))
                .Select(entry =>
                {
                    using var stream = entry.Open();
                    using var reader = new StreamReader(stream);
                    return reader.ReadToEnd();
                })
                .ToList();
            Assert.Contains(xml, text =>
                text.Contains("xSplit=\"3\"", StringComparison.Ordinal)
                && text.Contains("ySplit=\"5\"", StringComparison.Ordinal)
                && text.Contains("state=\"frozenSplit\"", StringComparison.Ordinal));
            using var styles = zip.GetEntry("xl/styles.xml")!.Open();
            using var reader = new StreamReader(styles);
            var stylesXml = reader.ReadToEnd();
            var fontsAt = stylesXml.IndexOf("<fonts", StringComparison.Ordinal);
            var fonts = fontsAt < 0 ? stylesXml[..800] : stylesXml.Substring(fontsAt, Math.Min(1800, stylesXml.Length - fontsAt));
            Assert.True(stylesXml.Contains("Segoe UI", StringComparison.Ordinal) && !stylesXml.Contains("Calibri", StringComparison.Ordinal), fonts);
        }
        finally
        {
            dir.Delete(recursive: true);
        }
    }

    [Fact]
    public void Status_names_match_the_shared_palette()
    {
        Assert.Equal("ОК", SeverityText.ToRu(Severity.Ok));
        Assert.Equal("Оформление", SeverityText.ToRu(Severity.Formatting));
        Assert.Equal("Предупреждение", SeverityText.ToRu(Severity.Warning));
        Assert.Equal("Критично", SeverityText.ToRu(Severity.Critical));
        Assert.Equal("#F4C7C3", SeverityPalette.For(Severity.Critical));
        Assert.Equal("#FFE08A", SeverityPalette.For(Severity.Formatting));
        Assert.Equal("#F8CBAD", SeverityPalette.For(Severity.Warning));
        Assert.Equal("#C6EFCE", SeverityPalette.For(Severity.Ok));
        Assert.Equal("Segoe UI", SeverityPalette.FontName);
    }

    [Fact]
    public void Signature_table_stays_quiet_when_kits_and_costs_were_parsed()
    {
        var dir = Directory.CreateTempSubdirectory("proverkard-act-quiet");
        var path = Path.Combine(dir.FullName, "РД 4.10.docx");
        try
        {
            using (var doc = WordprocessingDocument.Create(path, WordprocessingDocumentType.Document))
            {
                var main = doc.AddMainDocumentPart();
                main.Document = new Document(new Body(
                    new Paragraph(new Run(new Text("АКТ № 4.10"))),
                    Table(
                        ["№ тома", "Шифр", "Наименование документа"],
                        [["4.10.1.1.1 (4.10)", "100-20-3000-А-КМ1", "Конструкции"]]),
                    Table(
                        ["Наименование", "Стоимость без НДС", "НДС", "Стоимость с НДС"],
                        [["4.10.1.1.1 Рабочая документация. Сооружения", "1000", "220", "1220"]]),
                    Table(["Должность", "Подпись", "ФИО"], [["ГИП", "", "Фролов И.И."]])));
                main.Document.Save();
            }

            var act = ActParser.Parse(path);
            Assert.NotEmpty(act.Kits);
            Assert.NotEmpty(act.CostLines);
            Assert.DoesNotContain(act.Notices, notice => notice.Message.Contains("Таблица не разобрана"));
        }
        finally
        {
            dir.Delete(recursive: true);
        }
    }

    [Fact]
    public void Skipped_ledger_rows_name_empty_total_text_or_non_numeric()
    {
        var dir = Directory.CreateTempSubdirectory("proverkard-row-reason");
        try
        {
            var verd = Path.Combine(dir.FullName, "verd.xlsx");
            using (var book = new XLWorkbook())
            {
                var sheet = book.AddWorksheet("База");
                sheet.Cell(1, 1).Value = "Номер элемента";
                sheet.Cell(1, 2).Value = "Наименование элемента";
                sheet.Cell(1, 4).Value = "Шифр РД";
                sheet.Cell(1, 5).Value = "Наименование документации";
                sheet.Cell(2, 1).Value = "4.10";
                sheet.Cell(2, 2).Value = "Сооружения";
                sheet.Cell(2, 4).Value = "100-20-3000-А-КМ1";
                sheet.Cell(2, 5).Value = "Конструкции";
                sheet.Cell(3, 2).Value = "Итого по разделу";
                sheet.Cell(4, 2).Value = "см. примечание";
                sheet.Cell(5, 1).Value = "—";
                sheet.Cell(6, 4).Value = "12--34";
                book.SaveAs(verd);
            }

            var parsed = VerdParser.Parse(verd);
            Assert.Contains(parsed.RowIssues, notice => notice.Reason.Contains("строка-итог"));
            Assert.Contains(parsed.RowIssues, notice => notice.Reason.Contains("примечание или текст"));
            Assert.Contains(parsed.RowIssues, notice => notice.Reason.Contains("пустая строка"));
            Assert.Contains(parsed.RowIssues, notice => notice.Reason.Contains("нечисловое значение"));

            var vesr = Path.Combine(dir.FullName, "vesr.xlsx");
            using (var book = new XLWorkbook())
            {
                var sheet = book.AddWorksheet("ВЭ");
                sheet.Cell(1, 1).Value = "№ Элемента";
                sheet.Cell(1, 2).Value = "Наименование";
                sheet.Cell(1, 6).Value = "Всего";
                sheet.Cell(2, 1).Value = "4.10.1.1.1";
                sheet.Cell(2, 2).Value = "Рабочая документация";
                sheet.Cell(2, 6).Value = 10m;
                sheet.Cell(3, 2).Value = "Итого";
                sheet.Cell(4, 1).Value = "???";
                book.SaveAs(vesr);
            }

            var costs = VesrParser.Parse(vesr);
            Assert.Contains(costs.RowIssues, notice => notice.Reason.Contains("строка-итог"));
            Assert.Contains(costs.RowIssues, notice => notice.Reason.Contains("нечисловое значение"));
            Assert.True(costs.ByCode.ContainsKey(TextMatch.CodeKey("4.10.1.1.1")));
        }
        finally
        {
            dir.Delete(recursive: true);
        }
    }

    [Fact]
    public void Vesr_sum_notice_names_the_cell_value_and_the_reason()
    {
        var dir = Directory.CreateTempSubdirectory("proverkard-vesr-cell");
        try
        {
            var vesr = Path.Combine(dir.FullName, "vesr.xlsx");
            using (var book = new XLWorkbook())
            {
                var sheet = book.AddWorksheet("ВЭ");
                sheet.Cell(1, 1).Value = "№ Элемента";
                sheet.Cell(1, 2).Value = "Наименование";
                sheet.Cell(1, 6).Value = "Всего";
                sheet.Cell(2, 1).Value = "4.10.1.1.1";
                sheet.Cell(2, 2).Value = "Текст в сумме";
                sheet.Cell(2, 6).Value = "нечисло";
                sheet.Cell(3, 1).Value = "4.10.1.1.2";
                sheet.Cell(3, 2).Value = "Ошибка Excel";
                sheet.Cell(3, 6).Value = XLError.IncompatibleValue;
                sheet.Cell(4, 1).Value = "4.10.1.1.3";
                sheet.Cell(4, 2).Value = "Прочерк";
                sheet.Cell(4, 6).Value = "—";
                sheet.Cell(5, 6).Value = XLError.DivisionByZero;
                sheet.Cell(6, 1).Value = "4.10.1.1.4";
                sheet.Cell(6, 2).Value = "Пустая сумма";
                book.SaveAs(vesr);
            }

            var parsed = VesrParser.Parse(vesr);
            var text = parsed.ByCode[TextMatch.CodeKey("4.10.1.1.1")].SumProblem;
            Assert.Contains("Ячейка не разобрана как сумма", text);
            Assert.Contains("«нечисло»", text);
            Assert.Contains("текст", text);

            var error = parsed.ByCode[TextMatch.CodeKey("4.10.1.1.2")].SumProblem;
            Assert.Contains("«#ЗНАЧ!»", error);
            Assert.Contains("ошибка Excel", error);

            var blank = parsed.ByCode[TextMatch.CodeKey("4.10.1.1.3")].SumProblem;
            Assert.Contains("Пропущена", blank);
            Assert.DoesNotContain("не разобрана", blank);

            var uncoded = parsed.RowIssues.Single(notice => notice.Reason.Contains("F5"));
            Assert.Contains("без кода элемента", uncoded.Reason);
            Assert.Contains("«#ДЕЛ/0!»", uncoded.Reason);
            Assert.Contains("пропущена", uncoded.Reason);
            Assert.DoesNotContain("не разобрана", uncoded.Reason);

            var empty = parsed.ByCode[TextMatch.CodeKey("4.10.1.1.4")].SumProblem;
            Assert.Contains("пустая", empty);
            Assert.DoesNotContain("не разобрана", empty);
            Assert.Empty(parsed.Notices);
        }
        finally
        {
            dir.Delete(recursive: true);
        }
    }

    private static string Nest(string root, string fileName)
    {
        var current = root;
        var path = Path.Combine(current, fileName);
        while (path.Length <= 270)
        {
            current = Path.Combine(current, "папка_" + new string('б', 24));
            Directory.CreateDirectory(current);
            path = Path.Combine(current, fileName);
        }

        return path;
    }

    private static void WriteNoisyVesr(string path, int extraSkipped)
    {
        using var book = new XLWorkbook();
        var sheet = book.AddWorksheet("Данные");
        sheet.Cell(1, 1).Value = "№ Элемента";
        sheet.Cell(1, 2).Value = "Наименование";
        sheet.Cell(1, 3).Value = "Единица измерения";
        sheet.Cell(1, 4).Value = "Количество";
        sheet.Cell(1, 5).Value = "на единицу";
        sheet.Cell(1, 6).Value = "Всего";
        sheet.Cell(2, 1).Value = "1";
        sheet.Cell(2, 2).Value = "2";
        sheet.Cell(3, 1).Value = "4.10.1.1.1";
        sheet.Cell(3, 2).Value = "Рабочая документация. Сооружения";
        sheet.Cell(3, 6).Value = 1000m;
        sheet.Cell(4, 1).Value = "4.10.1.1.1";
        sheet.Cell(4, 2).Value = "повтор";
        sheet.Cell(4, 6).Value = 5m;
        sheet.Cell(5, 1).Value = "4.10.1.1.9";
        sheet.Cell(5, 2).Value = "кривая сумма";
        sheet.Cell(5, 6).Value = "нечисло";
        for (var i = 0; i < extraSkipped; i++)
        {
            sheet.Cell(6 + i, 1).Value = "примечание " + i;
            sheet.Cell(6 + i, 2).Value = "строка";
        }

        book.SaveAs(path);
    }

    private static void WriteBrokenAct(string path)
    {
        using var doc = WordprocessingDocument.Create(path, WordprocessingDocumentType.Document);
        var main = doc.AddMainDocumentPart();
        main.Document = new Document(new Body(
            new Paragraph(new Run(new Text("АКТ № 4.10"))),
            Table(["Служебное", "Поле"], [["да", "нет"]]),
            Table(
                ["Наименование", "Стоимость без НДС", "НДС", "Стоимость с НДС"],
                [["4.10.1.1.1 Рабочая документация. Сооружения", "криво", "220,00", "1 220,00"]])));
        main.Document.Save();
    }

    private static void WritePlainVerd(string path)
    {
        using var book = new XLWorkbook();
        var sheet = book.AddWorksheet("База");
        sheet.Cell(1, 1).Value = "Номер элемента";
        sheet.Cell(1, 2).Value = "Наименование элемента";
        sheet.Cell(1, 4).Value = "Шифр РД";
        sheet.Cell(1, 5).Value = "Наименование документации";
        sheet.Cell(1, 7).Value = "№ Элемента по ДГП";
        sheet.Cell(2, 1).Value = "4.10";
        sheet.Cell(2, 2).Value = "Сооружения";
        sheet.Cell(2, 4).Value = "100-20-3000-А-КМ1";
        sheet.Cell(2, 5).Value = "Конструкции";
        sheet.Cell(2, 7).Value = "4.10.1.1.1";
        book.SaveAs(path);
    }

    private static void WritePlainVesr(string path)
    {
        using var book = new XLWorkbook();
        var sheet = book.AddWorksheet("ВЭ");
        sheet.Cell(1, 1).Value = "№ Элемента";
        sheet.Cell(1, 2).Value = "Наименование";
        sheet.Cell(1, 5).Value = "на единицу";
        sheet.Cell(1, 6).Value = "Всего";
        sheet.Cell(2, 1).Value = "4.10.1.1.1";
        sheet.Cell(2, 2).Value = "Рабочая документация. Сооружения";
        sheet.Cell(2, 5).Value = 1000m;
        sheet.Cell(2, 6).Value = 1000m;
        book.SaveAs(path);
    }

    private static void WritePlainAct(string path)
    {
        using var doc = WordprocessingDocument.Create(path, WordprocessingDocumentType.Document);
        var main = doc.AddMainDocumentPart();
        main.Document = new Document(new Body(
            new Paragraph(new Run(new Text("АКТ № 4.10"))),
            new Paragraph(new Run(new Text("Приложение к акту сдачи-приёмки рабочей документации № 4.10. Расчет погашенной части аванса в размере 10 (десять) рублей 50 копеек без учета НДС."))),
            Table(
                ["№ тома", "Шифр", "Наименование документа"],
                [["4.10.1.1.1 (4.10)", "100-20-3000-А-КМ1", "Конструкции"]]),
            Table(
                ["Наименование", "Стоимость без НДС", "НДС", "Стоимость с НДС"],
                [["4.10.1.1.1 Рабочая документация. Сооружения", "1000", "220", "1220"]])));
        main.Document.Save();
    }

    private static Table Table(string[] headers, string[][] rows)
    {
        var table = new Table();
        table.Append(Row(headers));
        foreach (var row in rows)
            table.Append(Row(row));
        return table;
    }

    private static TableRow Row(IEnumerable<string> cells)
    {
        var row = new TableRow();
        foreach (var cell in cells)
            row.Append(new TableCell(new Paragraph(new Run(new Text(cell) { Space = SpaceProcessingModeValues.Preserve }))));
        return row;
    }

    private sealed class SyncProgress(List<CheckProgress> steps) : IProgress<CheckProgress>
    {
        public void Report(CheckProgress value) => steps.Add(value);
    }
}
