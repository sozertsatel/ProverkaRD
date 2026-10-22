namespace ProverkaRD.Engine;

public sealed class CheckInput
{
    public string? VerdPath { get; init; }
    public string? VesrPath { get; init; }
    public IReadOnlyList<string> ActPaths { get; init; } = [];
    public IReadOnlyList<string> RdPaths { get; init; } = [];
}

public sealed record Finding(
    string Act,
    string Kit,
    string CheckName,
    string ValueA,
    string ValueB,
    Severity Severity,
    string Explanation);

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
    public IReadOnlyList<string> Errors { get; init; } = [];
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
}

public sealed class VesrRow
{
    public string Code { get; init; } = "";
    public string Name { get; init; } = "";
    public string Unit { get; init; } = "";
    public decimal? Quantity { get; init; }
    public decimal? UnitPrice { get; init; }
    public decimal? TotalNet { get; init; }
}

public sealed class VesrBook
{
    public Dictionary<string, VesrRow> ByCode { get; } = new(StringComparer.Ordinal);
    public List<string> Errors { get; } = [];
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
    public IReadOnlyList<string> Signers { get; init; } = [];
    public bool SignatureParsed { get; init; }
    public string? SignatureError { get; init; }
    public IReadOnlyList<string> Errors { get; init; } = [];
}

public sealed class ActResult
{
    public string FileName { get; init; } = "";
    public string ElementNo { get; init; } = "";
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
    public decimal? ActGross { get; init; }
    public decimal? GrossRecalculated { get; init; }
    public Severity GrossStatus { get; init; }
    public int ActKitCount { get; init; }
    public int VerdKitCount { get; init; }
    public string CompletenessText { get; init; } = "";
    public Severity CompletenessStatus { get; init; }
    public Severity Overall { get; init; }
}

public sealed class KitResult
{
    public string ElementNo { get; init; } = "";
    public string ActName { get; init; } = "";
    public string Cipher { get; init; } = "";
    public string DgpCode { get; init; } = "";
    public string ActKitName { get; init; } = "";
    public string VerdKitName { get; init; } = "";
    public Severity NameStatus { get; init; }
    public string PaperCopies { get; init; } = "";
    public string ElectronicCopies { get; init; } = "";
    public string RdFileName { get; init; } = "";
    public string RdCipher { get; init; } = "";
    public string RdKitName { get; init; } = "";
    public Severity RdNameStatus { get; init; }
    public string RdElementNo { get; init; } = "";
    public string RdElementName { get; init; } = "";
    public Severity RdElementStatus { get; init; }
    public string Surnames { get; init; } = "";
    public string Signers { get; init; } = "";
    public Severity SurnameStatus { get; init; }
    public string Presence { get; init; } = "";
    public Severity Overall { get; init; }
}

public sealed class CheckReport
{
    public string? VerdFileName { get; init; }
    public string? VesrFileName { get; init; }
    public List<ActResult> Acts { get; } = [];
    public List<KitResult> Kits { get; } = [];
    public List<Finding> Findings { get; } = [];
    public List<string> Problems { get; } = [];
}
