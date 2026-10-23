using System.Globalization;

namespace ProverkaRD.Engine;

public sealed class CheckOptions
{
    public IReadOnlyList<string> IgnoredSigners { get; init; } = [];
    public bool GroupSignerWarnings { get; init; } = true;
    public bool GroupDuplicateFindings { get; init; } = true;
    /// <summary>Ставка НДС в процентах. 22 — ставка РФ с 2026 года.</summary>
    public decimal VatPercent { get; init; } = 22m;

    /// <summary>Читать PDF и DOCX внутри .zip. По умолчанию архив пропускается.</summary>
    public bool ReadZipArchives { get; init; }
}

public sealed class CheckInput
{
    public string? VerdPath { get; init; }
    public string? VesrPath { get; init; }
    public IReadOnlyList<string> ActPaths { get; init; } = [];
    public IReadOnlyList<string> RdPaths { get; init; } = [];
    /// <summary>Папки, из которых взяты комплекты. Нужны для подсчёта .sig и пропущенных .СВОР/_ИЗМ.</summary>
    public IReadOnlyList<string> RdFolders { get; init; } = [];
    public CheckOptions Options { get; init; } = new();
}

public readonly record struct FindingPlace(string Act, string ElementNo, string Kit, string ActPath = "")
{
    public string Display
    {
        get
        {
            if (!string.IsNullOrWhiteSpace(Kit) && Kit != "—")
                return Kit;
            return "";
        }
    }
}

public sealed record Finding(
    string Act,
    string Kit,
    string CheckName,
    string ValueA,
    string ValueB,
    Severity Severity,
    string Explanation,
    string ElementNo = "",
    string InAct = "",
    string InRd = "",
    string InVerd = "",
    string InVesr = "",
    string ActPath = "",
    IReadOnlyList<FindingPlace>? Locations = null,
    string ErrorWhere = "")
{
    public IReadOnlyList<FindingPlace> Locations { get; init; } = Locations ?? [];

    public string Places => Locations.Count == 0
        ? (string.IsNullOrWhiteSpace(Kit) || Kit == "—" ? "" : Kit)
        : string.Join(", ", Locations
            .Select(p => p.Kit)
            .Where(s => !string.IsNullOrWhiteSpace(s) && s != "—")
            .Distinct(StringComparer.OrdinalIgnoreCase));
}

public sealed class ActKit
{
    public string VolumeRaw { get; init; } = "";
    public string DgpCode { get; init; } = "";
    public string ElementNo { get; init; } = "";
    public string Cipher { get; init; } = "";
    public string Name { get; init; } = "";
    public string PaperCopies { get; init; } = "";
    public string ElectronicCopies { get; init; } = "";
}

public sealed class ActCostLine
{
    public string RawName { get; init; } = "";
    public string DgpCode { get; init; } = "";
    public string Name { get; init; } = "";
    public string Unit { get; init; } = "";
    public decimal? Quantity { get; init; }
    public decimal? Net { get; init; }
    public decimal? Vat { get; init; }
    public decimal? Gross { get; init; }
    public bool IsTotal { get; init; }
}

public sealed class ActDocument
{
    public required string FilePath { get; init; }
    public string FileName => Path.GetFileName(FilePath);
    public string DisplayName => Path.GetFileNameWithoutExtension(FilePath);
    public string? ElementNoFromFileName { get; init; }
    public string? ElementNoFromHeading { get; init; }
    public IReadOnlyList<ActKit> Kits { get; init; } = [];
    public IReadOnlyList<ActCostLine> CostLines { get; init; } = [];
    public decimal? AdvanceNet { get; init; }
    public IReadOnlyList<string> Errors { get; init; } = [];
    public IReadOnlyList<Notice> Notices { get; init; } = [];
    /// <summary>Номер этапа из текста акта, если он есть. «Этап 7.29» сюда не попадает.</summary>
    public string Stage { get; init; } = "";
}

public sealed class VerdKit
{
    public string Cipher { get; init; } = "";
    public string Name { get; init; } = "";
    public string PdCipher { get; init; } = "";
}

public sealed class VerdElement
{
    public string Number { get; init; } = "";
    public string Name { get; init; } = "";
    public string DgpCode { get; set; } = "";
    public List<VerdKit> Kits { get; } = [];
}

public sealed class VerdBook
{
    public List<VerdElement> Elements { get; } = [];
    public List<string> Errors { get; } = [];
    public List<Notice> Notices { get; } = [];
    public List<LedgerRowIssue> RowIssues { get; } = [];
    /// <summary>Текст шапки листа до строки столбцов и сама строка столбцов, чтобы найти «Этап N».</summary>
    public string HeaderText { get; set; } = "";
    /// <summary>Имя листа, с которого прочитана ведомость.</summary>
    public string SheetName { get; set; } = "";
}

public sealed class VesrRow
{
    public int ExcelRow { get; init; }
    public string Code { get; init; } = "";
    public string Name { get; init; } = "";
    public string Unit { get; init; } = "";
    public decimal? Quantity { get; init; }
    public decimal? UnitPrice { get; init; }
    public decimal? TotalNet { get; init; }
    /// <summary>Пустая или нечисловая сумма «Всего». Для строки акта это замечание, не пропуск.</summary>
    public string SumProblem { get; init; } = "";
}

public sealed class VesrBook
{
    public Dictionary<string, VesrRow> ByCode { get; } = new(StringComparer.Ordinal);
    public List<VesrRow> Rows { get; } = [];
    public List<string> Errors { get; } = [];
    public List<Notice> Notices { get; } = [];
    public List<LedgerRowIssue> RowIssues { get; } = [];
}

public sealed class RdDocument
{
    public required string FilePath { get; init; }
    public string FileName => Path.GetFileName(FilePath);
    public string? CipherFromFileName { get; init; }
    public string? CipherFromText { get; init; }
    public string Cipher { get; init; } = "";
    public string KitName { get; init; } = "";
    public string ElementNo { get; init; } = "";
    public string ElementName { get; init; } = "";
    public IReadOnlyList<string> Surnames { get; init; } = [];
    public string? SignaturePath { get; init; }
    public bool SignatureNameDiffers { get; init; }
    /// <summary>Рядом лежит .sig с ожидаемым именем, но она не покрывает этот файл.</summary>
    public bool SignatureRejected { get; init; }
    public string? RejectedSignatureName { get; init; }
    public string? CoveringSignatureName { get; init; }
    public IReadOnlyList<string> Signers { get; init; } = [];
    public IReadOnlyList<CertificateSigner> CertificateSigners { get; init; } = [];
    public Severity CertStatus { get; init; }
    public string CertValidityText { get; init; } = "";
    public bool SignatureParsed { get; init; }
    public string? SignatureError { get; init; }
    public IReadOnlyList<string> Errors { get; init; } = [];
    public IReadOnlyList<Notice> Notices { get; init; } = [];
}

public sealed class ActResult
{
    public string FileName { get; init; } = "";
    public string SourcePath { get; init; } = "";
    public string ElementNo { get; init; } = "";
    public decimal? AdvanceNet { get; init; }
    public string ActElementName { get; init; } = "";
    public string VerdElementName { get; init; } = "";
    public Severity NameStatus { get; init; }
    public string ActCostName { get; init; } = "";
    public string VesrCostName { get; init; } = "";
    public Severity CostNameStatus { get; init; }
    public string ActCode { get; init; } = "";
    public string VerdCode { get; init; } = "";
    public bool CodeFoundInVesr { get; init; }
    public Severity CodeStatus { get; init; }
    public decimal? ActNet { get; init; }
    public decimal? VesrNet { get; init; }
    public Severity NetStatus { get; init; }
    public decimal? ActVat { get; init; }
    public decimal? VatRecalculated { get; init; }
    public Severity VatStatus { get; init; }
    public decimal? ActGross { get; init; }
    public decimal? GrossRecalculated { get; init; }
    public Severity GrossStatus { get; init; }
    public decimal? VesrUnitPrice { get; init; }
    public Severity UnitPriceStatus { get; init; }
    public int ActKitCount { get; init; }
    public int VerdKitCount { get; init; }
    public string CompletenessText { get; init; } = "";
    public Severity CompletenessStatus { get; init; }
    public Severity Overall { get; set; }
    public int CriticalCount { get; set; }
    public int FormattingCount { get; set; }
    public int WarningCount { get; set; }

    public string NameTip => Pair("В акте", ActElementName, "В ВЭРД", VerdElementName);
    public string CostNameTip => Pair("В акте", ActCostName, "В ВЭСР", VesrCostName);
    public string CodeTip => Pair("В акте", ActCode, "В ВЭРД", VerdCode);
    public string NetTip => Pair("В акте", Money(ActNet), "В ВЭСР", Money(VesrNet));
    public string VatTip => Pair("В акте", Money(ActVat), "Расчёт", Money(VatRecalculated));
    public string GrossTip => Pair("В акте", Money(ActGross), "Расчёт", Money(GrossRecalculated));
    public string UnitTip => Pair("В акте, без НДС", Money(ActNet), "Цена ВЭСР", Money(VesrUnitPrice));
    public string CompletenessTip => string.IsNullOrWhiteSpace(CompletenessText) ? "—" : CompletenessText;

    private static string Pair(string leftLabel, string? left, string rightLabel, string? right) =>
        leftLabel + ": " + Blank(left) + "\n" + rightLabel + ": " + Blank(right);

    private static string Blank(string? value) => string.IsNullOrWhiteSpace(value) ? "—" : value.Trim();

    private static string Money(decimal? value) =>
        value is null ? "—" : value.Value.ToString("N2", CultureInfo.GetCultureInfo("ru-RU"));
}

public sealed class KitResult
{
    public string ElementNo { get; init; } = "";
    public string ActName { get; init; } = "";
    public string ActNo { get; init; } = "";
    public string SourcePath { get; init; } = "";
    public decimal? AdvanceNet { get; init; }
    public bool AdvanceKnown { get; init; }
    public string Cipher { get; init; } = "";
    public string ActCipher { get; init; } = "";
    public string VerdCipher { get; init; } = "";
    public string TitleCipher { get; init; } = "";
    public Severity CipherStatus { get; init; }
    public string CipherBlame { get; init; } = "";
    public string DgpCode { get; init; } = "";
    public string ActKitName { get; init; } = "";
    public string VerdKitName { get; init; } = "";
    public Severity NameStatus { get; init; }
    public string NameBlame { get; init; } = "";
    public string PaperCopies { get; init; } = "";
    public string ElectronicCopies { get; init; } = "";
    public string RdFileName { get; init; } = "";
    public string RdCipher { get; init; } = "";
    public string RdKitName { get; init; } = "";
    public Severity RdNameStatus { get; init; }
    public string ActElementName { get; init; } = "";
    public string VerdElementName { get; init; } = "";
    public string RdElementNo { get; init; } = "";
    public string RdElementName { get; init; } = "";
    public Severity RdElementStatus { get; init; }
    public Severity ElementNameStatus { get; init; }
    public string ElementNameBlame { get; init; } = "";
    public string PdfState { get; init; } = "";
    public string SigState { get; init; } = "";
    public string Surnames { get; init; } = "";
    public string MissingSigners { get; init; } = "";
    public string Signers { get; init; } = "";
    public Severity SurnameStatus { get; init; }
    public string Presence { get; init; } = "";
    public Severity Overall { get; init; }

    public string NameTip => Pair("В акте", ActKitName, "В ВЭРД", VerdKitName) + "\nВ РД: " + Blank(RdKitName);
    public string ElementTip => Pair("В акте", ActElementName, "В ВЭРД", VerdElementName) + "\nВ РД: " + Blank(RdElementName);
    public string SurnameTip => "В документе: " + Blank(Surnames) + "\nВ сертификате: " + Blank(Signers);

    private static string Pair(string leftLabel, string? left, string rightLabel, string? right) =>
        leftLabel + ": " + Blank(left) + "\n" + rightLabel + ": " + Blank(right);

    private static string Blank(string? value) => string.IsNullOrWhiteSpace(value) ? "—" : value.Trim();
}

public sealed record Notice(string Source, string Place, string Message);

/// <summary>Строка ведомости, которую не удалось разобрать. В отчёт попадают только строки элементов акта.</summary>
public sealed class LedgerRowIssue
{
    public string Source { get; init; } = "";
    public int Row { get; init; }
    public string Owner { get; init; } = "";
    public string Reason { get; init; } = "";
}

/// <summary>Одна строка листа «Опись комплекта»: комплект PDF или посторонний файл.</summary>
public sealed class RegistryRow
{
    public string FileName { get; init; } = "";
    public string Cipher { get; init; } = "";
    public string Element { get; init; } = "";
    public string Surnames { get; init; } = "";
    public string Signers { get; init; } = "";
    public string CertValidity { get; init; } = "";
    public Severity CertStatus { get; init; }
    public Severity SignatureStatus { get; init; }
    public Severity Overall { get; init; }
    public string Name { get; init; } = "";
    public string SignatureName { get; init; } = "нет";
    public bool HasSignature { get; init; }
    /// <summary>Письмо или PDF без шифра. В описи ниже итога, без номера.</summary>
    public bool Foreign { get; init; }

    public Severity SignatureColumn => SeverityText.Worst(SignatureStatus, CertStatus);
}

public static class CheckStages
{
    public const string LoadKits = "Загрузка комплектов";
    public const string Signatures = "Проверка подписей";
    public const string Ledgers = "Сверка с ВЭРД/ВЭСР";
    public const string Report = "Формирование отчёта";
}

public static class CheckProgressText
{
    public static string Format(string stage, int done, int total, string? fileName)
    {
        if (stage == CheckStages.LoadKits)
        {
            var text = stage + ": " + done + " из " + total;
            if (!string.IsNullOrWhiteSpace(fileName))
                text += " — " + fileName.Trim();
            return text;
        }

        if (stage == CheckStages.Signatures)
            return stage + ": " + done + " из " + total;
        return stage;
    }

    /// <summary>Верхняя полоса проверки: номер этапа среди четырёх, без имени файла.</summary>
    public static string StageHeading(string stage)
    {
        var index = stage switch
        {
            CheckStages.LoadKits => 1,
            CheckStages.Signatures => 2,
            CheckStages.Ledgers => 3,
            CheckStages.Report => 4,
            _ => 0,
        };
        return index == 0 ? stage : "Этап " + index + " из 4: " + stage;
    }

    public static string Elapsed(TimeSpan elapsed)
    {
        if (elapsed < TimeSpan.Zero)
            elapsed = TimeSpan.Zero;
        var hours = (int)elapsed.TotalHours;
        if (hours > 0)
            return "прошло " + hours + ":" + elapsed.Minutes.ToString("00") + ":" + elapsed.Seconds.ToString("00");
        return "прошло " + elapsed.Minutes + ":" + elapsed.Seconds.ToString("00");
    }
}

/// <summary>Шаг длинной операции. <see cref="Overall"/> — доля 0..1 на всю операцию, без сброса между этапами.</summary>
public readonly record struct CheckProgress(string Stage, int Done, int Total, string FileName, double Overall)
{
    public string Text => CheckProgressText.Format(Stage, Done, Total, FileName);
}

public sealed class CheckReport
{
    public string? VerdFileName { get; init; }
    public string? VesrFileName { get; init; }
    public List<ActResult> Acts { get; } = [];
    public List<KitResult> Kits { get; } = [];
    public List<Finding> Findings { get; } = [];
    public List<string> Problems { get; } = [];
    public List<Notice> Notices { get; } = [];
    public List<RegistryRow> Registry { get; } = [];
    public LoadStats Load { get; set; } = new();
    public bool VerdMissing { get; set; }
    public bool VesrMissing { get; set; }
    public bool VerdUnreadable { get; set; }
    public bool VesrUnreadable { get; set; }
    public int LockedFiles { get; set; }
    /// <summary>Номер этапа. Пусто — в имени файла и в шапке «Сводки» этапа нет.</summary>
    public string Stage { get; set; } = "";
    /// <summary>ВЭРД прочитана не с листа «База». Пусто, если лист «База» или ведомости нет.</summary>
    public string VerdSheetNote { get; set; } = "";
}

/// <summary>Сколько файлов увидено при загрузке и как они разошлись с актами.</summary>
public sealed class LoadStats
{
    public int Acts { get; init; }
    public int ActSets { get; init; }
    public int Pdfs { get; init; }
    public int MatchedPdfs { get; init; }
    public int LoosePdfs { get; init; }
    public int StrayPdfs { get; init; }
    public int Docx { get; init; }
    public int MatchedDocx { get; init; }
    public int LooseDocx { get; init; }
    public int StrayDocx { get; init; }
    public int Auxiliary { get; init; }
    public int Signatures { get; init; }
    public string Text { get; init; } = "";
}
