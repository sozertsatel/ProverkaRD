using ClosedXML.Excel;
using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Wordprocessing;

namespace ProverkaRD.Engine.Tests;

public class LedgerScopeTests
{
    [Fact]
    public void Only_act_rows_stay_in_skips_and_a_bad_sum_is_a_remark()
    {
        var dir = Directory.CreateTempSubdirectory("proverkard-scope");
        try
        {
            var verd = Path.Combine(dir.FullName, "verd.xlsx");
            var vesr = Path.Combine(dir.FullName, "vesr.xlsx");
            var act = Path.Combine(dir.FullName, "РД 7.29.docx");
            WriteVerd(verd);
            WriteVesr(vesr);
            WriteAct(act, "7.29", "7.10.2.1.5.20", "000-00-0000-А-НС1", "Насосная станция");

            var report = new CheckingEngine().Run(new CheckInput
            {
                VerdPath = verd,
                VesrPath = vesr,
                ActPaths = [act],
            });

            var grouped = Assert.Single(report.Notices, notice => notice.Source == "ВЭРД" && notice.Place.StartsWith("строки ", StringComparison.Ordinal));
            Assert.Equal("строки 4–23 (20)", grouped.Place);
            Assert.Contains("примечание", grouped.Message);
            Assert.DoesNotContain(report.Notices, notice => notice.Message.Contains("8.01") || notice.Place.Contains('3') && notice.Source == "ВЭРД" && notice.Place.Contains('2') && notice.Place.Contains("24"));
            Assert.DoesNotContain(report.Notices, notice => notice.Source == "ВЭСР" && notice.Message.Contains("пуст"));
            Assert.DoesNotContain(report.Notices, notice => notice.Message.Contains("Чужая строка"));

            var sum = Assert.Single(report.Findings, finding => finding.CheckName == "Сумма ВЭСР");
            Assert.Equal(Severity.Critical, sum.Severity);
            Assert.Contains("7.29", sum.ElementNo);
            Assert.Contains("пуст", sum.Explanation);

            var name = Assert.Single(report.Findings, finding => finding.CheckName == "Наименование стоимости");
            Assert.Contains("в ВЭСР этот текст стоит под номером 9.1.1.1.1", name.Explanation);
        }
        finally
        {
            dir.Delete(recursive: true);
        }
    }

    [Fact]
    public void Missing_code_points_at_the_vesr_row_with_the_same_text()
    {
        var dir = Directory.CreateTempSubdirectory("proverkard-name-hint");
        try
        {
            var verd = Path.Combine(dir.FullName, "verd.xlsx");
            var vesr = Path.Combine(dir.FullName, "vesr.xlsx");
            var act = Path.Combine(dir.FullName, "РД 7.29.docx");
            using (var book = new XLWorkbook())
            {
                var sheet = book.AddWorksheet("База");
                sheet.Cell(1, 1).Value = "Номер элемента";
                sheet.Cell(1, 4).Value = "Шифр РД";
                sheet.Cell(1, 7).Value = "№ Элемента по ДГП";
                sheet.Cell(2, 1).Value = "7.29";
                sheet.Cell(2, 2).Value = "Насосная станция";
                sheet.Cell(2, 4).Value = "000-00-0000-А-НС1";
                sheet.Cell(2, 7).Value = "8.8.8.8.8";
                book.SaveAs(verd);
            }

            using (var book = new XLWorkbook())
            {
                var sheet = book.AddWorksheet("ВЭ");
                sheet.Cell(1, 1).Value = "№ Элемента";
                sheet.Cell(1, 2).Value = "Наименование конструктивных решений (элементов), комплексов (видов) работ";
                sheet.Cell(1, 6).Value = "Всего";
                sheet.Cell(2, 1).Value = "9.1.1.1.1";
                sheet.Cell(2, 2).Value = "Насосная станция";
                sheet.Cell(2, 6).Value = 1000m;
                book.SaveAs(vesr);
            }

            WriteAct(act, "7.29", "7.10.2.1.5.20", "000-00-0000-А-НС1", "Насосная станция");
            var report = new CheckingEngine().Run(new CheckInput
            {
                VerdPath = verd,
                VesrPath = vesr,
                ActPaths = [act],
            });

            var code = Assert.Single(report.Findings, finding => finding.CheckName == "Код ДГП в ВЭСР");
            Assert.Contains("в ВЭСР этот текст стоит под номером 9.1.1.1.1", code.Explanation);
        }
        finally
        {
            dir.Delete(recursive: true);
        }
    }

    [Fact]
    public void Preview_counts_pdfs_by_element_folder_or_cipher()
    {
        var dir = Directory.CreateTempSubdirectory("proverkard-preview");
        try
        {
            var folder = Path.Combine(dir.FullName, "7.29");
            var other = Path.Combine(dir.FullName, "7.40");
            var loose = Path.Combine(dir.FullName, "разное");
            Directory.CreateDirectory(folder);
            Directory.CreateDirectory(other);
            Directory.CreateDirectory(loose);
            File.WriteAllBytes(Path.Combine(folder, "том.pdf"), "a"u8.ToArray());
            File.WriteAllBytes(Path.Combine(folder, "ещё.pdf"), "b"u8.ToArray());
            File.WriteAllBytes(Path.Combine(other, "чужой.pdf"), "c"u8.ToArray());
            File.WriteAllBytes(Path.Combine(loose, "000-00-0000-А-НС1.pdf"), "d"u8.ToArray());
            File.WriteAllBytes(Path.Combine(loose, "письмо.pdf"), "e"u8.ToArray());

            var act = new ActDocument
            {
                FilePath = Path.Combine(dir.FullName, "РД 7.29.docx"),
                ElementNoFromFileName = "7.29",
                Kits = [new ActKit { Cipher = "000-00-0000-А-НС1", ElementNo = "7.29" }],
            };
            var empty = new ActDocument
            {
                FilePath = Path.Combine(dir.FullName, "РД 7.30.docx"),
                ElementNoFromFileName = "7.30",
            };

            var preview = ActSetPreview.Build([act, empty], [dir.FullName]);
            var found = Assert.Single(preview.Lines, line => line.Element == "7.29");
            Assert.Equal(3, found.PdfCount);
            Assert.False(found.Missing);
            Assert.Contains("3 PDF", found.Text);
            var missing = Assert.Single(preview.Lines, line => line.Element == "7.30");
            Assert.True(missing.Missing);
            Assert.Contains("комплекты не добавлены", missing.Text);
            Assert.Contains(preview.Unmatched, line => line.Contains("7.40") && line.Contains("нет акта"));
            Assert.Contains(preview.Unmatched, line => line.Contains("письмо.pdf"));
            Assert.DoesNotContain(preview.Unmatched, line => line.Contains("том.pdf") || line.Contains("НС1"));
        }
        finally
        {
            dir.Delete(recursive: true);
        }
    }

    [Fact]
    public void Opis_sheet_fits_the_page_and_repeats_the_header()
    {
        var dir = Directory.CreateTempSubdirectory("proverkard-opis");
        try
        {
            var report = new CheckReport();
            report.Registry.Add(new RegistryRow
            {
                Element = "7.29",
                Cipher = "000-00-0000-А-НС1",
                Name = "Насосная",
                FileName = "том.pdf",
                SignatureName = "том.pdf.sig",
                HasSignature = true,
                Signers = "Фролов Иван",
            });
            report.Registry.Add(new RegistryRow
            {
                Element = "7.30",
                Cipher = "000-00-0000-А-НС2",
                Name = "Без подписи",
                FileName = "пусто.pdf",
                SignatureName = "нет",
                HasSignature = false,
            });
            var path = Path.Combine(dir.FullName, "report.xlsx");
            ReportWriter.Write(report, path, "0.1.5");
            using var book = new XLWorkbook(path);
            var sheet = book.Worksheet("Реестр комплекта");
            Assert.Equal("№ п/п", sheet.Cell(4, 1).GetString());
            Assert.Equal("Подписанты", sheet.Cell(4, 7).GetString());
            Assert.DoesNotContain(
                Enumerable.Range(1, 7).Select(col => sheet.Cell(4, col).GetString()),
                header => header is "Сертификат" or "Итог");
            Assert.Equal("Итого", sheet.Cell(7, 1).GetString());
            Assert.Equal("Комплектов: 2", sheet.Cell(7, 5).GetString());
            Assert.Equal("Подписано: 1", sheet.Cell(7, 6).GetString());
            Assert.Equal(1, sheet.PageSetup.PagesWide);
            using var zip = System.IO.Compression.ZipFile.OpenRead(path);
            var workbook = zip.GetEntry("xl/workbook.xml");
            Assert.NotNull(workbook);
            using var reader = new StreamReader(workbook.Open());
            var xml = reader.ReadToEnd();
            Assert.Contains("_xlnm.Print_Titles", xml);
            Assert.Contains("'Реестр комплекта'!4:4", xml);
        }
        finally
        {
            dir.Delete(recursive: true);
        }
    }

    private static void WriteVerd(string path)
    {
        using var book = new XLWorkbook();
        var sheet = book.AddWorksheet("База");
        sheet.Cell(1, 1).Value = "Номер элемента";
        sheet.Cell(1, 2).Value = "Наименование элемента";
        sheet.Cell(1, 4).Value = "Шифр РД";
        sheet.Cell(1, 5).Value = "Наименование документации";
        sheet.Cell(1, 7).Value = "№ Элемента по ДГП";
        sheet.Cell(2, 1).Value = "7.29";
        sheet.Cell(2, 2).Value = "Насосная станция";
        sheet.Cell(2, 4).Value = "000-00-0000-А-НС1";
        sheet.Cell(2, 5).Value = "Насосная";
        sheet.Cell(2, 7).Value = "7.10.2.1.5.20";
        sheet.Cell(3, 4).Value = "000-00-0000-А-НС2";
        sheet.Cell(3, 5).Value = "Второй комплект";
        for (var row = 4; row <= 23; row++)
            sheet.Cell(row, 2).Value = "см. примечание " + row;
        sheet.Cell(24, 1).Value = "8.01";
        sheet.Cell(24, 2).Value = "Чужой элемент";
        sheet.Cell(24, 4).Value = "000-00-0000-А-ЧЖ1";
        for (var row = 25; row <= 30; row++)
            sheet.Cell(row, 2).Value = "см. примечание чужое " + row;
        book.SaveAs(path);
    }

    private static void WriteVesr(string path)
    {
        using var book = new XLWorkbook();
        var sheet = book.AddWorksheet("ВЭ");
        sheet.Cell(1, 1).Value = "№ Элемента";
        sheet.Cell(1, 2).Value = "Наименование конструктивных решений (элементов), комплексов (видов) работ";
        sheet.Cell(1, 6).Value = "Всего";
        sheet.Cell(2, 1).Value = "7.10.2.1.5.20";
        sheet.Cell(2, 2).Value = "Другой объект";
        sheet.Cell(3, 1).Value = "9.1.1.1.1";
        sheet.Cell(3, 2).Value = "Насосная станция";
        sheet.Cell(3, 6).Value = 1000m;
        for (var i = 0; i < 30; i++)
        {
            sheet.Cell(4 + i, 1).Value = "1.2.3.4." + (i + 1);
            sheet.Cell(4 + i, 2).Value = "Чужая строка " + i;
        }

        book.SaveAs(path);
    }

    private static void WriteAct(string path, string element, string code, string cipher, string costName)
    {
        using var doc = WordprocessingDocument.Create(path, WordprocessingDocumentType.Document);
        var main = doc.AddMainDocumentPart();
        main.Document = new Document(new Body(
            new Paragraph(new Run(new Text("АКТ № " + element))),
            Table(
                ["№ тома", "Шифр", "Наименование документа"],
                [[code + " (" + element + ")", cipher, "Комплект"]]),
            Table(
                ["Наименование", "Стоимость без НДС", "НДС", "Стоимость с НДС"],
                [[code + " " + costName, "1000", "220", "1220"]])));
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
}
