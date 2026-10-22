using System.Security.Cryptography;
using System.Security.Cryptography.Pkcs;
using System.Security.Cryptography.X509Certificates;
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
            Cipher = "960-07-1529-А-1.60-07-КЖ3",
            Overall = Severity.Critical,
        });
        report.Findings.Add(new Finding(
            "другой акт", "960-07-1529-А-1.60-07-КЖ3", "Наименование элемента",
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
    public void Verd_register_keeps_kits_under_the_element_and_opens_while_the_file_is_busy()
    {
        var dir = Directory.CreateTempSubdirectory("proverkard-verd");
        var path = Path.Combine(dir.FullName, "verd.xlsx");
        try
        {
            using (var book = new XLWorkbook())
            {
                var report = book.AddWorksheet("Отчет");
                report.Cell(1, 2).Value = "ОТЧЕТ";
                var sheet = book.AddWorksheet("База");
                sheet.Cell(2, 9).Value = "Служебная пометка";
                sheet.Cell(3, 1).Value = "Номер элемента";
                sheet.Cell(3, 2).Value = "Наименование элемента";
                sheet.Cell(3, 3).Value = "Шифр ПД";
                sheet.Cell(3, 4).Value = "Шифр РД";
                sheet.Cell(3, 5).Value = "Наименование документации";
                sheet.Cell(3, 7).Value = "№ Элемента по ДГП";
                sheet.Cell(4, 1).Value = "1";
                sheet.Cell(4, 2).Value = "2";
                sheet.Cell(4, 4).Value = "4";
                sheet.Cell(5, 1).Value = "4.10";
                sheet.Cell(5, 2).Value = "Сооружения синтетические";
                sheet.Cell(5, 4).Value = "100-20-3000-А-КЖ1";
                sheet.Cell(5, 5).Value = "Опоры. Конструкции";
                sheet.Cell(5, 7).Value = "4.10.1";
                sheet.Cell(6, 4).Value = "100-20-3000-А-КЖ2";
                sheet.Cell(6, 5).Value = "Опоры";
                sheet.Cell(7, 1).Value = "4,20";
                sheet.Cell(7, 2).Value = "Второй элемент";
                sheet.Cell(7, 4).Value = "100-20-3000-А-КЖ3";
                sheet.Cell(7, 5).Value = "Продолжение после шифра";
                sheet.Cell(8, 1).Value = 4.3;
                sheet.Cell(8, 2).Value = "Числовой номер";
                sheet.Cell(8, 4).Value = "100-20-3000-А-КЖ4";
                sheet.Cell(8, 5).Value = "Число";
                book.SaveAs(path);
            }

            using var hold = new FileStream(path, FileMode.Open, FileAccess.ReadWrite, FileShare.Read);
            var verd = VerdParser.Parse(path);
            Assert.Empty(verd.Errors);
            Assert.Equal(3, verd.Elements.Count);
            Assert.Equal("4.10", verd.Elements[0].Number);
            Assert.Equal(2, verd.Elements[0].Kits.Count);
            Assert.Equal("100-20-3000-А-КЖ2", verd.Elements[0].Kits[1].Cipher);
            Assert.Equal("4.20", verd.Elements[1].Number);
            Assert.Equal("4.3", verd.Elements[2].Number);
            Assert.Equal("Продолжение после шифра", verd.Elements[1].Kits[0].Name);
        }
        finally
        {
            dir.Delete(recursive: true);
        }
    }

    [Fact]
    public void Title_line_after_the_cipher_is_part_of_the_name_and_a_cut_name_is_critical()
    {
        var dir = Directory.CreateTempSubdirectory("proverkard-title");
        var path = Path.Combine(dir.FullName, "100-20-3000-А-КЖ1.docx");
        try
        {
            using (var doc = WordprocessingDocument.Create(path, WordprocessingDocumentType.Document))
            {
                var main = doc.AddMainDocumentPart();
                main.Document = new Document(new Body(
                    new Paragraph(new Run(new Text("РАБОЧАЯ ДОКУМЕНТАЦИЯ"))),
                    new Paragraph(new Run(new Text("Мост. Опоры."))),
                    new Paragraph(new Run(new Text("100-20-3000-А-КЖ1"))),
                    new Paragraph(new Run(new Text("Конструкции железобетонные"))),
                    new Paragraph(new Run(new Text("Разраб. Петрову А.Б."))),
                    new Paragraph(new Run(new Text("Этап 4.10 Сооружения")))));
                main.Document.Save();
            }

            var rd = RdParser.Parse(path);
            Assert.Contains("Опоры", rd.KitName);
            Assert.Contains("Конструкции железобетонные", rd.KitName);
            Assert.Equal("4.10", rd.ElementNo);
            Assert.Equal("Сооружения", rd.ElementName);
            Assert.Equal(Severity.Critical, TextMatch.Compare("Мост. Опоры.", "Мост. Опоры. Конструкции железобетонные"));
            Assert.Equal(Severity.Formatting, TextMatch.Compare("насыпью Конструкции", "насыпью. Конструкции"));
        }
        finally
        {
            dir.Delete(recursive: true);
        }
    }

    [Fact]
    public void Stamp_line_with_a_chief_title_still_returns_the_surname()
    {
        var names = RdParser.CollectSurnames(
        [
            new RdParser.RdPage(2, "", "Начальник ТО Согласовано Иванов А.А."),
            new RdParser.RdPage(8, "Чертёж\nБетон В25", "Инв. № подл."),
        ]);
        Assert.Contains(names, name => name.Contains("Иванов", StringComparison.Ordinal));
        Assert.DoesNotContain(names, name => name.Contains("Бетон", StringComparison.Ordinal));
    }

    [Fact]
    public void Surnames_come_from_titles_stamps_and_the_first_vor_sheet_only()
    {
        var pages = new[]
        {
            new RdParser.RdPage(1, "Разраб. Сидоров И.И.\nОбщие данные", ""),
            new RdParser.RdPage(2, "ГИП Павлову В.Г.", ""),
            new RdParser.RdPage(3, "Общие данные\nТепловая сеть\nРазработал Петров П.П.", "Пров. Краснощеком А.Б."),
            new RdParser.RdPage(4, "Ведомость объемов работ\nИсп. Янков Я.Я.", "Нач.отд. Смирнов С.С."),
            new RdParser.RdPage(5, "Письмо\nСогласовано Белов А.А.", "Нач.отд. Орлов О.О."),
        };
        var surnames = RdParser.CollectSurnames(pages);
        Assert.Contains(surnames, s => s.Contains("Сидоров", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(surnames, s => s.Contains("Павлову", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(surnames, s => s.Contains("Краснощеком", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(surnames, s => s.Contains("Янков", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(surnames, s => s.Contains("Смирнов", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(surnames, s => s.Contains("Петров", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(surnames, s => s.Contains("Орлов", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(surnames, s => s.Contains("Белов", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(surnames, s => s.Contains("Общие", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(surnames, s => s.Contains("Тепловая", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Feminine_and_adjective_surnames_stay_name_words_do_not()
    {
        var surnames = RdParser.CollectSurnames(
        [
            new RdParser.RdPage(1, "Турникетно-кассовый павильон\nКотельная установка\nПлощадка Корецкого\nУзел Ларецкого\nпримечание Милина без инициалов\nРазраб. Милина А.А.\nДальская И.И.", "Н.контр. Тихих Т.Т."),
            new RdParser.RdPage(2, "ГИП Котельная А.Б.", "Согласовано Ларецкий А.А.\nУтв. Корецкий К.К."),
            new RdParser.RdPage(3, "Чертёж", "Утв. Новина Н.Н."),
            new RdParser.RdPage(4, "Ведомость объемов работ\nИсп. Янков Я.Я.", ""),
            new RdParser.RdPage(5, "Согласовано Белов А.А.", "Светина А.А."),
        ]);
        Assert.Contains(surnames, s => s.Contains("Милина", StringComparison.Ordinal));
        Assert.Contains(surnames, s => s.Contains("Дальская", StringComparison.Ordinal));
        Assert.Contains(surnames, s => s.Contains("Тихих", StringComparison.Ordinal));
        Assert.Contains(surnames, s => s.Contains("Новина", StringComparison.Ordinal));
        Assert.Contains(surnames, s => s.Contains("Янков", StringComparison.Ordinal));
        Assert.DoesNotContain(surnames, s => s.Contains("Турникетно", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(surnames, s => s.Contains("кассовый", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(surnames, s => s.Contains("Котельная", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(surnames, s => s.Contains("Корецк", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(surnames, s => s.Contains("Ларецкий", StringComparison.Ordinal));
        Assert.DoesNotContain(surnames, s => s.Contains("Белов", StringComparison.Ordinal));
        Assert.DoesNotContain(surnames, s => s.Contains("Светина", StringComparison.Ordinal));
    }

    [Fact]
    public void Name_words_and_hyphen_parts_are_not_surnames()
    {
        var surnames = RdParser.CollectSurnames(
        [
            new RdParser.RdPage(1, "Турникетно-кассовый павильон\nКотельная установка\nРазраб. Петров А.А.\nСидоров И.И.", ""),
            new RdParser.RdPage(2, "ГИП Котельная А.Б.", ""),
            new RdParser.RdPage(3, "Чертёж", "Разраб. Турникетно-кассовый А.А.Петров"),
            new RdParser.RdPage(4, "Ведомость объемов работ\nИсп. Янков Я.Я.", ""),
            new RdParser.RdPage(5, "Согласовано Белов А.А.", "Нач.отд. Орлов О.О."),
        ]);
        Assert.Contains(surnames, s => s.Contains("Петров", StringComparison.Ordinal));
        Assert.Contains(surnames, s => s.Contains("Сидоров", StringComparison.Ordinal));
        Assert.Contains(surnames, s => s.Contains("Янков", StringComparison.Ordinal));
        Assert.DoesNotContain(surnames, s => s.Contains("Турникетно", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(surnames, s => s.Contains("кассовый", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(surnames, s => s.Contains("Котельная", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(surnames, s => s.Contains("Белов", StringComparison.Ordinal));
        Assert.DoesNotContain(surnames, s => s.Contains("Орлов", StringComparison.Ordinal));
    }

    [Fact]
    public void Glued_initials_and_surname_are_cut_from_the_element_name()
    {
        var dir = Directory.CreateTempSubdirectory("proverkard-glue");
        var path = Path.Combine(dir.FullName, "100-20-3000-А-КЖ1.docx");
        try
        {
            using (var doc = WordprocessingDocument.Create(path, WordprocessingDocumentType.Document))
            {
                var main = doc.AddMainDocumentPart();
                main.Document = new Document(new Body(
                    new Paragraph(new Run(new Text("РАБОЧАЯ ДОКУМЕНТАЦИЯ"))),
                    new Paragraph(new Run(new Text("Опоры"))),
                    new Paragraph(new Run(new Text("100-20-3000-А-КЖ1"))),
                    new Paragraph(new Run(new Text("Тестов А.Б."))),
                    new Paragraph(new Run(new Text("Разраб. Петрову А.Б."))),
                    new Paragraph(new Run(new Text("Этап 4.10 Сооружения ПК6146+67,00 А.А.Тестов")))));
                main.Document.Save();
            }

            var rd = RdParser.Parse(path);
            Assert.Equal("Сооружения ПК6146+67,00", rd.ElementName);
            Assert.Contains("Опоры", rd.KitName);
            Assert.DoesNotContain("Тестов", rd.KitName);
            Assert.DoesNotContain("Тестов", rd.ElementName);
        }
        finally
        {
            dir.Delete(recursive: true);
        }
    }

    [Fact]
    public void Name_difference_count_follows_kits_not_unique_text()
    {
        var dir = Directory.CreateTempSubdirectory("proverkard-count");
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
                sheet.Cell(2, 5).Value = "Опоры ПК6013+82";
                sheet.Cell(3, 4).Value = "100-20-3000-А-КМ2";
                sheet.Cell(3, 5).Value = "Опоры ПК6013+82";
                sheet.Cell(4, 4).Value = "100-20-3000-А-КМ3";
                sheet.Cell(4, 5).Value = "Опоры ПК6013+82";
                book.SaveAs(verd);
            }

            var act = Path.Combine(dir.FullName, "РД 4.10.docx");
            WriteActRows(act, "4.10", "4.10.1.1.1",
            [
                ("100-20-3000-А-КМ1", "Опоры ПК6013+82"),
                ("100-20-3000-А-КМ2", "Опоры ПК6013+82"),
                ("100-20-3000-А-КМ3", "Опоры ПК6013+82"),
            ], 1000m);
            var rd1 = Path.Combine(dir.FullName, "100-20-3000-А-КМ1.docx");
            var rd2 = Path.Combine(dir.FullName, "100-20-3000-А-КМ2.docx");
            var rd3 = Path.Combine(dir.FullName, "100-20-3000-А-КМ3.docx");
            WriteRd(rd1, "100-20-3000-А-КМ1", "Опоры ПК 6013+82", "4.10", "Сооружения");
            WriteRd(rd2, "100-20-3000-А-КМ2", "Опоры ПК 6013+82", "4.10", "Сооружения");
            WriteRd(rd3, "100-20-3000-А-КМ3", "Опоры ПК 6013+82", "4.10", "Сооружения");

            var report = new CheckingEngine().Run(new CheckInput
            {
                VerdPath = verd,
                ActPaths = [act],
                RdPaths = [rd1, rd2, rd3],
            });
            Assert.Contains("оформление наименований: 3", report.Acts.Single().CompletenessText);
            Assert.DoesNotContain("оформление наименований: 1", report.Acts.Single().CompletenessText);
        }
        finally
        {
            dir.Delete(recursive: true);
        }
    }

    [Theory]
    [InlineData("Чужой текст", "Общий текст", "Общий текст", SourceBlame.InAct)]
    [InlineData("Общий текст", "Чужой текст", "Общий текст", SourceBlame.InRd)]
    [InlineData("Общий текст", "Общий текст", "Чужой текст", SourceBlame.InVerd)]
    [InlineData("Альфа", "Бета", "Гамма", "")]
    [InlineData("Только акт", "Только РД", "", "")]
    [InlineData("Одно", "Одно", "Одно", "")]
    public void Blame_names_the_source_that_breaks_a_pair(string act, string rd, string verd, string expected)
    {
        Assert.Equal(expected, SourceBlame.Of(act, rd, verd));
    }

    [Fact]
    public void Blame_ignores_punctuation_and_marks_every_source_with_a_typo()
    {
        Assert.Equal("", SourceBlame.Of("Опоры.", "Опоры", "Опоры"));
        Assert.Equal(SourceBlame.InAct, SourceBlame.Of("Другие сооружения", "Опоры.", "Опоры"));
        Assert.Equal(SourceBlame.InRd, SourceBlame.Of("Опоры", "Мост иной", "Опоры "));
        Assert.Equal("", SourceBlame.Of("Альфа", "Бета", "Гамма"));

        const string typo = "насыпью соружения";
        const string correct = "насыпью сооружения";
        Assert.Equal(Severity.Formatting, TextMatch.Compare(typo, correct));
        Assert.Equal(
            SourceBlame.InAct + "; " + SourceBlame.InVerd,
            SourceBlame.Of(typo, correct, typo));
        Assert.Equal(SourceBlame.InAct, SourceBlame.Of(typo, correct, correct));
        Assert.Equal(
            SourceBlame.InAct + "; " + SourceBlame.InRd,
            SourceBlame.Of(typo, typo, correct));
        Assert.DoesNotContain(SourceBlame.InRd, SourceBlame.Of(typo, correct, typo));
    }

    [Fact]
    public void Error_place_is_a_findings_column_and_a_kit_status()
    {
        var dir = Directory.CreateTempSubdirectory("proverkard-blame");
        try
        {
            var verd = Path.Combine(dir.FullName, "verd.xlsx");
            var vesr = Path.Combine(dir.FullName, "vesr.xlsx");
            using (var book = new XLWorkbook())
            {
                var sheet = book.AddWorksheet("База");
                sheet.Cell(1, 1).Value = "Номер элемента";
                sheet.Cell(1, 2).Value = "Наименование элемента";
                sheet.Cell(1, 4).Value = "Шифр РД";
                sheet.Cell(1, 5).Value = "Наименование документации";
                sheet.Cell(1, 7).Value = "№ Элемента по ДГП";
                sheet.Cell(2, 1).Value = "4.10";
                sheet.Cell(2, 2).Value = "Сооружения";
                sheet.Cell(2, 7).Value = "4.10.1.1.1";
                sheet.Cell(2, 4).Value = "100-20-3000-А-КМ1";
                sheet.Cell(2, 5).Value = "Альфа комплект";
                sheet.Cell(3, 4).Value = "100-20-3000-А-КМ2";
                sheet.Cell(3, 5).Value = "Дельта комплект";
                sheet.Cell(4, 4).Value = "100-20-3000-А-КМ3";
                sheet.Cell(4, 5).Value = "Второй текст";
                book.SaveAs(verd);
            }
            WriteVesrRowFile(vesr, "4.10.1.1.1", 1000m);
            var act = Path.Combine(dir.FullName, "РД 4.10.docx");
            WriteActRows(act, "4.10", "4.10.1.1.1",
            [
                ("100-20-3000-А-КМ1", "Альфа комплект"),
                ("100-20-3000-А-КМ2", "Гамма комплект"),
                ("100-20-3000-А-КМ3", "Первый текст"),
            ], 1000m);
            var rd1 = Path.Combine(dir.FullName, "100-20-3000-А-КМ1.docx");
            var rd2 = Path.Combine(dir.FullName, "100-20-3000-А-КМ2.docx");
            var rd3 = Path.Combine(dir.FullName, "100-20-3000-А-КМ3.docx");
            WriteRd(rd1, "100-20-3000-А-КМ1", "Бета комплект", "4.10", "Сооружения");
            WriteRd(rd2, "100-20-3000-А-КМ2", "Дельта комплект", "4.10", "Сооружения");
            WriteRd(rd3, "100-20-3000-А-КМ3", "Третий текст", "4.10", "Сооружения");

            var report = new CheckingEngine().Run(new CheckInput
            {
                VerdPath = verd,
                VesrPath = vesr,
                ActPaths = [act],
                RdPaths = [rd1, rd2, rd3],
            });

            var names = report.Findings.Where(f => f.CheckName == "Наименование комплекта").ToList();
            Assert.Equal(SourceBlame.InRd, names.Single(f => f.Kit.Contains("КМ1")).ErrorWhere);
            Assert.Equal(SourceBlame.InAct, names.Single(f => f.Kit.Contains("КМ2")).ErrorWhere);
            Assert.Equal("", names.Single(f => f.Kit.Contains("КМ3")).ErrorWhere);
            Assert.Contains("наименования различаются: 3", report.Acts.Single().CompletenessText);

            var reportPath = Path.Combine(dir.FullName, "report.xlsx");
            ReportWriter.Write(report, reportPath, "0.0.5");
            using var outBook = new XLWorkbook(reportPath);
            var findings = outBook.Worksheet("Замечания");
            Assert.Equal("Где ошибка", findings.Cell(4, 10).GetString());
            var km1 = Enumerable.Range(5, findings.LastRowUsed()!.RowNumber())
                .First(r => findings.Cell(r, 4).GetString() == "Наименование комплекта" && findings.Cell(r, 9).GetString().Contains("КМ1"));
            Assert.Equal(SourceBlame.InRd, findings.Cell(km1, 10).GetString());
            var kits = outBook.Worksheet("Комплекты");
            Assert.Contains(SourceBlame.InRd, kits.Cell(6, 11).GetString());
            Assert.Contains(SourceBlame.InAct, kits.Cell(7, 11).GetString());
            Assert.Equal("Критично", kits.Cell(8, 11).GetString());
        }
        finally
        {
            dir.Delete(recursive: true);
        }
    }

    [Fact]
    public void Misnamed_signature_is_formatting_when_it_signs_the_file()
    {
        var dir = Directory.CreateTempSubdirectory("proverkard-sig");
        var path = Path.Combine(dir.FullName, "100-20-3000-А-АС.docx");
        try
        {
            WriteRd(path, "100-20-3000-А-АС", "Опоры", "4.10", "Сооружения");
            using var rsa = RSA.Create(2048);
            var request = new CertificateRequest(
                new X500DistinguishedName("SN=Тестов, CN=Тестов"),
                rsa,
                HashAlgorithmName.SHA256,
                RSASignaturePadding.Pkcs1);
            using var cert = request.CreateSelfSigned(DateTimeOffset.UtcNow.AddDays(-1), DateTimeOffset.UtcNow.AddDays(2));
            var signed = Detached(File.ReadAllBytes(path), cert);
            var actualName = "100-20-3000-А-АС-.docx.sig";
            File.WriteAllBytes(Path.Combine(dir.FullName, actualName), signed);
            File.WriteAllBytes(Path.Combine(dir.FullName, "чужой.sig"), Detached("другие байты"u8.ToArray(), cert));

            var located = SignatureParser.Locate(path);
            Assert.NotNull(located);
            Assert.True(located.Value.NameDiffers);
            Assert.Equal(actualName, Path.GetFileName(located.Value.Path));

            var exactDir = Directory.CreateTempSubdirectory("proverkard-sig-exact");
            try
            {
                var exactPath = Path.Combine(exactDir.FullName, "100-20-3000-А-АС.docx");
                File.Copy(path, exactPath);
                File.WriteAllBytes(exactPath + ".sig", signed);
                var exact = SignatureParser.Locate(exactPath);
                Assert.NotNull(exact);
                Assert.False(exact.Value.NameDiffers);
            }
            finally
            {
                exactDir.Delete(recursive: true);
            }

            var report = new CheckingEngine().Run(new CheckInput { RdPaths = [path] });
            var signature = report.Findings.Where(f => f.CheckName == "Электронная подпись").ToList();
            var naming = Assert.Single(signature);
            Assert.Equal(Severity.Formatting, naming.Severity);
            Assert.Contains("Ошибка оформления: файл подписи назван иначе (" + actualName + ")", naming.InRd);
            Assert.DoesNotContain(report.Findings, f => f.CheckName == "Электронная подпись" && f.Severity == Severity.Critical);

            File.Delete(Path.Combine(dir.FullName, actualName));
            var missing = new CheckingEngine().Run(new CheckInput { RdPaths = [path] });
            Assert.Contains(missing.Findings, f => f.CheckName == "Электронная подпись" && f.Severity == Severity.Critical);
        }
        finally
        {
            dir.Delete(recursive: true);
        }
    }

    [Fact]
    public void Letter_without_a_cipher_is_a_warning_not_a_set()
    {
        var dir = Directory.CreateTempSubdirectory("proverkard-stray");
        var letter = Path.Combine(dir.FullName, "Письмо от 01.01.2026.docx");
        var kit = Path.Combine(dir.FullName, "100-20-3000-А-КМ1.docx");
        try
        {
            using (var doc = WordprocessingDocument.Create(letter, WordprocessingDocumentType.Document))
            {
                var main = doc.AddMainDocumentPart();
                main.Document = new Document(new Body(
                    new Paragraph(new Run(new Text("Сопроводительное письмо без шифра комплекта")))));
                main.Document.Save();
            }

            WriteRd(kit, "100-20-3000-А-КМ1", "Опоры", "4.10", "Сооружения");
            var act = Path.Combine(dir.FullName, "РД 4.11.docx");
            WriteAct(act, "4.11", "4.11.1.1.1", "100-20-3000-А-КМ9", 1000m);
            var parsed = RdParser.Parse(letter);
            Assert.True(RdFileRules.IsStray(parsed));
            Assert.False(RdFileRules.IsStray(RdParser.Parse(kit)));

            var report = new CheckingEngine().Run(new CheckInput
            {
                ActPaths = [act],
                RdPaths = [letter, kit],
            });
            var stray = Assert.Single(report.Findings, f => f.CheckName == "Посторонний файл");
            Assert.Equal(Severity.Warning, stray.Severity);
            Assert.Equal("Письмо от 01.01.2026.docx", stray.InRd);
            Assert.DoesNotContain(report.Findings, f => f.CheckName == "Комплект вне актов" && f.ValueA.Contains("Письмо"));
            Assert.DoesNotContain(report.Findings, f => f.CheckName == "Электронная подпись" && f.InRd.Contains("Письмо"));
            Assert.Contains(report.Findings, f => f.CheckName == "Электронная подпись" && f.Kit.Contains("КМ1"));
        }
        finally
        {
            dir.Delete(recursive: true);
        }
    }

    [Fact]
    public void Forced_update_installs_even_when_the_version_matches()
    {
        Assert.True(UpdateOffer.ShouldInstall(remoteIsNewer: false, force: true));
        Assert.False(UpdateOffer.ShouldInstall(remoteIsNewer: false, force: false));
        Assert.True(UpdateOffer.ShouldInstall(remoteIsNewer: true, force: false));
    }

    [Fact]
    public void Missing_volume_surname_is_critical_and_extra_signer_is_silent()
    {
        var missing = SignerAudit.Check(["Янков", "Павлову"], ["Павлов", "Гирсова"], true, null, []);
        Assert.Equal(Severity.Critical, missing.Status);
        Assert.Equal("Янков", Assert.Single(missing.Missing));

        var matched = SignerAudit.Check(["Павлову"], ["Павлов", "Гирсова"], true, null, []);
        Assert.Equal(Severity.Ok, matched.Status);
        Assert.Empty(matched.Missing);

        var noSig = SignerAudit.Check(["Янков"], [], false, null, []);
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
                sheet.Cell(2, 4).Value = "960-07-1529-А-1.60-07-ТМ8";
                sheet.Cell(2, 5).Value = "насыпью. Конструкции";
                sheet.Cell(2, 7).Value = "7.10.2.1.11.5";
                verdBook.SaveAs(verd);
            }
            WriteVesr(vesr);
            var actPath = Path.Combine(dir.FullName, "РД 7.31.docx");
            WriteAct(actPath, "7.31", "7.10.2.1.11.5", "960-07-1529-А-1.60-07-ТМ8", 1000m, "насыпью Конструкции", withAdvance: true);
            var rdPath = Path.Combine(dir.FullName, "960-07-1529-А-1.60-07-ТМ8.docx");
            WriteRd(rdPath, "960-07-1529-А-1.60-07-ТМ8", "насыпью. Конструкции", "7.31", "соружения");

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
            Assert.Equal("960-07-1529-А-1.60-07-ТМ8", names[0].Places);
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
            Assert.Equal(XLDataType.Number, kits.Cell(6, 3).DataType);
            Assert.Equal("0,00", kits.Cell(6, 3).Style.NumberFormat.Format);
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

    private static void WriteActRows(string path, string element, string code, (string Cipher, string Name)[] kits, decimal net)
    {
        var gross = decimal.Round(net * 1.22m, 2, MidpointRounding.AwayFromZero);
        var vat = gross - net;
        static string Money(decimal value) => value.ToString("0", System.Globalization.CultureInfo.InvariantCulture);
        var rows = kits.Select(kit => new[] { code + " (" + element + ")", kit.Cipher, kit.Name }).ToArray();
        using var doc = WordprocessingDocument.Create(path, WordprocessingDocumentType.Document);
        var main = doc.AddMainDocumentPart();
        main.Document = new Document(new Body(
            new Paragraph(new Run(new Text("АКТ № " + element))),
            Table(
                ["№ тома", "Шифр", "Наименование документа"],
                rows),
            Table(
                ["Наименование", "Ед. изм.", "Стоимость без НДС", "НДС", "Стоимость с НДС"],
                [[code + " Рабочая документация. Сооружения", "шт", Money(net), Money(vat), Money(gross)]])));
        main.Document.Save();
    }

    private static void WriteVesrRowFile(string path, string code, decimal net)
    {
        using var book = new XLWorkbook();
        var sheet = book.AddWorksheet("ВЭ");
        sheet.Cell(1, 1).Value = "№ Элемента";
        sheet.Cell(1, 2).Value = "Наименование";
        sheet.Cell(1, 3).Value = "Единица измерения";
        sheet.Cell(1, 4).Value = "Количество";
        sheet.Cell(1, 5).Value = "на единицу";
        sheet.Cell(1, 6).Value = "Всего";
        WriteVesrRow(sheet, 2, code, net);
        book.SaveAs(path);
    }

    private static byte[] Detached(byte[] content, X509Certificate2 cert)
    {
        var cms = new SignedCms(new ContentInfo(content), detached: true);
        cms.ComputeSignature(new CmsSigner(cert));
        return cms.Encode();
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
