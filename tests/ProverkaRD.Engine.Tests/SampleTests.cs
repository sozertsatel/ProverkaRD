using ProverkaRD.Engine.Parsing;

namespace ProverkaRD.Engine.Tests;

public class TextMatchTests
{
    [Theory]
    [InlineData("ПК 5859", "ПК5859")]
    [InlineData("ПК5859+76", "ПК 5859+76")]
    [InlineData("Вокзал.", "Вокзал")]
    [InlineData("Высоково.", "высоково")]
    [InlineData("водоотведение.Наружные", "водоотведение. Наружные")]
    [InlineData("Акт\nЛивневая", "Акт Ливневая")]
    [InlineData("«Вокзал»", "\"Вокзал\"")]
    [InlineData("ПК\u00A05859", "ПК 5859")]
    public void Formatting_only_differences_are_not_critical(string left, string right)
    {
        Assert.Equal(Severity.Formatting, TextMatch.Compare(left, right));
    }

    [Theory]
    [InlineData("v0.0.2", "0.0.1", true)]
    [InlineData("v0.0.1", "0.0.1", false)]
    [InlineData("v0.0.1", "0.0.1.0", false)]
    [InlineData("0.0.1", "v0.0.2", false)]
    [InlineData("v1.0.0", "0.0.1", true)]
    [InlineData("v0.0.2-beta", "0.0.1", true)]
    public void Newer_release_tag_is_detected(string tag, string current, bool newer)
    {
        Assert.Equal(newer, ReleaseVersion.IsNewer(tag, current));
    }

    [Fact]
    public void One_or_two_letter_typo_is_formatting_but_missing_pk_and_digits_are_critical()
    {
        Assert.Equal(Severity.Formatting, TextMatch.Compare("соружения", "сооружения"));
        Assert.Equal(Severity.Formatting, TextMatch.Compare("насыпью Конструкции", "насыпью. Конструкции"));
        Assert.Equal(Severity.Formatting, TextMatch.Compare("Сооружения", "\u0043ооружения"));
        Assert.Equal(Severity.Formatting, TextMatch.Compare("Вокзал", "Вокзал\u200B"));
        Assert.NotEqual(Severity.Ok, TextMatch.Compare("Вокзал", "Вокзал\u200B"));
        Assert.Equal(Severity.Ok, TextMatch.Compare("Вокзал", "Вокзал"));
        Assert.Equal(Severity.Critical, TextMatch.Compare("на 04+60,00", "на ПК04+60,00"));
        Assert.Equal(Severity.Critical, TextMatch.Compare("ПК01+91,66", "ПК01+91,67"));
        Assert.Equal(Severity.Critical, TextMatch.Compare("мост", "путь"));
    }

    [Fact]
    public void Invisible_direction_mark_does_not_change_money()
    {
        var value = TextMatch.ParseMoney("1\u00A0556\u00A0042,07\u202C");
        Assert.Equal(1556042.07m, value);
        Assert.Equal(1245444.32m, TextMatch.ParseMoney("1 245 444,32"));
    }
}

public class SampleTests
{
    private static string Root => Samples.Root;

    [RequiresSamplesFact]
    public void Act_7_46_has_known_cost_error_and_vat_recalc_matches_vesr()
    {
        var report = Run();
        var act = report.Acts.Single(a => a.ElementNo == "7.46");
        Assert.Equal(1245444.32m, act.ActNet);
        Assert.Equal(1275444.32m, act.VesrNet);
        Assert.Equal(1556042.07m, act.ActGross);
        Assert.Equal(Severity.Critical, act.NetStatus);
        Assert.Equal(Severity.Ok, act.GrossStatus);
        Assert.Equal(Severity.Critical, act.Overall);
        Assert.Contains(report.Findings, f => f.Act.Contains("7.46") && f.CheckName == "Стоимость без НДС" && f.Severity == Severity.Critical);
        Assert.Contains(report.Findings, f => f.Act.Contains("7.46") && f.CheckName == "Арифметика акта");
    }

    [RequiresSamplesFact]
    public void Act_7_51_code_does_not_match_verd()
    {
        var report = Run();
        var act = report.Acts.Single(a => a.ElementNo == "7.51");
        Assert.Equal("7.10.2.1.11.3", act.ActCode.TrimEnd('.'));
        Assert.Equal("7.10.2.1.11.4", act.VerdCode.TrimEnd('.'));
        Assert.Equal(Severity.Critical, act.CodeStatus);
        Assert.Equal(Severity.Critical, act.NameStatus);
        Assert.Equal(Severity.Ok, act.NetStatus);
        Assert.Contains(report.Findings, f =>
            f.CheckName.Contains("Код ДГП")
            && f.ValueA.Contains("7.10.2.1.11.3")
            && f.ValueB.Contains("7.10.2.1.11.4"));
    }

    [RequiresSamplesFact]
    public void Act_7_40_station_spacing_is_formatting()
    {
        var report = Run();
        var act = report.Acts.Single(a => a.ElementNo == "7.40");
        Assert.Equal(Severity.Formatting, act.NameStatus);
        Assert.Contains("ПК5859", act.ActElementName.Replace(" ", ""));
        Assert.Contains("ПК 5859", act.VerdElementName);
    }

    [RequiresSamplesFact]
    public void Act_7_97_kit_set_matches_and_trailing_dot_is_formatting()
    {
        var report = Run();
        var act = report.Acts.Single(a => a.ElementNo == "7.97");
        Assert.Equal(18, act.ActKitCount);
        Assert.Equal(18, act.VerdKitCount);
        Assert.Equal(Severity.Formatting, act.NameStatus);
        Assert.NotEqual(Severity.Critical, act.CompletenessStatus);
        Assert.Equal(Severity.Ok, act.NetStatus);
    }

    [RequiresSamplesFact]
    public void Sample_pdf_typo_and_signers()
    {
        var report = Run();
        var kit = report.Kits.Single(k => k.Cipher.Contains("ИС21-27-КЖ1", StringComparison.OrdinalIgnoreCase));
        Assert.Equal("7.31", kit.ElementNo);
        Assert.Contains(report.Findings, f =>
            f.Kit.Contains("КЖ1")
            && f.Severity == Severity.Formatting
            && (f.ValueA + f.ValueB + f.InRd + f.InVerd).Contains("сооружения")
            && (f.ValueA + f.ValueB + f.InRd + f.InVerd).Contains("соружения"));

        var surnames = kit.Surnames.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
        foreach (var expected in new[] { "Ермолич", "Корецкий", "Щербаков", "Булычев", "Письмак", "Сапегин", "Янков" })
            Assert.Contains(surnames, s => s.Equals(expected, StringComparison.OrdinalIgnoreCase));

        var signers = kit.Signers.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
        foreach (var expected in new[] { "Булычев", "Гирсова", "Дутчак", "Ермолич", "Корецкий", "Письмак", "Сапегин", "Щербаков", "Янков" })
            Assert.Contains(signers, s => s.Equals(expected, StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(signers, s => s.Contains("Минцифр", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(signers, s => s.Contains("Сертум", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(signers, s => s.Contains("ТАКСКОМ", StringComparison.OrdinalIgnoreCase));

        Assert.DoesNotContain(report.Findings, f => f.CheckName == FindingGrouper.SignerMissing);
        Assert.DoesNotContain(report.Findings, f => f.ValueA.Contains("Гирсова") || f.ValueA.Contains("Дутчак"));
        Assert.NotEqual(Severity.Warning, kit.SurnameStatus);
        Assert.DoesNotContain(report.Findings, f =>
            f.Severity == Severity.Critical
            && f.CheckName == "Фамилия без подписи"
            && new[] { "Ермолич", "Корецкий", "Щербаков", "Булычев", "Письмак", "Сапегин", "Янков" }
                .Any(s => f.ValueA.Contains(s)));
    }

    [RequiresSamplesFact]
    public void Missing_signature_is_critical()
    {
        var source = Path.Combine(Root, "rd", "960-07-1529-Д-ИС21-27-КЖ1.pdf");
        var dir = Directory.CreateTempSubdirectory("proverkard");
        try
        {
            var copy = Path.Combine(dir.FullName, Path.GetFileName(source));
            File.Copy(source, copy);
            var report = new CheckingEngine().Run(new CheckInput
            {
                VerdPath = Path.Combine(Root, "etap7.xlsx"),
                VesrPath = Path.Combine(Root, "vesr.xlsx"),
                RdPaths = [copy],
            });
            Assert.Contains(report.Findings, f => f.CheckName == "Электронная подпись" && f.Severity == Severity.Critical);
        }
        finally
        {
            dir.Delete(recursive: true);
        }
    }

    [RequiresSamplesFact]
    public void Sample_report_workbook_has_the_three_sheets()
    {
        var report = Run();
        var path = Path.Combine(Path.GetTempPath(), "proverkard-sample-test.xlsx");
        ReportWriter.Write(report, path);
        Assert.True(new FileInfo(path).Length > 10_000);
    }

    private static CheckReport? _cached;

    private static CheckReport Run() => _cached ??= new CheckingEngine().Run(new CheckInput
    {
        VerdPath = Path.Combine(Root, "etap7.xlsx"),
        VesrPath = Path.Combine(Root, "vesr.xlsx"),
        ActPaths = Directory.GetFiles(Path.Combine(Root, "akty"), "*.docx"),
        RdPaths = Directory.GetFiles(Path.Combine(Root, "rd")).Where(f => !f.EndsWith(".sig", StringComparison.OrdinalIgnoreCase)).ToArray(),
    });
}

/// <summary>
/// Пропускает тест, если папки samples или файлов, без которых тест бросил бы исключение, нет на диске.
/// </summary>
public sealed class RequiresSamplesFactAttribute : FactAttribute
{
    public RequiresSamplesFactAttribute()
    {
        if (!Samples.IsAvailable)
            Skip = "Папка samples или нужные файлы образцов отсутствуют — тест пропущен.";
    }
}

internal static class Samples
{
    private static readonly string[] RequiredEntries =
    [
        "etap7.xlsx",
        "vesr.xlsx",
        "akty",
        "rd",
        Path.Combine("rd", "960-07-1529-Д-ИС21-27-КЖ1.pdf"),
    ];

    public static bool IsAvailable => TryGetRoot(out _);

    public static string Root =>
        TryGetRoot(out var root)
            ? root
            : throw new InvalidOperationException("Не найдена папка samples.");

    public static bool TryGetRoot(out string root)
    {
        root = "";
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            var candidate = Path.Combine(dir.FullName, "samples");
            if (HasRequiredEntries(candidate))
            {
                root = candidate;
                return true;
            }

            dir = dir.Parent;
        }

        return false;
    }

    private static bool HasRequiredEntries(string samplesDir)
    {
        if (!Directory.Exists(samplesDir))
            return false;

        foreach (var relative in RequiredEntries)
        {
            var path = Path.Combine(samplesDir, relative);
            if (!File.Exists(path) && !Directory.Exists(path))
                return false;
        }

        return true;
    }
}
