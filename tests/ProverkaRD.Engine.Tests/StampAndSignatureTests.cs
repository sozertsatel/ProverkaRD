using System.Security.Cryptography;
using ClosedXML.Excel;
using Org.BouncyCastle.Asn1;
using Org.BouncyCastle.Asn1.CryptoPro;
using Org.BouncyCastle.Asn1.Rosstandart;
using Org.BouncyCastle.Asn1.X509;
using Org.BouncyCastle.Cms;
using Org.BouncyCastle.Crypto;
using Org.BouncyCastle.Crypto.Generators;
using Org.BouncyCastle.Crypto.Operators;
using Org.BouncyCastle.Crypto.Parameters;
using Org.BouncyCastle.Math;
using Org.BouncyCastle.Security;
using Org.BouncyCastle.X509;
using ProverkaRD.Engine.Parsing;
using QuestPDF.Fluent;
using QuestPDF.Infrastructure;
using BcAttribute = Org.BouncyCastle.Asn1.Cms.Attribute;
using BcAttributeTable = Org.BouncyCastle.Asn1.Cms.AttributeTable;

namespace ProverkaRD.Engine.Tests;

public class StampAndSignatureTests
{
    static StampAndSignatureTests()
    {
        QuestPDF.Settings.License = LicenseType.Community;
    }

    [Fact]
    public void Form3_grid_reads_surname_column_of_each_role_row()
    {
        const double mm = 72.0 / 25.4;
        var pageWidth = 210 * mm;
        var pageHeight = 297 * mm;
        var stampLeft = pageWidth - 5 * mm - 185 * mm;
        var stampBottom = 5 * mm;
        var words = new List<StampForm3.StampWord>();

        void Role(int row, string role, string surname)
        {
            var top = stampBottom + 55 * mm - row * 5 * mm;
            var bottom = top - 5 * mm;
            words.Add(new StampForm3.StampWord(role, stampLeft + 1 * mm, bottom + 1 * mm, stampLeft + 16 * mm, top - 1 * mm));
            words.Add(new StampForm3.StampWord(surname, stampLeft + 22 * mm, bottom + 1 * mm, stampLeft + 38 * mm, top - 1 * mm));
            words.Add(new StampForm3.StampWord("01.02.24", stampLeft + 56 * mm, bottom + 1 * mm, stampLeft + 64 * mm, top - 1 * mm));
        }

        Role(5, "Разраб.", "Фролов");
        Role(6, "Пров.", "Белкина");
        Role(7, "ГИП", "Гордеев");
        Role(8, "Нач.отд.", "Кузнецов");
        Role(9, "Н.контр.", "Лесной");
        Role(10, "Утв.", "Морозов");
        Role(3, "Согласован", "Котельная");
        words.Add(new StampForm3.StampWord("Разраб.", 40 * mm, 200 * mm, 55 * mm, 205 * mm));
        words.Add(new StampForm3.StampWord("Зайцев", 62 * mm, 200 * mm, 80 * mm, 205 * mm));
        words.Add(new StampForm3.StampWord("Обозначение", stampLeft + 70 * mm, stampBottom + 50 * mm, stampLeft + 110 * mm, stampBottom + 54 * mm));

        var cells = StampForm3.ReadCells(words, pageWidth, pageHeight);
        Assert.Contains(cells, cell => cell.Contains("Фролов", StringComparison.Ordinal) && !cell.Contains("01.02", StringComparison.Ordinal));
        Assert.Contains(cells, cell => cell.Contains("Белкина", StringComparison.Ordinal));
        Assert.Contains(cells, cell => cell.Contains("Гордеев", StringComparison.Ordinal));
        Assert.Contains(cells, cell => cell.Contains("Кузнецов", StringComparison.Ordinal));
        Assert.Contains(cells, cell => cell.Contains("Лесной", StringComparison.Ordinal));
        Assert.Contains(cells, cell => cell.Contains("Морозов", StringComparison.Ordinal));
        Assert.Contains(cells, cell => cell.Contains("Котельная", StringComparison.Ordinal));
        Assert.DoesNotContain(cells, cell => cell.Contains("Зайцев", StringComparison.Ordinal));
        Assert.DoesNotContain(cells, cell => cell.Contains("Обозначение", StringComparison.Ordinal));
    }

    [Fact]
    public void Coordinate_surnames_merge_with_text_and_stop_after_the_vor()
    {
        var surnames = RdParser.CollectSurnames(
        [
            new RdParser.RdPage(1, "Разраб. Сидоров И.И.", "", CoordinateCells: ["Кузнецов"]),
            new RdParser.RdPage(3, "Чертёж", "Пров. Павлову В.Г.", CoordinateCells: ["Котельная"]),
            new RdParser.RdPage(4, "Ведомость объемов работ", "Исп. Фролов Я.Я."),
            new RdParser.RdPage(5, "Письмо", "Утв. Орлов О.О.", CoordinateCells: ["Зайцев"]),
        ]);
        Assert.Contains(surnames, name => name.Contains("Сидоров", StringComparison.Ordinal));
        Assert.Contains(surnames, name => name.Contains("Павлову", StringComparison.Ordinal));
        Assert.Contains(surnames, name => name.Contains("Фролов", StringComparison.Ordinal));
        Assert.Contains(surnames, name => name.Contains("Кузнецов", StringComparison.Ordinal));
        Assert.DoesNotContain(surnames, name => name.Contains("Котельная", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(surnames, name => name.Contains("Орлов", StringComparison.Ordinal));
        Assert.DoesNotContain(surnames, name => name.Contains("Зайцев", StringComparison.Ordinal));
    }

    [Fact]
    public void Pdf_stamp_surnames_come_from_word_boxes_and_keep_the_title_surname()
    {
        var dir = Directory.CreateTempSubdirectory("proverkard-stamp-pdf");
        var path = Path.Combine(dir.FullName, "100-20-3000-А-КЖ1.pdf");
        try
        {
            WriteStampPdf(path,
            [
                (5, "Разраб.", "Фролов"),
                (6, "Пров.", "Белкина"),
                (7, "ГИП", "Гордеев"),
                (9, "Н.контр.", "Лесной"),
                (10, "Утв.", "Морозов"),
            ],
            [
                "РАБОЧАЯ ДОКУМЕНТАЦИЯ",
                "Площадка сооружений",
                "100-20-3000-А-КЖ1",
                "Разраб. Волгин А.А.",
                "Этап 4.10 Сооружения",
                "Инв. № подл.",
            ]);
            var unsigned = RdParser.Parse(path);
            Assert.Contains(unsigned.Surnames, name => name.Contains("Волгин", StringComparison.Ordinal));
            Assert.DoesNotContain(unsigned.Surnames, name => name == "Фролов");
            Assert.Equal("4.10", unsigned.ElementNo);

            var bytes = File.ReadAllBytes(path);
            var now = DateTime.UtcNow;
            var (keys, cert) = GostCertificates.Issue("Фролов", "Иван Иванович", now.AddDays(-10), now.AddDays(10));
            File.WriteAllBytes(path + ".sig", GostCertificates.Sign(bytes, keys.Private, cert, now, includeTime: true));
            var rd = RdParser.Parse(path);
            Assert.Contains(rd.Surnames, name => name == "Фролов");
            Assert.Contains(rd.Surnames, name => name.Contains("Волгин", StringComparison.Ordinal));
            Assert.DoesNotContain(rd.Surnames, name => name == "Белкина");
            Assert.DoesNotContain(rd.Surnames, name => name == "Гордеев");
            Assert.Equal("4.10", rd.ElementNo);
        }
        finally
        {
            dir.Delete(recursive: true);
        }
    }

    [Fact]
    public void Coordinate_title_words_are_not_surnames_and_adjective_endings_need_initials_or_a_certificate()
    {
        var cells = new[]
        {
            "Детали", "Соединительные", "Все", "Рабочие", "Проектируемый",
            "Бельский И.И.", "Полевая А.А.", "Долгих И.И.", "Фролов",
        };
        var surnames = RdParser.CollectSurnames(
        [
            new RdParser.RdPage(1, "Разраб. Волгин А.А.", "", CoordinateCells: cells),
        ]);
        Assert.Contains(surnames, name => name.Contains("Волгин", StringComparison.Ordinal));
        Assert.Contains(surnames, name => name.Contains("Бельский", StringComparison.Ordinal));
        Assert.Contains(surnames, name => name.Contains("Полевая", StringComparison.Ordinal));
        Assert.Contains(surnames, name => name.Contains("Долгих", StringComparison.Ordinal));
        Assert.Contains(surnames, name => name == "Фролов");
        Assert.DoesNotContain(surnames, name => name.Contains("Детали", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(surnames, name => name.Contains("Соединительные", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(surnames, name => name.Equals("Все", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(surnames, name => name.Contains("Рабочие", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(surnames, name => name.Contains("Проектируемый", StringComparison.OrdinalIgnoreCase));

        var bareAdjective = RdParser.CollectSurnames(
        [
            new RdParser.RdPage(1, "Титул", "", CoordinateCells: ["Бельский", "Полевая", "Долгих"]),
        ]);
        Assert.DoesNotContain(bareAdjective, name => name.Contains("Бельский", StringComparison.Ordinal));
        Assert.Contains(bareAdjective, name => name.Contains("Полевая", StringComparison.Ordinal));
        Assert.Contains(bareAdjective, name => name.Contains("Долгих", StringComparison.Ordinal));
    }

    [Fact]
    public void False_stamp_words_are_not_critical_and_a_real_surname_with_initials_still_is()
    {
        var dir = Directory.CreateTempSubdirectory("proverkard-false-stamp");
        var path = Path.Combine(dir.FullName, "100-20-3000-А-КЖ1.pdf");
        try
        {
            WriteStampPdf(path,
            [
                (5, "Разраб.", "Детали"),
                (6, "Пров.", "Соединительные"),
                (7, "ГИП", "Все"),
                (8, "Нач.отд.", "Рабочие"),
                (9, "Н.контр.", "Проектируемый"),
                (10, "Утв.", "Бельский И.И."),
            ],
            [
                "РАБОЧАЯ ДОКУМЕНТАЦИЯ",
                "100-20-3000-А-КЖ1",
                "Разраб. Волгин А.А.",
                "Пров. Бельский И.И.",
                "Этап 4.10 Сооружения",
            ]);
            var bytes = File.ReadAllBytes(path);
            var now = DateTime.UtcNow;
            var (keys, cert) = GostCertificates.Issue("Фролов", "Иван Иванович", now.AddDays(-5), now.AddDays(20));
            File.WriteAllBytes(path + ".sig", GostCertificates.Sign(bytes, keys.Private, cert, now, includeTime: true));
            var report = new CheckingEngine().Run(new CheckInput { RdPaths = [path] });
            Assert.Contains(report.Findings, finding =>
                finding.CheckName == "Фамилия без подписи"
                && finding.Severity == Severity.Critical
                && (finding.ValueA.Contains("Бельский", StringComparison.Ordinal) || finding.InRd.Contains("Бельский", StringComparison.Ordinal)));
            Assert.Contains(report.Findings, finding =>
                finding.CheckName == "Фамилия без подписи" && finding.ValueA.Contains("Волгин", StringComparison.Ordinal));
            Assert.DoesNotContain(report.Findings, finding =>
                finding.CheckName == "Фамилия без подписи"
                && (finding.ValueA.Contains("Детали", StringComparison.OrdinalIgnoreCase)
                    || finding.ValueA.Contains("Соединительные", StringComparison.OrdinalIgnoreCase)
                    || finding.ValueA.Contains("Рабочие", StringComparison.OrdinalIgnoreCase)
                    || finding.ValueA.Contains("Проектируемый", StringComparison.OrdinalIgnoreCase)
                    || finding.ValueA.Equals("Все", StringComparison.OrdinalIgnoreCase)));

            File.Delete(path + ".sig");
            var (ownKeys, ownCert) = GostCertificates.Issue("Бельский", "Пётр", now.AddDays(-5), now.AddDays(20));
            WriteStampPdf(path, [(5, "Разраб.", "Бельский"), (6, "Пров.", "Полевая А.А."), (7, "ГИП", "Долгих")],
            [
                "РАБОЧАЯ ДОКУМЕНТАЦИЯ",
                "100-20-3000-А-КЖ1",
                "Этап 4.10 Сооружения",
            ]);
            bytes = File.ReadAllBytes(path);
            File.WriteAllBytes(path + ".sig", GostCertificates.Sign(bytes, ownKeys.Private, ownCert, now, includeTime: true));
            var kept = RdParser.Parse(path);
            Assert.Contains(kept.Surnames, name => name.Contains("Бельский", StringComparison.Ordinal));
            Assert.Contains(kept.Surnames, name => name.Contains("Полевая", StringComparison.Ordinal));
            Assert.DoesNotContain(kept.Surnames, name => name.Contains("Долгих", StringComparison.Ordinal));
        }
        finally
        {
            dir.Delete(recursive: true);
        }
    }

    [Fact]
    public void Stray_pdf_is_a_warning_in_the_registry_and_in_the_summary()
    {
        var dir = Directory.CreateTempSubdirectory("proverkard-stray-pdf");
        var path = Path.Combine(dir.FullName, "Письмо.pdf");
        try
        {
            WriteStampPdf(path, [(5, "Разраб.", "Детали")], ["Сопроводительное письмо без шифра"]);
            var bytes = File.ReadAllBytes(path);
            var now = DateTime.UtcNow;
            var (keys, cert) = GostCertificates.Issue("Фролов", "Иван", now.AddDays(-5), now.AddDays(20));
            File.WriteAllBytes(path + ".sig", GostCertificates.Sign(bytes, keys.Private, cert, now, includeTime: true));

            var report = new CheckingEngine().Run(new CheckInput { RdPaths = [path] });
            var stray = Assert.Single(report.Findings, finding => finding.CheckName == "Посторонний файл");
            Assert.Equal(Severity.Warning, stray.Severity);
            Assert.DoesNotContain(report.Findings, finding => finding.CheckName == "Электронная подпись");
            Assert.DoesNotContain(report.Findings, finding => finding.CheckName == "Фамилия без подписи");
            var row = Assert.Single(report.Registry);
            Assert.Equal(Severity.Warning, row.SignatureStatus);
            Assert.Equal(Severity.Warning, row.Overall);
            Assert.NotEqual(Severity.Critical, row.CertStatus);

            var reportPath = Path.Combine(dir.FullName, "report.xlsx");
            ReportWriter.Write(report, reportPath, "0.1.2");
            using var book = new XLWorkbook(reportPath);
            var summary = book.Worksheet("Сводка");
            Assert.Contains("Предупреждения: 1", summary.Cell(2, 1).GetString());
            Assert.Equal(1d, summary.Cell(6, 5).GetDouble());
            var opis = book.Worksheet("Реестр комплекта");
            Assert.Equal("Файл PDF", opis.Cell(4, 5).GetString());
            Assert.Equal("Письмо.pdf", opis.Cell(5, 5).GetString());
            Assert.Contains("Комплектов: 1", opis.Cell(6, 5).GetString());
        }
        finally
        {
            dir.Delete(recursive: true);
        }
    }

    [Fact]
    public void Stamp_surname_matches_certificate_fio_despite_yo_case_spaces_and_initials()
    {
        Assert.True(SignerNames.Matches("Лёгкий", "Легкий"));
        Assert.True(SignerNames.Matches("Фролов", "Фролов И.И."));
        Assert.True(SignerNames.Matches("Фролов", "  фролов   и.  и. "));
        Assert.True(SignerNames.Matches("Фролов", "Фролов Иван Иванович"));
        Assert.True(SignerNames.Matches("Павлову", "Павлов"));
        Assert.True(SignerNames.Matches("Фролов И.И.", "Фролов"));
        Assert.False(SignerNames.Matches("Фролов А.Б.", "Фролов И.И."));
        Assert.False(SignerNames.Matches("Морозов", "Белкина"));

        var matched = SignerAudit.Check(["Лёгкий"], ["Легкий И.И."], true, null, []);
        Assert.Equal(Severity.Ok, matched.Status);
        var conflict = SignerAudit.Check(["Фролов А.Б."], ["Фролов И.И."], true, null, []);
        Assert.Equal("Фролов А.Б.", Assert.Single(conflict.Missing));
    }

    [Fact]
    public void Gost2012_detached_cms_verifies_valid_other_file_corrupt_and_expired()
    {
        var content = "synthetic-rd-bytes-v011"u8.ToArray();
        var other = "other-file-bytes-v011"u8.ToArray();
        var now = DateTime.UtcNow;
        var (keys, cert) = GostCertificates.Issue("Фролов", "Иван Иванович", now.AddDays(-30), now.AddDays(30));
        var valid = GostCertificates.Sign(content, keys.Private, cert, now, includeTime: true);

        Assert.True(SignatureParser.Covers(content, valid));
        Assert.False(SignatureParser.Covers(other, valid));

        var corrupt = GostCertificates.CorruptSignatureValue(content, valid, cert);
        Assert.False(SignatureParser.Covers(content, corrupt));

        var dir = Directory.CreateTempSubdirectory("proverkard-gost");
        try
        {
            var path = Path.Combine(dir.FullName, "комплект.pdf");
            File.WriteAllBytes(path, content);
            File.WriteAllBytes(path + ".sig", GostCertificates.Sign(other, keys.Private, cert, now, includeTime: true));
            var located = SignatureParser.Locate(path);
            Assert.NotNull(located);
            Assert.True(located.Value.ExactRejected);
            Assert.False(SignatureParser.Covers(content, File.ReadAllBytes(path + ".sig")));

            var sig = Path.Combine(dir.FullName, "valid.sig");
            File.WriteAllBytes(sig, valid);
            var read = SignatureParser.Read(sig);
            Assert.Null(read.Error);
            Assert.Contains(read.Surnames, name => name == "Фролов");
            var person = Assert.Single(read.People);
            Assert.Equal("Фролов", person.Surname);
            Assert.Equal("Иван Иванович", person.GivenName);
            Assert.Contains("Фролов", person.CommonName, StringComparison.Ordinal);
            Assert.Contains("Иван", person.DisplayName, StringComparison.Ordinal);
            Assert.Equal(CertTimeStatus.Valid, person.TimeStatus);
            Assert.Equal(Severity.Ok, read.CertStatus);
            Assert.Contains("действовал", read.CertValidityText, StringComparison.Ordinal);

            var (expiredKeys, expiredCert) = GostCertificates.Issue(
                "Фролов", "Иван Иванович", new DateTime(2019, 1, 1, 0, 0, 0, DateTimeKind.Utc), new DateTime(2020, 1, 1, 0, 0, 0, DateTimeKind.Utc));
            var expiredSig = GostCertificates.Sign(content, expiredKeys.Private, expiredCert, new DateTime(2024, 6, 1, 12, 0, 0, DateTimeKind.Utc), includeTime: true);
            Assert.True(SignatureParser.Covers(content, expiredSig));
            var expiredPath = Path.Combine(dir.FullName, "expired.sig");
            File.WriteAllBytes(expiredPath, expiredSig);
            var expired = SignatureParser.Read(expiredPath);
            Assert.Equal(CertTimeStatus.Expired, Assert.Single(expired.People).TimeStatus);
            Assert.Equal(Severity.Critical, expired.CertStatus);
            Assert.Contains("истёк", expired.CertValidityText, StringComparison.Ordinal);

            var (futureKeys, futureCert) = GostCertificates.Issue(
                "Белкина", "Мария", new DateTime(2035, 1, 1, 0, 0, 0, DateTimeKind.Utc), new DateTime(2036, 1, 1, 0, 0, 0, DateTimeKind.Utc));
            var futureSig = GostCertificates.Sign(content, futureKeys.Private, futureCert, new DateTime(2024, 6, 1, 12, 0, 0, DateTimeKind.Utc), includeTime: true);
            Assert.True(SignatureParser.Covers(content, futureSig));
            File.WriteAllBytes(Path.Combine(dir.FullName, "future.sig"), futureSig);
            var future = SignatureParser.Read(Path.Combine(dir.FullName, "future.sig"));
            Assert.Equal(CertTimeStatus.NotYetValid, Assert.Single(future.People).TimeStatus);
            Assert.Equal(Severity.Critical, future.CertStatus);

            var undated = GostCertificates.Sign(content, keys.Private, cert, null, includeTime: false);
            Assert.True(SignatureParser.Covers(content, undated));
            File.WriteAllBytes(Path.Combine(dir.FullName, "undated.sig"), undated);
            var missingTime = SignatureParser.Read(Path.Combine(dir.FullName, "undated.sig"));
            Assert.Equal(CertTimeStatus.SigningTimeMissing, Assert.Single(missingTime.People).TimeStatus);
            Assert.Equal(Severity.Warning, missingTime.CertStatus);
            Assert.Contains("нет времени подписания", missingTime.CertValidityText, StringComparison.Ordinal);
        }
        finally
        {
            dir.Delete(recursive: true);
        }
    }

    [Fact]
    public void Registry_sheet_lists_each_pdf_with_stamp_fio_and_cert_status()
    {
        var dir = Directory.CreateTempSubdirectory("proverkard-registry");
        var path = Path.Combine(dir.FullName, "100-20-3000-А-КЖ1.pdf");
        try
        {
            WriteStampPdf(path, [(5, "Разраб.", "Фролов")],
            [
                "РАБОЧАЯ ДОКУМЕНТАЦИЯ",
                "Площадка сооружений",
                "100-20-3000-А-КЖ1",
                "Этап 4.10 Сооружения",
                "Инв. № подл.",
            ]);
            var bytes = File.ReadAllBytes(path);
            var now = DateTime.UtcNow;
            var (keys, cert) = GostCertificates.Issue("Фролов", "Иван Иванович", now.AddDays(-10), now.AddDays(10));
            File.WriteAllBytes(path + ".sig", GostCertificates.Sign(bytes, keys.Private, cert, now, includeTime: true));

            var report = new CheckingEngine().Run(new CheckInput { RdPaths = [path] });
            var row = Assert.Single(report.Registry);
            Assert.Equal("100-20-3000-А-КЖ1.pdf", row.FileName);
            Assert.Contains("100-20-3000-А-КЖ1", row.Cipher, StringComparison.Ordinal);
            Assert.Contains("4.10", row.Element, StringComparison.Ordinal);
            Assert.Contains("Фролов", row.Surnames, StringComparison.Ordinal);
            Assert.Contains("Фролов", row.Signers, StringComparison.Ordinal);
            Assert.Contains("Иван", row.Signers, StringComparison.Ordinal);
            Assert.Contains("действовал", row.CertValidity, StringComparison.Ordinal);
            Assert.Equal(Severity.Ok, row.CertStatus);
            Assert.Equal(Severity.Ok, row.SignatureStatus);
            Assert.Equal(Severity.Ok, row.Overall);
            Assert.DoesNotContain(report.Findings, finding => finding.CheckName == "Срок сертификата");
            Assert.DoesNotContain(report.Findings, finding => finding.CheckName == "Фамилия без подписи");

            var (expiredKeys, expiredCert) = GostCertificates.Issue(
                "Фролов", "Иван Иванович", new DateTime(2019, 1, 1, 0, 0, 0, DateTimeKind.Utc), new DateTime(2020, 1, 1, 0, 0, 0, DateTimeKind.Utc));
            File.WriteAllBytes(path + ".sig", GostCertificates.Sign(bytes, expiredKeys.Private, expiredCert, new DateTime(2024, 6, 1, 0, 0, 0, DateTimeKind.Utc), includeTime: true));
            var expiredReport = new CheckingEngine().Run(new CheckInput { RdPaths = [path] });
            var expiredRow = Assert.Single(expiredReport.Registry);
            Assert.Equal(Severity.Critical, expiredRow.CertStatus);
            Assert.Equal(Severity.Ok, expiredRow.SignatureStatus);
            Assert.Equal(Severity.Critical, expiredRow.Overall);
            Assert.Contains(expiredReport.Findings, finding =>
                finding.CheckName == "Срок сертификата"
                && finding.Severity == Severity.Critical
                && finding.InRd.Contains("истёк", StringComparison.Ordinal));

            var reportPath = Path.Combine(dir.FullName, "report.xlsx");
            ReportWriter.Write(report, reportPath, "0.1.1");
            using var book = new XLWorkbook(reportPath);
            var sheet = book.Worksheet("Реестр комплекта");
            Assert.Equal(SeverityPalette.FontName, sheet.Style.Font.FontName);
            Assert.Contains("Опись комплекта", sheet.Cell(1, 1).GetString());
            Assert.Equal("№ п/п", sheet.Cell(4, 1).GetString());
            Assert.Equal("Элемент", sheet.Cell(4, 2).GetString());
            Assert.Equal("Шифр комплекта", sheet.Cell(4, 3).GetString());
            Assert.Equal("Наименование", sheet.Cell(4, 4).GetString());
            Assert.Equal("Файл PDF", sheet.Cell(4, 5).GetString());
            Assert.Equal("Подпись (.sig)", sheet.Cell(4, 6).GetString());
            Assert.Equal("Подписанты", sheet.Cell(4, 7).GetString());
            Assert.Equal(4, sheet.SheetView.SplitRow);
            Assert.Equal(1, sheet.PageSetup.PagesWide);
            Assert.Equal(XLBorderStyleValues.Thick, sheet.Cell(4, 1).Style.Border.TopBorder);
            Assert.Equal(XLBorderStyleValues.Thick, sheet.Cell(4, 7).Style.Border.RightBorder);
            Assert.Equal("100-20-3000-А-КЖ1.pdf", sheet.Cell(5, 5).GetString());
            Assert.Contains("Фролов", sheet.Cell(5, 7).GetString(), StringComparison.Ordinal);
            Assert.Contains("Иван", sheet.Cell(5, 7).GetString(), StringComparison.Ordinal);
            Assert.Contains("Комплектов: 1", sheet.Cell(6, 5).GetString());
            Assert.Contains("Подписано: 1", sheet.Cell(6, 6).GetString());
            Assert.Equal(XLBorderStyleValues.Thick, sheet.Cell(5, 1).Style.Border.LeftBorder);
            Assert.Equal(XLBorderStyleValues.Thick, sheet.Cell(5, 7).Style.Border.RightBorder);
        }
        finally
        {
            dir.Delete(recursive: true);
        }
    }

    private static void WriteStampPdf(string path, (int Row, string Role, string Surname)[] rows, IReadOnlyList<string> title)
    {
        const float mm = 72f / 25.4f;
        float pageW = 210 * mm;
        float pageH = 297 * mm;
        float stampLeft = (210 - 5 - 185) * mm;
        float stampBottom = 5 * mm;
        float RowBaseline(int index) => stampBottom + (11 - index) * 5 * mm - 3.6f * mm;

        Document.Create(container =>
        {
            container.Page(page =>
            {
                page.Size(pageW, pageH);
                page.Margin(0);
                page.DefaultTextStyle(style => style.FontFamily("DejaVu Sans").FontSize(8));
                page.Content().Layers(layers =>
                {
                    layers.PrimaryLayer().Text("");
                    void Put(string text, float x, float yFromBottom)
                    {
                        layers.Layer().TranslateX(x).TranslateY(pageH - yFromBottom - 8).Text(text);
                    }

                    foreach (var (row, role, surname) in rows)
                    {
                        Put(role, stampLeft + 1 * mm, RowBaseline(row));
                        Put(surname, stampLeft + 21 * mm, RowBaseline(row));
                    }

                    var y = 250 * mm;
                    foreach (var line in title)
                    {
                        Put(line, 25 * mm, y);
                        y -= 8 * mm;
                    }
                });
            });
        }).GeneratePdf(path);
    }
}

file static class GostCertificates
{
    private const string SignatureOid = "1.2.643.7.1.1.3.2";
    private const string DigestOid = "1.2.643.7.1.1.2.2";

    public static (AsymmetricCipherKeyPair Keys, X509Certificate Cert) Issue(string surname, string given, DateTime notBefore, DateTime notAfter)
    {
        var random = new SecureRandom();
        var curve = RosstandartObjectIdentifiers.id_tc26_gost_3410_12_256_paramSetA;
        var parameters = ECGost3410NamedCurves.GetByOid(curve);
        var domain = new ECGost3410Parameters(
            new ECNamedDomainParameters(curve, parameters),
            curve,
            RosstandartObjectIdentifiers.id_tc26_gost_3411_12_256,
            null);
        var generator = new ECKeyPairGenerator();
        generator.Init(new ECKeyGenerationParameters(domain, random));
        var keys = generator.GenerateKeyPair();
        var name = new X509Name(
            [X509Name.CN, X509Name.Surname, X509Name.GivenName, X509Name.C],
            [surname + " " + given, surname, given, "RU"]);
        var builder = new X509V3CertificateGenerator();
        builder.SetSerialNumber(BigInteger.ValueOf(Math.Abs(surname.GetHashCode(StringComparison.Ordinal)) + 17));
        builder.SetIssuerDN(name);
        builder.SetSubjectDN(name);
        builder.SetNotBefore(notBefore);
        builder.SetNotAfter(notAfter);
        builder.SetPublicKey(keys.Public);
        var cert = builder.Generate(new Asn1SignatureFactory(SignatureOid, keys.Private, random));
        return (keys, cert);
    }

    public static byte[] Sign(byte[] content, AsymmetricKeyParameter privateKey, X509Certificate cert, DateTime? signingTime, bool includeTime)
    {
        var cms = new CmsSignedDataGenerator();
        if (!includeTime)
            cms.AddSigner(privateKey, cert, SignatureOid, DigestOid, new WithoutSigningTime(), null);
        else
        {
            var when = DateTime.SpecifyKind(signingTime ?? DateTime.UtcNow, DateTimeKind.Utc);
            var table = new BcAttributeTable(new Dictionary<DerObjectIdentifier, object>
            {
                [Org.BouncyCastle.Asn1.Cms.CmsAttributes.SigningTime] = new BcAttribute(
                    Org.BouncyCastle.Asn1.Cms.CmsAttributes.SigningTime,
                    new DerSet(new Org.BouncyCastle.Asn1.Cms.Time(when))),
            });
            cms.AddSigner(privateKey, cert, SignatureOid, DigestOid, table, null);
        }

        cms.AddCertificate(cert);
        return cms.Generate(new CmsProcessableByteArray(content), encapsulate: false).GetEncoded();
    }

    public static byte[] CorruptSignatureValue(byte[] content, byte[] signature, X509Certificate cert)
    {
        var parsed = new CmsSignedData(new CmsProcessableByteArray(content), signature);
        var signer = parsed.GetSignerInfos().GetSigners().Cast<SignerInformation>().Single();
        var value = signer.GetSignature();
        var at = LastIndexOf(signature, value);
        if (at < 0 || value.Length < 8)
            throw new InvalidOperationException("В CMS не найдено значение подписи.");
        var digestBefore = signer.SignedAttributes[Org.BouncyCastle.Asn1.Cms.CmsAttributes.MessageDigest].AttrValues[0].GetEncoded();
        var copy = (byte[])signature.Clone();
        copy[at + value.Length / 2] ^= 0xFF;
        var broken = new CmsSignedData(new CmsProcessableByteArray(content), copy);
        var again = broken.GetSignerInfos().GetSigners().Cast<SignerInformation>().Single();
        var digestAfter = again.SignedAttributes[Org.BouncyCastle.Asn1.Cms.CmsAttributes.MessageDigest].AttrValues[0].GetEncoded();
        if (!digestBefore.AsSpan().SequenceEqual(digestAfter))
            throw new InvalidOperationException("Порча подписи задела хэш содержимого.");
        var verified = false;
        try
        {
            verified = again.Verify(cert.GetPublicKey());
        }
        catch (CmsException)
        {
            verified = false;
        }

        if (verified)
            throw new InvalidOperationException("Испорченная подпись всё ещё проверяется.");
        return copy;
    }

    private static int LastIndexOf(byte[] haystack, byte[] needle)
    {
        for (var index = haystack.Length - needle.Length; index >= 0; index--)
        {
            if (haystack.AsSpan(index, needle.Length).SequenceEqual(needle))
                return index;
        }

        return -1;
    }

    private sealed class WithoutSigningTime : CmsAttributeTableGenerator
    {
        public BcAttributeTable GetAttributes(IDictionary<CmsAttributeTableParameter, object> parameters)
        {
            var table = new DefaultSignedAttributeTableGenerator().GetAttributes(parameters);
            var copy = new Dictionary<DerObjectIdentifier, object>();
            foreach (var pair in table.ToDictionary())
            {
                if (pair.Key.Equals(Org.BouncyCastle.Asn1.Cms.CmsAttributes.SigningTime))
                    continue;
                copy[pair.Key] = pair.Value;
            }

            return new BcAttributeTable(copy);
        }
    }
}
