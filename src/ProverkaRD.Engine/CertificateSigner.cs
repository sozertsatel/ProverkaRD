namespace ProverkaRD.Engine;

public enum CertTimeStatus
{
    Valid,
    Expired,
    NotYetValid,
    SigningTimeMissing,
}

/// <summary>
/// Подписант из сертификата .sig: фамилия (SN), имя (G), общее имя (CN)
/// и действовал ли сертификат в момент signingTime.
/// </summary>
public sealed record CertificateSigner(
    string Surname,
    string GivenName,
    string CommonName,
    string DisplayName,
    string MatchKey,
    DateTimeOffset? NotBefore,
    DateTimeOffset? NotAfter,
    DateTimeOffset? SigningTime,
    CertTimeStatus TimeStatus);
