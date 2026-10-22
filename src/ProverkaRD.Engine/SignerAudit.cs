namespace ProverkaRD.Engine;

public readonly record struct SignerAuditResult(
    Severity Status,
    IReadOnlyList<string> Missing,
    bool SignatureMissing,
    bool NoSurnames);

/// <summary>
/// Фамилия из тома обязана быть среди подписантов .sig.
/// Лишние подписанты в подписи не замечание.
/// </summary>
public static class SignerAudit
{
    public static SignerAuditResult Check(
        IReadOnlyList<string>? documentSurnames,
        IReadOnlyList<string>? signers,
        bool hasSignature,
        string? signatureError,
        IEnumerable<string>? ignored)
    {
        documentSurnames ??= [];
        signers ??= [];
        if (!hasSignature || !string.IsNullOrWhiteSpace(signatureError))
            return new SignerAuditResult(Severity.Critical, [], true, documentSurnames.Count == 0);

        var considered = documentSurnames.Where(s => !SurnameStem.IsIgnored(s, ignored)).ToList();
        if (considered.Count == 0 && documentSurnames.Count == 0)
            return new SignerAuditResult(Severity.Critical, [], false, true);

        var usable = signers.Where(s => !SurnameStem.IsIgnored(s, ignored)).ToList();
        var missing = new List<string>();
        foreach (var surname in considered)
        {
            if (usable.Any(signer => SurnameStem.Same(signer, surname)))
                continue;
            missing.Add(surname);
        }

        return new SignerAuditResult(
            missing.Count == 0 ? Severity.Ok : Severity.Critical,
            missing,
            false,
            false);
    }
}
