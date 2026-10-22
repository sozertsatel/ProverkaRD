using ClosedXML.Excel;
using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Wordprocessing;
using ProverkaRD.Engine.Parsing;

namespace ProverkaRD.Engine.Tests;

public class EngineRulesTests
{
    [Fact]
    public void Act_overall_is_the_worst_finding_and_kit_status()
    {
        var money = new ActResult
        {
            FileName = "РД 7.46",
            ElementNo = "7.46",
            NetStatus = Severity.Ok,
            CompletenessStatus = Severity.Ok,
        };
        var findings = new[]
        {
            new Finding("РД 7.46", "—", "Стоимость без НДС", "70 000,00", "100 000,00", Severity.Critical, "", "7.46"),
        };
        var moneyScore = ActScore.Score(money, findings, []);
        Assert.Equal(Severity.Critical, moneyScore.Overall);
        Assert.Equal(1, moneyScore.Critical);

        var kits = new ActResult
        {
            FileName = "РД 7.29",
            ElementNo = "7.29",
            CompletenessStatus = Severity.Ok,
        };
        var kitRows = new[]
        {
            new KitResult { ActName = "РД 7.29", ElementNo = "7.29", Cipher = "960-07-1529-А-1.60-07-ТМ2", Overall = Severity.Critical },
        };
        Assert.Equal(Severity.Critical, ActScore.Score(kits, [], kitRows).Overall);

        var clean = new ActResult { FileName = "РД 7.01", ElementNo = "7.01" };
        Assert.Equal(Severity.Ok, ActScore.Score(clean, [], []).Overall);
    }

    [Fact]
    public void Real_run_marks_money_and_completeness_critical_and_leaves_a_clean_act_ok()
    {
        var dir = Directory.CreateTempSubdirectory("proverkard-rules");
        try
        {
            var verd = Path.Combine(dir.FullName, "verd.xlsx");
            var vesr = Path.Combine(dir.FullName, "vesr.xlsx");
            WriteVerd(verd);
            WriteVesr(vesr);
            WriteAct(Path.Combine(dir.FullName, "РД 7.46.docx"), "7.46", "7.10.2.1.11.3", "960-07-1529-Д-ИС21-27-КЖ1", 70000m);
            WriteAct(Path.Combine(dir.FullName, "РД 7.29.docx"), "7.29", "7.10.2.1.11.4", "960-07-1529-А-1.60-07-ТМ2", 1000m);
            WriteAct(Path.Combine(dir.FullName, "РД 7.01.docx"), "7.01", "7.10.2.1.11.5", "960-07-1529-А-1.60-07-ТМ8", 1000m);

            var report = new CheckingEngine().Run(new CheckInput
            {
                VerdPath = verd,
                VesrPath = vesr,
                ActPaths = Directory.GetFiles(dir.FullName, "*.docx"),
                RdPaths = [Path.Combine(dir.FullName, "ГСН.СВОР.pdf"), Path.Combine(dir.FullName, "смета_ИЗМ.xlsx")],
            });

            var act46 = report.Acts.Single(a => a.ElementNo == "7.46");
            var act29 = report.Acts.Single(a => a.ElementNo == "7.29");
            var act01 = report.Acts.Single(a => a.ElementNo == "7.01");
            Assert.Equal(Severity.Critical, act46.Overall);
            Assert.True(act46.CriticalCount > 0);
            Assert.Contains(report.Findings, f => f.ElementNo.Contains("7.46") && f.CheckName == "Стоимость без НДС" && f.Severity == Severity.Critical);
            Assert.Equal(Severity.Critical, act29.Overall);
            Assert.Equal(Severity.Critical, act29.CompletenessStatus);
            Assert.Equal(Severity.Ok, act01.Overall);
            Assert.DoesNotContain(report.Findings, f => f.CheckName.Contains("Чтение комплекта"));
            Assert.DoesNotContain(report.Kits, k => k.RdFileName.Contains("СВОР", StringComparison.OrdinalIgnoreCase));

            var reportPath = Path.Combine(dir.FullName, "report.xlsx");
            ReportWriter.Write(report, reportPath, "0.0.2");
            using var book = new XLWorkbook(reportPath);
            var summary = book.Worksheet("Сводка");
            Assert.Equal("Элемент", summary.Cell(5, 1).GetString());
            Assert.Equal("Итог", summary.Cell(5, 2).GetString());
            Assert.Equal("Критично", summary.Cell(5, 3).GetString());
            Assert.Equal("Комплектность", summary.Cell(5, 6).GetString());
            var findings = book.Worksheet("Замечания");
            Assert.Equal("Элемент", findings.Cell(4, 2).GetString());
            Assert.Equal("В акте", findings.Cell(4, 5).GetString());
            Assert.Equal("В РД", findings.Cell(4, 6).GetString());
            Assert.Equal("В ВЭРД", findings.Cell(4, 7).GetString());
            Assert.Equal("В ВЭСР", findings.Cell(4, 8).GetString());
            Assert.DoesNotContain(findings.Row(4).Cells().Select(c => c.GetString()), h => h.Contains("Значение"));

            var moneyRow = Enumerable.Range(5, findings.LastRowUsed()!.RowNumber())
                .First(r => findings.Cell(r, 4).GetString() == "Стоимость без НДС" && findings.Cell(r, 2).GetString().Contains("7.46"));
            Assert.Equal(XLDataType.Number, findings.Cell(moneyRow, 5).DataType);
            Assert.Equal(70000m, findings.Cell(moneyRow, 5).GetValue<decimal>());
        }
        finally
        {
            dir.Delete(recursive: true);
        }
    }

    [Theory]
    [InlineData("ПК01+91,66 Конструкции", "ПК01+91,66. Конструкции")]
    [InlineData("ПК6013+82", "ПК 6013+82")]
    [InlineData("7.10.2.1.11.3.", "7.10.2.1.11.3")]
    public void Punctuation_and_spacing_are_formatting(string left, string right)
    {
        Assert.Equal(Severity.Formatting, TextMatch.Compare(left, right));
        Assert.NotEqual(Severity.Ok, TextMatch.Compare(left, right));
    }

    [Fact]
    public void Dotted_cipher_is_not_cut_at_the_dot()
    {
        const string cipher = "960-07-1529-А-1.60-07-ТМ2";
        Assert.Equal(cipher, RdParser.FindCipher("Комплект " + cipher + ". Лист 1"));
        Assert.Equal("960-07-1529-Д-ИС21-27-КЖ1", RdParser.FindCipher("960-07-1529-Д-ИС21-27-КЖ1"));
    }

    [Fact]
    public void Surnames_come_from_role_lines_and_match_by_stem()
    {
        var text = """
            Общие данные
            Тепловая сеть
            Телефон 12-34
            Разработал И.И. Сидоров
            Пров. Краснощеком А.Б.
            ГИП Павлову В.Г.
            Н.контр. Янков Я.Я.
            """;
        var surnames = RdParser.ExtractSurnames(text);
        Assert.DoesNotContain(surnames, s => s.Contains("Общие", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(surnames, s => s.Contains("Тепловая", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(surnames, s => s.Contains("Телефон", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(surnames, s => s.Contains("Разработал", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(surnames, s => s.Contains("Сидоров", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(surnames, s => s.Contains("Краснощеком", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(surnames, s => s.Contains("Павлову", StringComparison.OrdinalIgnoreCase));
        Assert.True(SurnameStem.Same("Павлову", "Павлов"));
        Assert.True(SurnameStem.Same("Краснощеком", "Краснощек"));
        Assert.False(SurnameStem.Same("Сидоров", "Янков"));
    }

    [Fact]
    public void Auxiliary_gsn_names_are_not_kits()
    {
        Assert.True(RdFileRules.IsAuxiliary(@"C:\rd\ГСН.СВОР.pdf"));
        Assert.True(RdFileRules.IsAuxiliary(@"C:\rd\ведомость_ИЗМ.xlsx"));
        Assert.False(RdFileRules.IsAuxiliary(@"C:\rd\960-07-1529-А-1.60-07-ТМ2.pdf"));
    }

    [Fact]
    public void Signer_warnings_and_identical_findings_collapse_to_one_row()
    {
        var findings = new List<Finding>();
        for (var i = 0; i < 4; i++)
        {
            findings.Add(new Finding("Акт " + i, "КЖ" + i, FindingGrouper.SignerMissing, "Гирсова", "список", Severity.Warning, "длинно", "7." + (30 + i)));
            findings.Add(new Finding("Акт " + i, "КЖ" + i, "Наименование элемента: РД и ВЭРД", "сооружения", "соружения", Severity.Critical, "пояснение", "7.31"));
        }

        var grouped = FindingGrouper.Apply(findings, new CheckOptions());
        var signers = grouped.Where(f => f.CheckName == FindingGrouper.SignerMissing).ToList();
        var typos = grouped.Where(f => f.ValueB.Contains("соружения")).ToList();
        Assert.Single(signers);
        Assert.Contains("КЖ0", signers[0].Places);
        Assert.Contains("КЖ3", signers[0].Places);
        Assert.Single(typos);
        Assert.Contains("КЖ0", typos[0].Places);
        Assert.Equal(4, typos[0].Locations.Count);
    }

    private static void WriteVerd(string path)
    {
        using var book = new XLWorkbook();
        var sheet = book.AddWorksheet("База");
        sheet.Cell(1, 1).Value = "Номер элемента";
        sheet.Cell(1, 2).Value = "Наименование элемента";
        sheet.Cell(1, 3).Value = "Шифр ПД";
        sheet.Cell(1, 4).Value = "Шифр РД";
        sheet.Cell(1, 5).Value = "Наименование документации";
        sheet.Cell(1, 7).Value = "№ Элемента по ДГП";
        WriteVerdRow(sheet, 2, "7.46", "7.10.2.1.11.3", "960-07-1529-Д-ИС21-27-КЖ1");
        WriteVerdRow(sheet, 3, "7.29", "7.10.2.1.11.4", "960-07-1529-А-1.60-07-ТМ3");
        WriteVerdRow(sheet, 4, "7.01", "7.10.2.1.11.5", "960-07-1529-А-1.60-07-ТМ8");
        book.SaveAs(path);
    }

    private static void WriteVerdRow(IXLWorksheet sheet, int row, string element, string code, string cipher)
    {
        sheet.Cell(row, 1).Value = element;
        sheet.Cell(row, 2).Value = "Сооружения";
        sheet.Cell(row, 4).Value = cipher;
        sheet.Cell(row, 5).Value = "Конструкции";
        sheet.Cell(row, 7).Value = code;
    }

    private static void WriteVesr(string path)
    {
        using var book = new XLWorkbook();
        var sheet = book.AddWorksheet("ВЭ");
        sheet.Cell(1, 1).Value = "№ Элемента";
        sheet.Cell(1, 2).Value = "Наименование";
        sheet.Cell(1, 3).Value = "Единица измерения";
        sheet.Cell(1, 4).Value = "Количество";
        sheet.Cell(1, 5).Value = "на единицу";
        sheet.Cell(1, 6).Value = "Всего";
        WriteVesrRow(sheet, 2, "7.10.2.1.11.3", 100000m);
        WriteVesrRow(sheet, 3, "7.10.2.1.11.4", 1000m);
        WriteVesrRow(sheet, 4, "7.10.2.1.11.5", 1000m);
        book.SaveAs(path);
    }

    private static void WriteVesrRow(IXLWorksheet sheet, int row, string code, decimal net)
    {
        sheet.Cell(row, 1).Value = code;
        sheet.Cell(row, 2).Value = "Рабочая документация. Сооружения";
        sheet.Cell(row, 3).Value = "шт";
        sheet.Cell(row, 4).Value = 1;
        sheet.Cell(row, 6).Value = net;
    }

    private static void WriteAct(string path, string element, string code, string cipher, decimal net)
    {
        var gross = decimal.Round(net * 1.22m, 2, MidpointRounding.AwayFromZero);
        var vat = gross - net;
        static string Money(decimal value) => value.ToString("0", System.Globalization.CultureInfo.InvariantCulture);
        using var doc = WordprocessingDocument.Create(path, WordprocessingDocumentType.Document);
        var main = doc.AddMainDocumentPart();
        main.Document = new Document(new Body(
            new Paragraph(new Run(new Text("АКТ № " + element))),
            Table(
                ["№ тома", "Шифр", "Наименование документа"],
                [[code + " (" + element + ")", cipher, "Конструкции"]]),
            Table(
                ["Наименование", "Ед. изм.", "Стоимость без НДС", "НДС", "Стоимость с НДС"],
                [[code + " Рабочая документация. Сооружения", "шт", Money(net), Money(vat), Money(gross)]])));
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
            row.Append(Cell(cell));
        return row;
    }

    private static TableCell Cell(string text) =>
        new(new Paragraph(new Run(new Text(text) { Space = SpaceProcessingModeValues.Preserve })));
}
