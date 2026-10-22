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
            new KitResult { ActName = "РД 7.29", ElementNo = "7.29", Cipher = "000-00-0000-А-1.60-07-ТМ2", Overall = Severity.Critical },
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
            WriteAct(Path.Combine(dir.FullName, "РД 7.46.docx"), "7.46", "7.10.2.1.11.3", "000-00-0000-Д-АБ01-01-КЖ1", 70000m);
            WriteAct(Path.Combine(dir.FullName, "РД 7.29.docx"), "7.29", "7.10.2.1.11.4", "000-00-0000-А-1.60-07-ТМ2", 1000m);
            WriteAct(Path.Combine(dir.FullName, "РД 7.01.docx"), "7.01", "7.10.2.1.11.5", "000-00-0000-А-1.60-07-ТМ8", 1000m);

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
            var summaryRow46 = Enumerable.Range(6, 10).First(r => summary.Cell(r, 1).GetString() == "7.46");
            Assert.Equal("Критично", summary.Cell(summaryRow46, 2).GetString());
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
        const string cipher = "000-00-0000-А-1.60-07-ТМ2";
        Assert.Equal(cipher, RdParser.FindCipher("Комплект " + cipher + ". Лист 1"));
        Assert.Equal("000-00-0000-Д-АБ01-01-КЖ1", RdParser.FindCipher("000-00-0000-Д-АБ01-01-КЖ1"));
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
            Н.контр. Фролов Я.Я.
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
        Assert.False(SurnameStem.Same("Сидоров", "Фролов"));
    }

    [Fact]
    public void Auxiliary_gsn_names_are_not_kits()
    {
        Assert.True(RdFileRules.IsAuxiliary(@"C:\rd\ГСН.СВОР.pdf"));
        Assert.True(RdFileRules.IsAuxiliary(@"C:\rd\ведомость_ИЗМ.xlsx"));
        Assert.False(RdFileRules.IsAuxiliary(@"C:\rd\000-00-0000-А-1.60-07-ТМ2.pdf"));
    }

    [Fact]
    public void Signer_warnings_and_identical_findings_collapse_to_one_row()
    {
        var findings = new List<Finding>();
        for (var i = 0; i < 4; i++)
        {
            findings.Add(new Finding("Акт " + i, "КЖ" + i, FindingGrouper.SignerMissing, "Белкина", "список", Severity.Warning, "длинно", "7." + (30 + i)));
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
        Assert.DoesNotContain("Акт", typos[0].Places);
        Assert.Equal(4, typos[0].Locations.Count);
    }

    [Fact]
    public void Summary_cell_is_critical_when_the_kit_is_critical_even_if_act_field_was_left_ok()
    {
        var act = new ActResult
        {
            FileName = "РД 7.31",
            SourcePath = @"C:\acts\7.31.docx",
            ElementNo = "7.31",
            Overall = Severity.Ok,
        };
        var report = new CheckReport();
        report.Acts.Add(act);
        report.Kits.Add(new KitResult
        {
            ActName = "другой акт",
            SourcePath = act.SourcePath,
            ElementNo = "",
            Cipher = "000-00-0000-А-1.60-07-КЖ3",
            Overall = Severity.Critical,
        });
        report.Findings.Add(new Finding(
            "другой акт", "000-00-0000-А-1.60-07-КЖ3", "Наименование элемента",
            "насыпью Конструкции", "насыпью. Конструкции", Severity.Critical, "",
            "7.99", ActPath: act.SourcePath));

        var path = Path.Combine(Path.GetTempPath(), "proverkard-summary-" + Guid.NewGuid().ToString("N") + ".xlsx");
        try
        {
            ReportWriter.Write(report, path, "0.0.3");
            using var book = new XLWorkbook(path);
            var summary = book.Worksheet("Сводка");
            Assert.Equal("7.31", summary.Cell(6, 1).GetString());
            Assert.Equal("Критично", summary.Cell(6, 2).GetString());
        }
        finally
        {
            if (File.Exists(path))
                File.Delete(path);
        }
    }

    [Fact]
    public void Advance_amount_reads_rubles_and_kopecks()
    {
        const string sentence = "Приложение к акту сдачи-приёмки рабочей документации № 7.33. Расчет погашенной части аванса в размере 168 027 (сто шестьдесят восемь тысяч двадцать семь) рублей 54 копейки без учета НДС.";
        Assert.Equal(168027.54m, AdvancePayment.Parse(sentence));
        Assert.Equal(168027.54m, AdvancePayment.Parse(sentence.Replace(" ", "\u00A0")));
        Assert.Equal(10.01m, AdvancePayment.Parse("аванса в размере 10 рубль 01 копейка"));
        Assert.Equal(2000.00m, AdvancePayment.Parse("аванса в размере 2 000 рублей 00 копеек"));
        Assert.Null(AdvancePayment.Parse("Сумма договора 168 027 рублей 54 копейки."));
    }

    [Fact]
    public void Surnames_come_from_titles_stamps_and_the_first_vor_sheet_only()
    {
        var pages = new[]
        {
            new RdParser.RdPage(1, "Разраб. Сидоров И.И.\nОбщие данные", ""),
            new RdParser.RdPage(2, "ГИП Павлову В.Г.", ""),
            new RdParser.RdPage(3, "Общие данные\nТепловая сеть\nРазработал Петров П.П.", "Пров. Краснощеком А.Б."),
            new RdParser.RdPage(4, "Ведомость объемов работ\nИсп. Фролов Я.Я.", ""),
            new RdParser.RdPage(5, "Ведомость объемов работ\nУтв. Орлов О.О.", "Нач.отд. Смирнов С.С."),
        };
        var surnames = RdParser.CollectSurnames(pages);
        Assert.Contains(surnames, s => s.Contains("Сидоров", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(surnames, s => s.Contains("Павлову", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(surnames, s => s.Contains("Краснощеком", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(surnames, s => s.Contains("Фролов", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(surnames, s => s.Contains("Смирнов", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(surnames, s => s.Contains("Петров", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(surnames, s => s.Contains("Орлов", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(surnames, s => s.Contains("Общие", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(surnames, s => s.Contains("Тепловая", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Missing_volume_surname_is_critical_and_extra_signer_is_silent()
    {
        var missing = SignerAudit.Check(["Фролов", "Павлову"], ["Павлов", "Белкина"], true, null, []);
        Assert.Equal(Severity.Critical, missing.Status);
        Assert.Equal("Фролов", Assert.Single(missing.Missing));

        var matched = SignerAudit.Check(["Павлову"], ["Павлов", "Белкина"], true, null, []);
        Assert.Equal(Severity.Ok, matched.Status);
        Assert.Empty(matched.Missing);

        var noSig = SignerAudit.Check(["Фролов"], [], false, null, []);
        Assert.True(noSig.SignatureMissing);
        Assert.Equal(Severity.Critical, noSig.Status);
    }

    [Fact]
    public void Release_json_selects_exe_and_sha256_assets()
    {
        const string json = """
            {
              "tag_name": "v0.0.3",
              "body": "Что нового",
              "html_url": "https://github.com/sozertsatel/ProverkaRD/releases/tag/v0.0.3",
              "assets": [
                {"name": "notes.txt", "browser_download_url": "https://example/notes", "size": 4},
                {"name": "ProverkaRD.exe", "browser_download_url": "https://example/ProverkaRD.exe", "size": 1000},
                {"name": "ProverkaRD.exe.sha256", "browser_download_url": "https://example/ProverkaRD.exe.sha256", "size": 64}
              ]
            }
            """;
        var release = ReleaseCatalog.Parse(json);
        Assert.NotNull(release);
        Assert.Equal("v0.0.3", release!.Tag);
        Assert.Equal("https://example/ProverkaRD.exe", release.Exe!.Url);
        Assert.Equal(1000, release.Exe.Size);
        Assert.Equal("https://example/ProverkaRD.exe.sha256", release.Sha256!.Url);
        Assert.True(ReleaseVersion.IsNewer(release.Tag, "0.0.2"));
        var bytes = "proverkard"u8.ToArray();
        var hash = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(bytes)).ToLowerInvariant();
        Assert.True(ReleaseCatalog.HashMatches(bytes, hash + "  ProverkaRD.exe\n"));
        Assert.False(ReleaseCatalog.HashMatches(bytes, new string('a', 64)));
    }

    [Fact]
    public void One_field_is_one_findings_row_and_kit_sheet_has_advance()
    {
        var dir = Directory.CreateTempSubdirectory("proverkard-field");
        try
        {
            var verd = Path.Combine(dir.FullName, "verd.xlsx");
            var vesr = Path.Combine(dir.FullName, "vesr.xlsx");
            using (var verdBook = new XLWorkbook())
            {
                var sheet = verdBook.AddWorksheet("База");
                sheet.Cell(1, 1).Value = "Номер элемента";
                sheet.Cell(1, 2).Value = "Наименование элемента";
                sheet.Cell(1, 4).Value = "Шифр РД";
                sheet.Cell(1, 5).Value = "Наименование документации";
                sheet.Cell(1, 7).Value = "№ Элемента по ДГП";
                sheet.Cell(2, 1).Value = "7.31";
                sheet.Cell(2, 2).Value = "Сооружения";
                sheet.Cell(2, 4).Value = "000-00-0000-А-1.60-07-ТМ8";
                sheet.Cell(2, 5).Value = "насыпью. Конструкции";
                sheet.Cell(2, 7).Value = "7.10.2.1.11.5";
                verdBook.SaveAs(verd);
            }
            WriteVesr(vesr);
            var actPath = Path.Combine(dir.FullName, "РД 7.31.docx");
            WriteAct(actPath, "7.31", "7.10.2.1.11.5", "000-00-0000-А-1.60-07-ТМ8", 1000m, "насыпью Конструкции", withAdvance: true);
            var rdPath = Path.Combine(dir.FullName, "000-00-0000-А-1.60-07-ТМ8.docx");
            WriteRd(rdPath, "000-00-0000-А-1.60-07-ТМ8", "насыпью. Конструкции", "7.31", "соружения");

            var report = new CheckingEngine().Run(new CheckInput
            {
                VerdPath = verd,
                VesrPath = vesr,
                ActPaths = [actPath],
                RdPaths = [rdPath],
            });

            var names = report.Findings.Where(f => f.CheckName == "Наименование комплекта").ToList();
            Assert.Single(names);
            Assert.Equal(Severity.Formatting, names[0].Severity);
            Assert.Contains("насыпью", names[0].InAct);
            Assert.Contains("насыпью", names[0].InRd);
            Assert.Contains("Конструкции", names[0].InVerd);
            Assert.Equal("000-00-0000-А-1.60-07-ТМ8", names[0].Places);
            Assert.DoesNotContain("РД 7.31", names[0].Places);
            var element = report.Findings.Single(f => f.CheckName == "Наименование элемента" && f.Kit.Contains("ТМ8"));
            Assert.Equal(Severity.Formatting, element.Severity);
            Assert.Contains(report.Findings, f => f.CheckName == "Электронная подпись" && f.Severity == Severity.Critical);
            Assert.DoesNotContain(report.Findings, f => f.CheckName == FindingGrouper.SignerMissing);
            Assert.Equal(168027.54m, report.Acts.Single().AdvanceNet);

            var reportPath = Path.Combine(dir.FullName, "report.xlsx");
            ReportWriter.Write(report, reportPath, "0.0.3");
            using var book = new XLWorkbook(reportPath);
            var kits = book.Worksheet("Комплекты");
            Assert.Equal("1. Шифр", kits.Cell(4, 4).GetString());
            Assert.Equal("2. Наименование комплекта", kits.Cell(4, 8).GetString());
            Assert.Equal("4. Файлы", kits.Cell(4, 16).GetString());
            Assert.Equal("5. Подписи", kits.Cell(4, 18).GetString());
            Assert.Equal("Погашение аванса без НДС", kits.Cell(5, 3).GetString());
            Assert.Equal(168027.54m, kits.Cell(6, 3).GetValue<decimal>());
            Assert.Equal("0.00", kits.Cell(6, 3).Style.NumberFormat.Format);
            var summary = book.Worksheet("Сводка");
            Assert.Equal("Критично", summary.Cell(6, 2).GetString());
            var findings = book.Worksheet("Замечания");
            var nameRow = Enumerable.Range(5, findings.LastRowUsed()!.RowNumber())
                .First(r => findings.Cell(r, 4).GetString() == "Наименование комплекта");
            Assert.True(findings.Cell(nameRow, 5).Style.Font.Bold);
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
        sheet.Cell(1, 3).Value = "Шифр ПД";
        sheet.Cell(1, 4).Value = "Шифр РД";
        sheet.Cell(1, 5).Value = "Наименование документации";
        sheet.Cell(1, 7).Value = "№ Элемента по ДГП";
        WriteVerdRow(sheet, 2, "7.46", "7.10.2.1.11.3", "000-00-0000-Д-АБ01-01-КЖ1");
        WriteVerdRow(sheet, 3, "7.29", "7.10.2.1.11.4", "000-00-0000-А-1.60-07-ТМ3");
        WriteVerdRow(sheet, 4, "7.01", "7.10.2.1.11.5", "000-00-0000-А-1.60-07-ТМ8");
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

    private static void WriteAct(string path, string element, string code, string cipher, decimal net, string kitName = "Конструкции", bool withAdvance = false)
    {
        var gross = decimal.Round(net * 1.22m, 2, MidpointRounding.AwayFromZero);
        var vat = gross - net;
        static string Money(decimal value) => value.ToString("0", System.Globalization.CultureInfo.InvariantCulture);
        using var doc = WordprocessingDocument.Create(path, WordprocessingDocumentType.Document);
        var main = doc.AddMainDocumentPart();
        main.Document = new Document(new Body(
            new Paragraph(new Run(new Text("АКТ № " + element))),
            withAdvance
                ? new Paragraph(new Run(new Text("Приложение к акту сдачи-приёмки рабочей документации № " + element + ". Расчет погашенной части аванса в размере 168\u00A0027 (сто шестьдесят восемь тысяч двадцать семь) рублей 54 копейки без учета НДС.")))
                : new Paragraph(new Run(new Text(""))),
            Table(
                ["№ тома", "Шифр", "Наименование документа"],
                [[code + " (" + element + ")", cipher, kitName]]),
            Table(
                ["Наименование", "Ед. изм.", "Стоимость без НДС", "НДС", "Стоимость с НДС"],
                [[code + " Рабочая документация. Сооружения", "шт", Money(net), Money(vat), Money(gross)]])));
        main.Document.Save();
    }

    private static void WriteRd(string path, string cipher, string kitName, string element, string elementName)
    {
        using var doc = WordprocessingDocument.Create(path, WordprocessingDocumentType.Document);
        var main = doc.AddMainDocumentPart();
        main.Document = new Document(new Body(
            new Paragraph(new Run(new Text("РАБОЧАЯ ДОКУМЕНТАЦИЯ"))),
            new Paragraph(new Run(new Text(kitName))),
            new Paragraph(new Run(new Text(cipher))),
            new Paragraph(new Run(new Text("Разраб. Павлову В.Г."))),
            new Paragraph(new Run(new Text("Этап " + element + " " + elementName)))));
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
