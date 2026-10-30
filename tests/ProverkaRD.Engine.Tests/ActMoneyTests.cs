using ClosedXML.Excel;
using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Wordprocessing;

namespace ProverkaRD.Engine.Tests;

public class ActMoneyTests
{
    private const string SampleSentence =
        "Стоимость выполненных работ составляет 483 073 (четыреста восемьдесят три тысячи семьдесят три) рубля 86 копеек, в том числе НДС (22%) – 87 111 (восемьдесят семь тысяч сто одиннадцать) рублей 68 копеек.";

    [Fact]
    public void Spoken_rubles_match_the_digits_including_the_thousand_case()
    {
        Assert.True(MoneyWords.TryParse("четыреста восемьдесят три тысячи семьдесят три", out var gross));
        Assert.Equal(483_073, gross);
        Assert.True(MoneyWords.TryParse("восемьдесят семь тысяч сто одиннадцать", out var vat));
        Assert.Equal(87_111, vat);
        Assert.True(MoneyWords.TryParse("сто восемьдесят семь тысячи триста шестьдесят девять", out var advance));
        Assert.Equal(187_369, advance);
        Assert.True(MoneyWords.TryParse("одна тысяча двести двадцать", out var one));
        Assert.Equal(1_220, one);
        Assert.False(MoneyWords.TryParse("четыреста тысяч чего-то", out _));
    }

    [Fact]
    public void Clause_reads_the_table_sentence_and_ignores_the_heading_before_the_table()
    {
        var clause = ActMoneyText.Parse(SampleSentence);
        Assert.NotNull(clause);
        Assert.Equal(483073.86m, clause!.Gross);
        Assert.Equal(87111.68m, clause.Vat);
        Assert.True(clause.HasVat);
        Assert.Equal(22m, clause.VatPercent);
        Assert.True(clause.GrossWordsMatch);
        Assert.True(clause.VatWordsMatch);

        var withHeading = "Стоимость выполненных работ составляет:\n395 962,18 87 111,68 483 073,86\n" + SampleSentence;
        var again = ActMoneyText.Parse(withHeading);
        Assert.Equal(483073.86m, again!.Gross);
        Assert.Equal(87111.68m, again.Vat);
    }

    [Fact]
    public void Words_that_do_not_spell_the_digits_do_not_match()
    {
        const string text =
            "Стоимость выполненных работ составляет 483 073 (четыреста тысяч) рубля 86 копеек, в том числе НДС (22%) – 87 111 (сто) рублей 68 копеек.";
        var clause = ActMoneyText.Parse(text);
        Assert.NotNull(clause);
        Assert.Equal(483073.86m, clause!.Gross);
        Assert.False(clause.GrossWordsMatch);
        Assert.False(clause.VatWordsMatch);
    }

    [Fact]
    public void Matching_sentence_is_not_a_finding()
    {
        var report = Run(SampleSentence, 395962.18m, 87111.68m, 483073.86m);
        Assert.DoesNotContain(report.Findings, finding => finding.CheckName is "Сумма в тексте акта" or "НДС в тексте акта" or "Сумма прописью" or "НДС прописью" or "Ставка НДС в тексте");
    }

    [Fact]
    public void Text_that_disagrees_with_the_table_is_critical()
    {
        const string text =
            "Стоимость выполненных работ составляет 100 (сто) рублей 00 копеек, в том числе НДС (22%) – 22 (двадцать два) рубля 00 копеек.";
        var report = Run(text, 1000m, 220m, 1220m);
        var gross = Assert.Single(report.Findings, finding => finding.CheckName == "Сумма в тексте акта");
        Assert.Equal(Severity.Critical, gross.Severity);
        Assert.Contains(TextMatch.FormatMoney(100m), gross.InAct);
        Assert.Contains(TextMatch.FormatMoney(1220m), gross.InAct);
        var vat = Assert.Single(report.Findings, finding => finding.CheckName == "НДС в тексте акта");
        Assert.Equal(Severity.Critical, vat.Severity);
        Assert.Contains("таблице", vat.Explanation);
    }

    [Fact]
    public void Wrong_words_and_wrong_rate_are_critical_even_when_the_digits_match_the_table()
    {
        const string text =
            "Стоимость выполненных работ составляет 1220 (сто) рублей 00 копеек, в том числе НДС (20%) – 220 (двести двадцать) рублей 00 копеек.";
        var report = Run(text, 1000m, 220m, 1220m);
        Assert.DoesNotContain(report.Findings, finding => finding.CheckName is "Сумма в тексте акта" or "НДС в тексте акта");
        var words = Assert.Single(report.Findings, finding => finding.CheckName == "Сумма прописью");
        Assert.Equal(Severity.Critical, words.Severity);
        Assert.Contains("сто", words.InAct);
        Assert.DoesNotContain(report.Findings, finding => finding.CheckName == "НДС прописью");
        var rate = Assert.Single(report.Findings, finding => finding.CheckName == "Ставка НДС в тексте");
        Assert.Equal(Severity.Critical, rate.Severity);
        Assert.Contains("20%", rate.InAct);
        Assert.Contains("22%", rate.InAct);
    }

    [Fact]
    public void Missing_sentence_is_critical_when_the_cost_table_is_present()
    {
        var report = Run(sentence: null, 1000m, 220m, 1220m);
        var missing = Assert.Single(report.Findings, finding => finding.CheckName == "Сумма в тексте акта");
        Assert.Equal(Severity.Critical, missing.Severity);
        Assert.Contains("нет фразы", missing.Explanation);
    }

    [Fact]
    public void Act_row_found_on_another_verd_element_is_critical()
    {
        var dir = Directory.CreateTempSubdirectory("proverkard-verd-row");
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
                sheet.Cell(1, 7).Value = "№ Элемента по ДГП";
                sheet.Cell(2, 1).Value = "7.50";
                sheet.Cell(2, 2).Value = "Схема организации движения";
                sheet.Cell(2, 4).Value = "960-07-1529-Д-ИС6-27-ПОДД1";
                sheet.Cell(2, 5).Value = "Схема организации движения";
                sheet.Cell(2, 7).Value = "7.10.2.1.11.3";
                sheet.Cell(3, 1).Value = "7.51";
                sheet.Cell(3, 2).Value = "Путепровод на ПК5734";
                sheet.Cell(3, 4).Value = "960-07-1529-Д-ИС6-27-ПОДД2";
                sheet.Cell(3, 5).Value = "Путепровод на ПК5734";
                sheet.Cell(3, 7).Value = "7.10.2.1.11.4";
                book.SaveAs(verd);
            }

            var act = Path.Combine(dir.FullName, "РД 7.51.docx");
            WriteAct(act, "7.51", "7.10.2.1.11.3", "960-07-1529-Д-ИС6-27-ПОДД2", "Схема организации движения", 1000m, 220m, 1220m, SampleSentence);
            var report = new CheckingEngine().Run(new CheckInput
            {
                VerdPath = verd,
                ActPaths = [act],
            });
            var row = Assert.Single(report.Findings, finding => finding.CheckName == "Строка акта в ВЭРД");
            Assert.Equal(Severity.Critical, row.Severity);
            Assert.Contains("7.51", row.InAct);
            Assert.Contains("7.10.2.1.11.3", row.InAct);
            Assert.Contains("7.50", row.InVerd);
            Assert.Contains("код 7.10.2.1.11.3 в ВЭРД стоит у элемента 7.50", row.Explanation);
            Assert.Contains("наименование строки акта в ВЭРД стоит под номером 7.50", row.Explanation);

            var same = Path.Combine(dir.FullName, "РД 7.50.docx");
            WriteAct(same, "7.50", "7.10.2.1.11.3", "960-07-1529-Д-ИС6-27-ПОДД1", "Схема организации движения", 1000m, 220m, 1220m,
                ActMoneyText.DigitsSentence(1220m, 220m));
            var matched = new CheckingEngine().Run(new CheckInput
            {
                VerdPath = verd,
                ActPaths = [same],
            });
            Assert.DoesNotContain(matched.Findings, finding => finding.CheckName == "Строка акта в ВЭРД");
        }
        finally
        {
            dir.Delete(recursive: true);
        }
    }

    private static CheckReport Run(string? sentence, decimal net, decimal vat, decimal gross)
    {
        var dir = Directory.CreateTempSubdirectory("proverkard-act-money");
        try
        {
            var act = Path.Combine(dir.FullName, "РД 7.51.docx");
            WriteAct(act, "7.51", "7.10.2.1.11.3", "960-07-0000-А-ТМ1", "Сооружения", net, vat, gross, sentence);
            return new CheckingEngine().Run(new CheckInput { ActPaths = [act] });
        }
        finally
        {
            dir.Delete(recursive: true);
        }
    }

    private static void WriteAct(
        string path,
        string element,
        string code,
        string cipher,
        string name,
        decimal net,
        decimal vat,
        decimal gross,
        string? sentence)
    {
        static string Money(decimal value)
        {
            var rounded = decimal.Round(value, 2, MidpointRounding.AwayFromZero);
            return rounded == decimal.Truncate(rounded)
                ? rounded.ToString("0", System.Globalization.CultureInfo.InvariantCulture)
                : rounded.ToString("0.00", System.Globalization.CultureInfo.InvariantCulture).Replace('.', ',');
        }

        using var doc = WordprocessingDocument.Create(path, WordprocessingDocumentType.Document);
        var main = doc.AddMainDocumentPart();
        var body = new Body(
            new Paragraph(new Run(new Text("АКТ № " + element))),
            new Paragraph(new Run(new Text("Стоимость выполненных работ составляет:"))),
            Table(
                ["№ тома", "Шифр", "Наименование документа"],
                [[code + " (" + element + ")", cipher, name]]),
            Table(
                ["Наименование", "Ед. изм.", "Стоимость без НДС", "НДС", "Стоимость с НДС"],
                [[code + " Рабочая документация. " + name, "шт", Money(net), Money(vat), Money(gross)]]));
        if (!string.IsNullOrWhiteSpace(sentence))
            body.Append(new Paragraph(new Run(new Text(sentence))));
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
