using System.IO.Compression;
using ClosedXML.Excel;
using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Wordprocessing;
using ProverkaRD.Engine.Parsing;

namespace ProverkaRD.Engine.Tests;

public class Stage6ReportTests
{
    [Fact]
    public void Zip_reading_is_off_by_default_and_identical_copies_are_read_once()
    {
        Assert.False(new CheckOptions().ReadZipArchives);

        var dir = Directory.CreateTempSubdirectory("proverkard-zip-off");
        try
        {
            var set = Directory.CreateDirectory(Path.Combine(dir.FullName, "set"));
            var again = Directory.CreateDirectory(Path.Combine(dir.FullName, "again"));
            var pdf = Path.Combine(set.FullName, "000-00-0000-А-НС1.pdf");
            var copy = Path.Combine(again.FullName, "000-00-0000-А-НС1.pdf");
            var bytes = "один и тот же комплект"u8.ToArray();
            File.WriteAllBytes(pdf, bytes);
            File.WriteAllBytes(copy, bytes);
            var zip = Path.Combine(set.FullName, "комплект.zip");
            using (var archive = ZipFile.Open(zip, ZipArchiveMode.Create))
            {
                var entry = archive.CreateEntry("000-00-0000-А-НС1.pdf");
                using var stream = entry.Open();
                stream.Write(bytes);
            }

            var act = Path.Combine(dir.FullName, "РД 7.29.docx");
            WriteAct(act, "7.29", "7.10.2.1.11.4", "000-00-0000-А-КМ9", "Сооружения");
            var report = new CheckingEngine().Run(new CheckInput
            {
                ActPaths = [act],
                RdPaths = [pdf, copy, zip],
                RdFolders = [set.FullName],
            });

            Assert.Equal(1, report.Load.Pdfs);
            Assert.Equal(1, report.Load.LoosePdfs);
            Assert.Contains("пропущено zip: 1", report.Load.Text);
            var skipped = Assert.Single(report.Notices, notice => notice.Place.StartsWith("Пропущено zip:", StringComparison.Ordinal));
            Assert.Equal("Пропущено zip: 1", skipped.Place);
            Assert.Equal("комплект.zip", skipped.Message);
            var reportPath = Path.Combine(dir.FullName, "report.xlsx");
            ReportWriter.Write(report, reportPath, "0.1.9");
            using (var book = new XLWorkbook(reportPath))
            {
                var gaps = book.Worksheet("Пропуски");
                Assert.Contains(gaps.RowsUsed(), row => row.Cell(2).GetString() == "Пропущено zip: 1" && row.Cell(3).GetString() == "комплект.zip");
            }
            Assert.Equal(1, report.Findings.Count(f => f.CheckName == "Комплект вне актов"));
            Assert.DoesNotContain(report.Findings, f => f.CheckName is "Шифр" or "Наименование комплекта" or "Наименование элемента" or "Номер элемента"
                && f.Kit.Contains("НС1", StringComparison.Ordinal));
        }
        finally
        {
            dir.Delete(recursive: true);
        }
    }

    [Fact]
    public void Zip_reading_on_still_keeps_one_copy_of_the_same_bytes()
    {
        var dir = Directory.CreateTempSubdirectory("proverkard-zip-on");
        try
        {
            var pdf = Path.Combine(dir.FullName, "000-00-0000-А-НС1.pdf");
            var bytes = "одинаковые байты комплекта"u8.ToArray();
            File.WriteAllBytes(pdf, bytes);
            var zip = Path.Combine(dir.FullName, "комплект.zip");
            using (var archive = ZipFile.Open(zip, ZipArchiveMode.Create))
            {
                var entry = archive.CreateEntry("вложенная/000-00-0000-А-НС1.pdf");
                using var stream = entry.Open();
                stream.Write(bytes);
            }

            var act = Path.Combine(dir.FullName, "РД 7.29.docx");
            WriteAct(act, "7.29", "7.10.2.1.11.4", "000-00-0000-А-КМ9", "Сооружения");
            var report = new CheckingEngine().Run(new CheckInput
            {
                ActPaths = [act],
                RdPaths = [pdf, zip],
                Options = new CheckOptions { ReadZipArchives = true },
            });

            Assert.Equal(1, report.Load.Pdfs);
            Assert.Equal(1, report.Findings.Count(f => f.CheckName == "Комплект вне актов"));
            Assert.DoesNotContain("пропущено zip:", report.Load.Text);
        }
        finally
        {
            dir.Delete(recursive: true);
        }
    }

    [Fact]
    public void Same_cipher_with_different_bytes_is_one_duplicate_remark()
    {
        var dir = Directory.CreateTempSubdirectory("proverkard-dup-cipher");
        try
        {
            var left = Path.Combine(dir.FullName, "левая", "000-00-0000-А-НС1.pdf");
            var right = Path.Combine(dir.FullName, "правая", "000-00-0000-А-НС1.pdf");
            Directory.CreateDirectory(Path.GetDirectoryName(left)!);
            Directory.CreateDirectory(Path.GetDirectoryName(right)!);
            File.WriteAllBytes(left, "первая копия"u8.ToArray());
            File.WriteAllBytes(right, "вторая копия, другие байты"u8.ToArray());

            var report = new CheckingEngine().Run(new CheckInput { RdPaths = [left, right] });
            var remark = Assert.Single(report.Findings, f => f.CheckName == "Дубль комплекта");
            Assert.Equal(Severity.Warning, remark.Severity);
            Assert.Contains("Дубль комплекта: 2 копий", remark.ValueA + remark.InAct + remark.Explanation);
            Assert.Contains(left, remark.Explanation);
            Assert.Contains(right, remark.Explanation);
        }
        finally
        {
            dir.Delete(recursive: true);
        }
    }

    [Fact]
    public void Equal_element_numbers_are_not_critical_and_a_real_mismatch_still_is()
    {
        var dir = Directory.CreateTempSubdirectory("proverkard-element-no");
        try
        {
            var verd = Path.Combine(dir.FullName, "verd.xlsx");
            var vesr = Path.Combine(dir.FullName, "vesr.xlsx");
            WriteVerd(verd, "6.65", "000-00-0000-А-НС1", "Мост на ПК");
            WriteVesr(vesr, "6.65.1.1.1");
            var act = Path.Combine(dir.FullName, "РД 6.65.docx");
            WriteAct(act, "6.65", "6.65.1.1.1", "000-00-0000-А-НС1", "Мост на ПК");
            var withoutNumber = Path.Combine(dir.FullName, "000-00-0000-А-НС1.docx");
            WriteRd(withoutNumber, "000-00-0000-А-НС1", "Мост на ПК", element: null, elementName: null);

            var agreed = new CheckingEngine().Run(new CheckInput
            {
                VerdPath = verd,
                VesrPath = vesr,
                ActPaths = [act],
                RdPaths = [withoutNumber],
            });
            Assert.DoesNotContain(agreed.Findings, f => f.CheckName == "Номер элемента" && f.Severity == Severity.Critical);
            var actRow = Assert.Single(agreed.Acts);
            if (actRow.CriticalCount == 0)
                Assert.NotEqual(Severity.Critical, actRow.Overall);

            var mismatch = Path.Combine(dir.FullName, "другой.docx");
            WriteRd(mismatch, "000-00-0000-А-НС1", "Мост на ПК", "6.66", "Мост на ПК");
            var differed = new CheckingEngine().Run(new CheckInput
            {
                VerdPath = verd,
                VesrPath = vesr,
                ActPaths = [act],
                RdPaths = [mismatch],
            });
            Assert.Contains(differed.Findings, f => f.CheckName == "Номер элемента" && f.Severity == Severity.Critical);
        }
        finally
        {
            dir.Delete(recursive: true);
        }
    }

    [Fact]
    public void Element_total_is_not_critical_when_there_are_no_critical_remarks()
    {
        var act = new ActResult
        {
            FileName = "РД 6.65.docx",
            ElementNo = "6.65",
            SourcePath = "РД 6.65.docx",
            NameStatus = Severity.Critical,
            CompletenessStatus = Severity.Warning,
        };
        var kit = new KitResult
        {
            ActName = "РД 6.65.docx",
            ElementNo = "6.65",
            SourcePath = "РД 6.65.docx",
            Overall = Severity.Critical,
        };
        var score = ActScore.Score(act, [], [kit]);
        Assert.Equal(0, score.Critical);
        Assert.NotEqual(Severity.Critical, score.Overall);
        Assert.Equal(Severity.Warning, score.Overall);

        var withRemark = ActScore.Score(act,
            [new Finding("РД 6.65.docx", "—", "Шифр", "а", "б", Severity.Critical, "", "6.65")],
            [kit]);
        Assert.Equal(1, withRemark.Critical);
        Assert.Equal(Severity.Critical, withRemark.Overall);
    }

    [Fact]
    public void Skipped_zips_are_one_grouped_notice()
    {
        var dir = Directory.CreateTempSubdirectory("proverkard-zips");
        try
        {
            var first = Path.Combine(dir.FullName, "первый.zip");
            var second = Path.Combine(dir.FullName, "второй.zip");
            using (var archive = ZipFile.Open(first, ZipArchiveMode.Create))
                archive.CreateEntry("пусто.txt");
            using (var archive = ZipFile.Open(second, ZipArchiveMode.Create))
                archive.CreateEntry("пусто.txt");

            var report = new CheckingEngine().Run(new CheckInput { RdPaths = [first, second] });
            var skipped = Assert.Single(report.Notices, notice => notice.Place.StartsWith("Пропущено zip:", StringComparison.Ordinal));
            Assert.Equal("Пропущено zip: 2", skipped.Place);
            Assert.Contains("первый.zip", skipped.Message);
            Assert.Contains("второй.zip", skipped.Message);
            Assert.Contains("пропущено zip: 2", report.Load.Text);
        }
        finally
        {
            dir.Delete(recursive: true);
        }
    }

    [Fact]
    public void Registry_lists_foreign_docx_and_pdf_but_not_a_real_docx_kit()
    {
        var dir = Directory.CreateTempSubdirectory("proverkard-foreign");
        try
        {
            var letter = Path.Combine(dir.FullName, "Письмо.docx");
            var note = Path.Combine(dir.FullName, "заметка.pdf");
            var kit = Path.Combine(dir.FullName, "100-20-3000-А-КЖ1.docx");
            using (var doc = WordprocessingDocument.Create(letter, WordprocessingDocumentType.Document))
            {
                var main = doc.AddMainDocumentPart();
                main.Document = new Document(new Body(new Paragraph(new Run(new Text("Сопроводительное письмо")))));
                main.Document.Save();
            }

            File.WriteAllBytes(note, []);
            WriteRd(kit, "100-20-3000-А-КЖ1", "Опоры", "6.65", "Сооружения");

            var report = new CheckingEngine().Run(new CheckInput { RdPaths = [letter, note, kit] });
            var foreign = report.Registry.Where(row => row.Foreign).Select(row => row.FileName).ToList();
            Assert.Equal(2, foreign.Count);
            Assert.Contains("Письмо.docx", foreign);
            Assert.Contains("заметка.pdf", foreign);
            Assert.DoesNotContain(report.Registry, row => row.FileName.Contains("КЖ1", StringComparison.Ordinal));

            var reportPath = Path.Combine(dir.FullName, "report.xlsx");
            ReportWriter.Write(report, reportPath, "0.1.9");
            using var book = new XLWorkbook(reportPath);
            var sheet = book.Worksheet("Опись комплекта");
            Assert.Equal("Файл", sheet.Cell(4, 5).GetString());
            Assert.Contains(sheet.CellsUsed(), cell => cell.GetString().Contains("Посторонних файлов: 2", StringComparison.Ordinal));
            var names = sheet.Column(5).CellsUsed().Select(cell => cell.GetString()).ToList();
            Assert.Contains("Письмо.docx", names);
            Assert.Contains("заметка.pdf", names);
            Assert.DoesNotContain(names, name => name.Contains("КЖ1", StringComparison.Ordinal));
        }
        finally
        {
            dir.Delete(recursive: true);
        }
    }

    [Fact]
    public void Spacing_and_a_glued_conjunction_are_formatting_a_missing_word_is_critical()
    {
        const string withNa = "Мост на ПК1+74,25";
        const string withoutNa = "Мост ПК1+74,25";
        Assert.Contains("на", TextMatch.Normalize(withNa), StringComparison.Ordinal);
        Assert.Contains("на", TextMatch.VisualKey(withNa), StringComparison.Ordinal);
        Assert.DoesNotContain("на", TextMatch.Normalize(withoutNa), StringComparison.Ordinal);

        Assert.Equal(Severity.Formatting, TextMatch.Compare("Мост  на ПК", "Мост на ПК"));
        Assert.Equal(Severity.Formatting, TextMatch.Compare("производственно- служебное", "производственно-служебное"));
        Assert.Equal(Severity.Formatting, TextMatch.Compare("зданияи", "здания и"));
        Assert.Equal(Severity.Formatting, TextMatch.Compare("здания и", "зданияи"));
        Assert.Equal(Severity.Formatting, TextMatch.Compare("Мост, ПК1+74,25", "Мост ПК1+74,25"));
        Assert.Equal(Severity.Critical, TextMatch.Compare("Мост ПК 10", "Мост на ПК 10"));
        Assert.Equal(Severity.Critical, TextMatch.Compare("Мост на ПК 10", "Мост ПК 10"));
        Assert.Equal(Severity.Critical, TextMatch.Compare(withNa, withoutNa));
        Assert.Equal(Severity.Critical, TextMatch.Compare(withoutNa, withNa));
        Assert.Equal(Severity.Critical, TextMatch.Compare("Мост в ПК", "Мост ПК"));
        Assert.Equal(Severity.Formatting, TextMatch.Compare("соружения", "сооружения"));
    }

    [Fact]
    public void Missing_short_word_in_the_element_name_is_critical()
    {
        // В прогоне этапа 6 замечание 6.127 по «Наименование элемента» было «Оформление», а 6.126 — «Критично».
        // Исходных строк 6.127 в репозитории нет. Нормализация слово «на» не выбрасывает:
        // «Мост на ПК1+74,25» и «Мост ПК1+74,25» — разные слова, значит «Критично».
        // «Оформление» остаётся, только если слова совпали: пробелы, знаки, приклеенное «и» или опечатка в 1–2 буквы.
        var dir = Directory.CreateTempSubdirectory("proverkard-na");
        try
        {
            var verd = Path.Combine(dir.FullName, "Ведомость элементов РД 05.10.2026.xlsx");
            using (var book = new XLWorkbook())
            {
                var sheet = book.AddWorksheet("База");
                sheet.Cell(1, 1).Value = "Номер элемента";
                sheet.Cell(1, 2).Value = "Наименование элемента";
                sheet.Cell(1, 4).Value = "Шифр РД";
                sheet.Cell(1, 5).Value = "Наименование документации";
                sheet.Cell(1, 7).Value = "№ Элемента по ДГП";
                sheet.Cell(2, 1).Value = "6.127";
                sheet.Cell(2, 2).Value = "Мост ПК1+74,25";
                sheet.Cell(2, 4).Value = "100-20-3000-А-КЖ1";
                sheet.Cell(2, 5).Value = "Комплект опор";
                sheet.Cell(2, 7).Value = "6.127.1.1.1";
                book.SaveAs(verd);
            }

            var act = Path.Combine(dir.FullName, "РД 6.127.docx");
            using (var doc = WordprocessingDocument.Create(act, WordprocessingDocumentType.Document))
            {
                var main = doc.AddMainDocumentPart();
                main.Document = new Document(new Body(
                    new Paragraph(new Run(new Text("АКТ № 6.127"))),
                    Table(
                        ["№ тома", "Шифр", "Наименование документа"],
                        [["6.127.1.1.1 (6.127)", "100-20-3000-А-КЖ1", "Комплект опор"]]),
                    Table(
                        ["Наименование", "Ед. изм.", "Стоимость без НДС", "НДС", "Стоимость с НДС"],
                        [["6.127.1.1.1 Рабочая документация. Мост на ПК1+74,25", "шт", "1000", "220", "1220"]])));
                main.Document.Save();
            }

            var report = new CheckingEngine().Run(new CheckInput
            {
                VerdPath = verd,
                ActPaths = [act],
            });
            var element = Assert.Single(report.Findings, finding => finding.CheckName == "Наименование элемента");
            Assert.Equal(Severity.Critical, element.Severity);
            Assert.Contains("Мост на ПК1+74,25", element.InAct + element.ValueA + element.ValueB);
            Assert.Contains("Мост ПК1+74,25", element.InVerd + element.ValueA + element.ValueB);
            Assert.DoesNotContain(report.Findings, finding => finding.CheckName == "Наименование комплекта");
        }
        finally
        {
            dir.Delete(recursive: true);
        }
    }

    [Fact]
    public void Stamp_words_vedomosti_and_fasad_are_not_surnames()
    {
        var surnames = RdParser.CollectSurnames(
        [
            new RdParser.RdPage(
                1,
                "Фасад в осях\nВедомости отделки\nФасад А.А.\nВедомости Б.Б.\nРазраб. Фролов А.А.",
                "Фасад\nВедомости",
                CoordinateCells: ["Фасад", "Ведомости", "Белкина И.И."]),
        ]);
        Assert.Contains(surnames, name => name.Contains("Фролов", StringComparison.Ordinal));
        Assert.Contains(surnames, name => name.Contains("Белкина", StringComparison.Ordinal));
        Assert.DoesNotContain(surnames, name => name.Contains("Фасад", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(surnames, name => name.Contains("Ведомости", StringComparison.OrdinalIgnoreCase));
    }

    private static void WriteVerd(string path, string element, string cipher, string name)
    {
        using var book = new XLWorkbook();
        var sheet = book.AddWorksheet("База");
        sheet.Cell(1, 1).Value = "Номер элемента";
        sheet.Cell(1, 2).Value = "Наименование элемента";
        sheet.Cell(1, 4).Value = "Шифр РД";
        sheet.Cell(1, 5).Value = "Наименование документации";
        sheet.Cell(1, 7).Value = "№ Элемента по ДГП";
        sheet.Cell(2, 1).Value = element;
        sheet.Cell(2, 2).Value = name;
        sheet.Cell(2, 4).Value = cipher;
        sheet.Cell(2, 5).Value = name;
        sheet.Cell(2, 7).Value = "6.65.1.1.1";
        book.SaveAs(path);
    }

    private static void WriteVesr(string path, string code)
    {
        using var book = new XLWorkbook();
        var sheet = book.AddWorksheet("ВЭ");
        sheet.Cell(1, 1).Value = "№ Элемента";
        sheet.Cell(1, 2).Value = "Наименование";
        sheet.Cell(1, 3).Value = "Единица измерения";
        sheet.Cell(1, 4).Value = "Количество";
        sheet.Cell(1, 5).Value = "на единицу";
        sheet.Cell(1, 6).Value = "Всего";
        sheet.Cell(2, 1).Value = code;
        sheet.Cell(2, 2).Value = "Рабочая документация. Мост на ПК";
        sheet.Cell(2, 3).Value = "шт";
        sheet.Cell(2, 4).Value = 1;
        sheet.Cell(2, 5).Value = 1000m;
        sheet.Cell(2, 6).Value = 1000m;
        book.SaveAs(path);
    }

    private static void WriteAct(string path, string element, string code, string cipher, string kitName)
    {
        using var doc = WordprocessingDocument.Create(path, WordprocessingDocumentType.Document);
        var main = doc.AddMainDocumentPart();
        main.Document = new Document(new Body(
            new Paragraph(new Run(new Text("АКТ № " + element))),
            new Paragraph(new Run(new Text(ActMoneyText.DigitsSentence(1220m, 220m)))),
            Table(
                ["№ тома", "Шифр", "Наименование документа"],
                [[code + " (" + element + ")", cipher, kitName]]),
            Table(
                ["Наименование", "Ед. изм.", "Стоимость без НДС", "НДС", "Стоимость с НДС"],
                [[code + " Рабочая документация. " + kitName, "шт", "1000", "220", "1220"]])));
        main.Document.Save();
    }

    private static void WriteRd(string path, string cipher, string kitName, string? element, string? elementName)
    {
        using var doc = WordprocessingDocument.Create(path, WordprocessingDocumentType.Document);
        var main = doc.AddMainDocumentPart();
        var body = new Body(
            new Paragraph(new Run(new Text("РАБОЧАЯ ДОКУМЕНТАЦИЯ"))),
            new Paragraph(new Run(new Text(kitName))),
            new Paragraph(new Run(new Text(cipher))),
            new Paragraph(new Run(new Text("Разраб. Фролов А.А."))));
        if (!string.IsNullOrWhiteSpace(element))
            body.Append(new Paragraph(new Run(new Text("Этап " + element + " " + (elementName ?? "")))));
        main.Document = new Document(body);
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
