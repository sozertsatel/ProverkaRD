using ClosedXML.Excel;
using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Wordprocessing;

namespace ProverkaRD.Engine.Tests;

public class StageLabelTests
{
    private static readonly DateTime Day = new(2026, 10, 23);

    [Fact]
    public void File_name_wins_and_builds_a_windows_safe_report_name()
    {
        Assert.Equal("7", StageLabel.FromText("Этап 7 2026.10.13.xlsx"));
        Assert.Null(StageLabel.FromText("Этап 4.10 Сооружения"));
        Assert.Null(StageLabel.FromText("Этап 7.29.1"));
        Assert.Equal("6", StageLabel.FromText("Приёмка этапа № 6, комплект."));

        var picked = StageLabel.Pick(
            Path.Combine("папка", "Этап 7 2026.10.13.xlsx"),
            ["9"],
            "Этап 8");
        Assert.Equal("7", picked);
        Assert.Equal("РД Контроль — Этап 7 — 23.10.2026.xlsx", StageLabel.ReportFileName(picked, Day));
        Assert.Equal("РД Контроль — 23.10.2026.xlsx", StageLabel.ReportFileName("", Day));

        var unsafeName = StageLabel.ReportFileName("7:8|?", Day);
        Assert.Equal("РД Контроль — Этап 78 — 23.10.2026.xlsx", unsafeName);
        Assert.DoesNotContain(unsafeName, ch => ch is '<' or '>' or ':' or '"' or '/' or '\\' or '|' or '?' or '*' || ch < ' ');
    }

    [Fact]
    public void Check_takes_the_stage_from_the_verd_name_then_the_act_then_the_sheet()
    {
        var dir = Directory.CreateTempSubdirectory("proverkard-stage");
        try
        {
            var named = Path.Combine(dir.FullName, "Этап 7 2026.10.13.xlsx");
            var actNine = WriteAct(Path.Combine(dir.FullName, "РД 7.29.docx"), "Приёмка этапа 9.");
            var fromName = new CheckingEngine().Run(new CheckInput
            {
                VerdPath = named,
                ActPaths = [actNine],
            });
            Assert.Equal("7", fromName.Stage);

            var plain = Path.Combine(dir.FullName, "ведомость.xlsx");
            WriteVerd(plain, "Ведомость объёмов. Этап 8");
            var fromSheet = new CheckingEngine().Run(new CheckInput { VerdPath = plain });
            Assert.Equal("8", fromSheet.Stage);

            WriteVerd(plain, "");
            var actSix = WriteAct(Path.Combine(dir.FullName, "РД 8.01.docx"), "Акт сдачи-приёмки этапа № 6.");
            var fromAct = new CheckingEngine().Run(new CheckInput
            {
                VerdPath = plain,
                ActPaths = [actSix],
            });
            Assert.Equal("6", fromAct.Stage);

            var elementOnly = WriteAct(Path.Combine(dir.FullName, "приемка.docx"), "Этап 4.10 Сооружения");
            var none = new CheckingEngine().Run(new CheckInput
            {
                VerdPath = plain,
                ActPaths = [elementOnly],
            });
            Assert.Equal("", none.Stage);
            Assert.Equal("РД Контроль — 23.10.2026.xlsx", StageLabel.ReportFileName(none.Stage, Day));

            fromName.Stage = "7";
            var reportPath = Path.Combine(dir.FullName, "report.xlsx");
            ReportWriter.Write(fromName, reportPath, "0.1.7");
            using var book = new XLWorkbook(reportPath);
            Assert.Equal(
                "РД Контроль. Проверка рабочей документации. Этап 7, версия 0.1.7",
                book.Worksheet("Сводка").Cell(1, 1).GetString());
        }
        finally
        {
            dir.Delete(recursive: true);
        }
    }

    [Fact]
    public void Stage_comes_from_the_element_or_the_cipher_when_the_verd_name_has_none()
    {
        Assert.Null(StageLabel.FromText("Ведомость элементов РД 05.10.2026.xlsx"));
        Assert.Null(StageLabel.FromText("Этап 4.10 Сооружения"));
        Assert.Equal("6", StageLabel.FromElementNumber("6.65"));
        Assert.Equal("6", StageLabel.FromElementNumber("6.126"));
        Assert.Equal("6", StageLabel.FromElementNumber("6,65"));
        Assert.Null(StageLabel.FromElementNumber("Этап 6.65"));
        Assert.Equal("6", StageLabel.FromCipher("100-06-3000-А-КЖ1"));
        Assert.Equal("20", StageLabel.FromCipher("100-20-3000-А-АС"));
        Assert.Null(StageLabel.FromCipher("100-00-3000-А-КЖ1"));

        Assert.Equal("7", StageLabel.Pick("Этап 7.xlsx", ["6"], "Этап 8", ["4.10"], ["100-04-3000-А-КЖ1"]));
        Assert.Equal("6", StageLabel.Pick("ведомость.xlsx", ["6"], "Этап 8", ["7.29"], ["100-04-3000-А-КЖ1"]));
        Assert.Equal("6", StageLabel.Pick(
            "Ведомость элементов РД 05.10.2026.xlsx",
            [],
            "Этап 8",
            ["6.65", "6.126"],
            ["100-04-3000-А-КЖ1"]));
        Assert.Equal("6", StageLabel.Pick(null, [], null, [], ["100-06-3000-А-КЖ1"]));
        Assert.Equal("20", StageLabel.Pick(null, [], null, [], ["100-20-3000-А-АС"]));

        var dir = Directory.CreateTempSubdirectory("proverkard-stage-fallback");
        try
        {
            var verd = Path.Combine(dir.FullName, "Ведомость элементов РД 05.10.2026.xlsx");
            using (var book = new XLWorkbook())
            {
                var sheet = book.AddWorksheet("Лист1");
                sheet.Cell(1, 1).Value = "Ведомость объёмов. Этап 8";
                sheet.Cell(2, 1).Value = "Номер элемента";
                sheet.Cell(2, 2).Value = "Наименование элемента";
                sheet.Cell(2, 4).Value = "Шифр РД";
                sheet.Cell(2, 5).Value = "Наименование документации";
                sheet.Cell(2, 7).Value = "№ Элемента по ДГП";
                sheet.Cell(3, 1).Value = "6.65";
                book.SaveAs(verd);
            }

            var act = WriteAct(Path.Combine(dir.FullName, "РД 6.65.docx"), "АКТ № 6.65");
            var report = new CheckingEngine().Run(new CheckInput
            {
                VerdPath = verd,
                ActPaths = [act],
            });
            Assert.Equal("6", report.Stage);
            Assert.Equal("Лист «База» не найден, читается «Лист1».", report.VerdSheetNote);
            Assert.Contains(report.Notices, notice => notice.Message == report.VerdSheetNote);
            Assert.Equal("РД Контроль — Этап 6 — 23.10.2026.xlsx", StageLabel.ReportFileName(report.Stage, Day));

            var reportPath = Path.Combine(dir.FullName, "report.xlsx");
            ReportWriter.Write(report, reportPath, "0.1.9");
            using var written = new XLWorkbook(reportPath);
            var summary = written.Worksheet("Сводка");
            Assert.Equal(
                "РД Контроль. Проверка рабочей документации. Этап 6, версия 0.1.9",
                summary.Cell(1, 1).GetString());
            Assert.Contains("Этап 6.", summary.Cell(2, 1).GetString());
            Assert.Contains("Лист «База» не найден, читается «Лист1».", summary.Cell(2, 1).GetString());

            var cipherAct = WriteKitAct(Path.Combine(dir.FullName, "приемка.docx"), "100-06-3000-А-КЖ1");
            WriteVerd(Path.Combine(dir.FullName, "ведомость.xlsx"), "");
            var fromCipher = new CheckingEngine().Run(new CheckInput
            {
                VerdPath = Path.Combine(dir.FullName, "ведомость.xlsx"),
                ActPaths = [cipherAct],
            });
            Assert.Equal("6", fromCipher.Stage);
            Assert.Equal("", fromCipher.VerdSheetNote);
        }
        finally
        {
            dir.Delete(recursive: true);
        }
    }

    private static string WriteAct(string path, string paragraph)
    {
        using var doc = WordprocessingDocument.Create(path, WordprocessingDocumentType.Document);
        var main = doc.AddMainDocumentPart();
        main.Document = new Document(new Body(new Paragraph(new Run(new Text(paragraph)))));
        main.Document.Save();
        return path;
    }

    private static void WriteVerd(string path, string title)
    {
        using var book = new XLWorkbook();
        var sheet = book.AddWorksheet("База");
        if (title.Length > 0)
            sheet.Cell(1, 1).Value = title;
        var header = title.Length > 0 ? 2 : 1;
        sheet.Cell(header, 1).Value = "Номер элемента";
        sheet.Cell(header, 2).Value = "Наименование элемента";
        sheet.Cell(header, 4).Value = "Шифр РД";
        sheet.Cell(header, 5).Value = "Наименование документации";
        sheet.Cell(header, 7).Value = "№ Элемента по ДГП";
        book.SaveAs(path);
    }

    private static string WriteKitAct(string path, string cipher)
    {
        using var doc = WordprocessingDocument.Create(path, WordprocessingDocumentType.Document);
        var main = doc.AddMainDocumentPart();
        var table = new Table();
        table.Append(HeaderRow(["№ тома", "Шифр", "Наименование документа"]));
        table.Append(HeaderRow(["1", cipher, "Опоры"]));
        main.Document = new Document(new Body(
            new Paragraph(new Run(new Text("Акт сдачи-приёмки"))),
            table));
        main.Document.Save();
        return path;
    }

    private static TableRow HeaderRow(IEnumerable<string> cells)
    {
        var row = new TableRow();
        foreach (var cell in cells)
            row.Append(new TableCell(new Paragraph(new Run(new Text(cell)))));
        return row;
    }
}
