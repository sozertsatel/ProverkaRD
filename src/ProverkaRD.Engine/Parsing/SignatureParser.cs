using System.Security.Cryptography;
using System.Security.Cryptography.Pkcs;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Text.RegularExpressions;
using Org.BouncyCastle.Asn1.Cms;
using Org.BouncyCastle.Asn1.X509;
using Org.BouncyCastle.Cms;
using Org.BouncyCastle.Security;
using Org.BouncyCastle.X509.Store;
using ProverkaRD.Engine;
using BcCertificate = Org.BouncyCastle.X509.X509Certificate;

namespace ProverkaRD.Engine.Parsing;

public readonly record struct SignatureLocation(
    string Path,
    bool NameDiffers,
    bool ExactRejected = false,
    string? ExactName = null,
    string? CoveringName = null);

public static partial class SignatureParser
{
    private enum CryptoVerdict
    {
        Verified,
        Rejected,
        Inconclusive,
    }

    public static string? FindSignature(string documentPath) => Locate(documentPath)?.Path;

    /// <summary>
    /// Подпись сверяется с байтами файла всегда, в том числе когда имя .sig точное.
    /// Точное имя есть, но подпись чужая — ExactRejected. Если точного имени нет,
    /// берётся другая .sig в папке, которая покрывает файл (NameDiffers).
    /// </summary>
    public static SignatureLocation? Locate(string documentPath)
    {
        var named = FindNamedSignature(documentPath);
        byte[] document;
        try
        {
            document = SharedRead.Bytes(documentPath);
        }
        catch
        {
            return named is null ? null : new SignatureLocation(named, false);
        }

        if (named is not null && Signs(document, named))
            return new SignatureLocation(named, false);

        var covering = FindCoveringSignature(documentPath, document, named);
        if (named is not null)
        {
            return new SignatureLocation(
                covering ?? named,
                false,
                ExactRejected: true,
                ExactName: Path.GetFileName(named),
                CoveringName: covering is null ? null : Path.GetFileName(covering));
        }

        if (covering is not null)
            return new SignatureLocation(covering, true);

        return null;
    }

    /// <summary>Криптографически ли эта подпись покрывает байты документа.</summary>
    public static bool Covers(byte[] document, byte[] signature)
    {
        if (document is null || signature is null || signature.Length == 0)
            return false;
        if (SignsBytes(document, signature))
            return true;
        if (signature[0] == 0x30)
            return false;
        var decoded = TryDecodeTextSignature(signature);
        return decoded is not null && SignsBytes(document, decoded);
    }

    private static string? FindNamedSignature(string documentPath)
    {
        var exact = documentPath + ".sig";
        if (File.Exists(exact))
            return exact;

        var directory = Path.GetDirectoryName(documentPath);
        if (string.IsNullOrEmpty(directory) || !Directory.Exists(directory))
            return null;

        var expected = Path.GetFileName(documentPath) + ".sig";
        foreach (var file in Directory.EnumerateFiles(directory))
        {
            if (Path.GetFileName(file).Equals(expected, StringComparison.OrdinalIgnoreCase))
                return file;
        }

        return null;
    }

    private static string? FindCoveringSignature(string documentPath, byte[] document, string? except)
    {
        var directory = Path.GetDirectoryName(documentPath);
        if (string.IsNullOrEmpty(directory) || !Directory.Exists(directory))
            return null;

        foreach (var file in Directory.EnumerateFiles(directory))
        {
            if (!Path.GetExtension(file).Equals(".sig", StringComparison.OrdinalIgnoreCase))
                continue;
            if (except is not null && string.Equals(Path.GetFullPath(file), Path.GetFullPath(except), StringComparison.OrdinalIgnoreCase))
                continue;
            if (Signs(document, file))
                return file;
        }

        return null;
    }

    public static SignatureRead Read(string path)
    {
        try
        {
            var bytes = SharedRead.Bytes(path);
            try
            {
                return ReadSignedCms(bytes);
            }
            catch (Exception pkcsError)
            {
                try
                {
                    return ReadBouncyCastle(bytes);
                }
                catch (Exception bcError)
                {
                    return Empty("Не удалось разобрать подпись: " + pkcsError.Message + " / " + bcError.Message);
                }
            }
        }
        catch (Exception ex)
        {
            return Empty("Не удалось прочитать файл подписи: " + ex.Message);
        }
    }

    private static bool Signs(byte[] document, string signaturePath)
    {
        byte[] bytes;
        try
        {
            bytes = SharedRead.Bytes(signaturePath);
        }
        catch
        {
            return false;
        }

        return Covers(document, bytes);
    }

    private static bool SignsBytes(byte[] document, byte[] signature)
    {
        var verdict = VerifyCryptographically(document, signature);
        if (verdict == CryptoVerdict.Verified)
            return true;
        if (verdict == CryptoVerdict.Rejected)
            return false;
        return DigestMatches(document, signature);
    }

    /// <summary>
    /// Сначала проверка .NET (RSA и алгоритмы ОС), затем BouncyCastle по открытому ключу
    /// сертификата из CMS — так проверяется ГОСТ Р 34.10-2012. Срок сертификата здесь не смотрится:
    /// его сравнивают с signingTime отдельно. Явный провал подписи не заменяется сверкой хэша.
    /// </summary>
    private static CryptoVerdict VerifyCryptographically(byte[] document, byte[] signature)
    {
        if (DetachedCmsSigns(document, signature) || AttachedCmsSigns(document, signature))
            return CryptoVerdict.Verified;
        return VerifyBouncyCastle(document, signature);
    }

    private static CryptoVerdict VerifyBouncyCastle(byte[] document, byte[] signature)
    {
        try
        {
            var detached = new CmsSignedData(new CmsProcessableByteArray(document), signature);
            return VerifySigners(detached);
        }
        catch (CmsException)
        {
            return CryptoVerdict.Rejected;
        }
        catch
        {
            // Это не отсоединённая CMS. Ниже — присоединённая.
        }

        try
        {
            var attached = new CmsSignedData(signature);
            if (attached.SignedContent is null)
                return CryptoVerdict.Inconclusive;
            using var buffer = new MemoryStream();
            attached.SignedContent.Write(buffer);
            if (!buffer.ToArray().AsSpan().SequenceEqual(document))
                return CryptoVerdict.Rejected;
            return VerifySigners(attached);
        }
        catch (CmsException)
        {
            return CryptoVerdict.Rejected;
        }
        catch
        {
            return CryptoVerdict.Inconclusive;
        }
    }

    private static CryptoVerdict VerifySigners(CmsSignedData cms)
    {
        var any = false;
        var rejected = false;
        foreach (SignerInformation signer in cms.GetSignerInfos().GetSigners())
        {
            any = true;
            var certs = CertificatesOf(cms, signer);
            if (certs.Count == 0)
                continue;
            foreach (var cert in certs)
            {
                try
                {
                    if (signer.Verify(cert.GetPublicKey()))
                        return CryptoVerdict.Verified;
                    rejected = true;
                }
                catch (CmsException)
                {
                    rejected = true;
                }
                catch
                {
                    // Алгоритм не поддержан — остаётся сверка хэша.
                }
            }
        }

        if (!any)
            return CryptoVerdict.Inconclusive;
        return rejected ? CryptoVerdict.Rejected : CryptoVerdict.Inconclusive;
    }

    private static List<BcCertificate> CertificatesOf(CmsSignedData cms, SignerInformation signer)
    {
        var matched = new List<BcCertificate>();
        foreach (BcCertificate cert in cms.GetCertificates().EnumerateMatches(signer.SignerID))
            matched.Add(cert);
        if (matched.Count > 0)
            return matched;

        foreach (BcCertificate cert in cms.GetCertificates().EnumerateMatches(new X509CertStoreSelector()))
        {
            if (cert.GetBasicConstraints() >= 0)
                continue;
            matched.Add(cert);
        }

        return matched;
    }

    private static bool DetachedCmsSigns(byte[] document, byte[] signature)
    {
        try
        {
            var cms = new SignedCms(new System.Security.Cryptography.Pkcs.ContentInfo(document), detached: true);
            cms.Decode(signature);
            cms.CheckSignature(verifySignatureOnly: true);
            return cms.SignerInfos.Count > 0;
        }
        catch
        {
            return false;
        }
    }

    private static bool AttachedCmsSigns(byte[] document, byte[] signature)
    {
        try
        {
            var cms = new SignedCms();
            cms.Decode(signature);
            var content = cms.ContentInfo.Content;
            if (content is null || !content.AsSpan().SequenceEqual(document))
                return false;
            cms.CheckSignature(verifySignatureOnly: true);
            return cms.SignerInfos.Count > 0;
        }
        catch
        {
            return false;
        }
    }

    private static bool DigestMatches(byte[] document, byte[] signature)
    {
        try
        {
            var detached = new CmsSignedData(new CmsProcessableByteArray(document), signature);
            if (AnyDigestMatches(detached, document))
                return true;
        }
        catch
        {
            // Подпись может быть присоединена или не быть CMS.
        }

        try
        {
            var attached = new CmsSignedData(signature);
            if (attached.SignedContent is null)
                return false;
            using var buffer = new MemoryStream();
            attached.SignedContent.Write(buffer);
            if (!buffer.ToArray().AsSpan().SequenceEqual(document))
                return false;
            return AnyDigestMatches(attached, document);
        }
        catch
        {
            return false;
        }
    }

    private static bool AnyDigestMatches(CmsSignedData cms, byte[] document)
    {
        foreach (SignerInformation signer in cms.GetSignerInfos().GetSigners())
        {
            byte[]? expected;
            try
            {
                expected = signer.GetContentDigest();
            }
            catch
            {
                continue;
            }

            if (expected is null || expected.Length == 0)
                continue;
            try
            {
                var name = DigestUtilities.GetAlgorithmName(signer.DigestAlgorithmID.Algorithm);
                var actual = DigestUtilities.CalculateDigest(name, document);
                if (actual is not null && actual.AsSpan().SequenceEqual(expected))
                    return true;
            }
            catch
            {
                // Неизвестный алгоритм хэша — эта подпись документ не подтверждает.
            }
        }

        return false;
    }

    private static byte[]? TryDecodeTextSignature(byte[] bytes)
    {
        var text = Encoding.UTF8.GetString(bytes);
        if (text.Contains('\0'))
            return null;
        var body = PemArmor().Replace(text, "");
        body = new string(body.Where(ch => !char.IsWhiteSpace(ch)).ToArray());
        if (body.Length < 16 || body.Length % 4 != 0)
            return null;
        try
        {
            return Convert.FromBase64String(body);
        }
        catch (FormatException)
        {
            return null;
        }
    }

    private static SignatureRead ReadSignedCms(byte[] bytes)
    {
        var cms = new SignedCms();
        cms.Decode(bytes);
        var people = new List<CertificateSigner>();
        if (cms.SignerInfos.Count > 0)
        {
            foreach (System.Security.Cryptography.Pkcs.SignerInfo signer in cms.SignerInfos)
            {
                var signingTime = ReadSigningTime(signer);
                foreach (var cert in SignerCertificates(cms, signer))
                {
                    if (IsCertificationAuthority(cert))
                        continue;
                    var person = PersonFrom(cert.Subject, cert.NotBefore, cert.NotAfter, signingTime);
                    if (person is not null)
                        people.Add(person);
                }
            }
        }
        else
        {
            foreach (var cert in cms.Certificates.OfType<X509Certificate2>())
            {
                if (IsCertificationAuthority(cert))
                    continue;
                var person = PersonFrom(cert.Subject, cert.NotBefore, cert.NotAfter, null);
                if (person is not null)
                    people.Add(person);
            }
        }

        return FromPeople(people);
    }

    private static IEnumerable<X509Certificate2> SignerCertificates(SignedCms cms, System.Security.Cryptography.Pkcs.SignerInfo signer)
    {
        if (signer.Certificate is not null)
        {
            yield return signer.Certificate;
            yield break;
        }

        foreach (var cert in cms.Certificates.OfType<X509Certificate2>())
            yield return cert;
    }

    private static DateTimeOffset? ReadSigningTime(System.Security.Cryptography.Pkcs.SignerInfo signer)
    {
        if (signer.SignedAttributes is null)
            return null;
        foreach (CryptographicAttributeObject attribute in signer.SignedAttributes)
        {
            if (attribute.Oid?.Value != "1.2.840.113549.1.9.5")
                continue;
            foreach (AsnEncodedData value in attribute.Values)
            {
                try
                {
                    var time = new Pkcs9SigningTime(value.RawData);
                    return ToOffset(time.SigningTime);
                }
                catch
                {
                    // Атрибут есть, но время не разобрано: для подписанта это отсутствие времени.
                }
            }
        }

        return null;
    }

    private static SignatureRead ReadBouncyCastle(byte[] bytes)
    {
        var cms = new CmsSignedData(bytes);
        var people = new List<CertificateSigner>();
        var signers = cms.GetSignerInfos().GetSigners().Cast<SignerInformation>().ToList();
        if (signers.Count == 0)
        {
            foreach (BcCertificate cert in cms.GetCertificates().EnumerateMatches(new X509CertStoreSelector()))
            {
                var person = PersonFromBc(cert, null);
                if (person is not null)
                    people.Add(person);
            }
        }
        else
        {
            foreach (var signer in signers)
            {
                var signingTime = ReadBcSigningTime(signer);
                var certs = CertificatesOf(cms, signer);
                if (certs.Count == 0)
                {
                    foreach (BcCertificate cert in cms.GetCertificates().EnumerateMatches(new X509CertStoreSelector()))
                        certs.Add(cert);
                }

                foreach (var cert in certs)
                {
                    var person = PersonFromBc(cert, signingTime);
                    if (person is not null)
                        people.Add(person);
                }
            }
        }

        return FromPeople(people);
    }

    private static DateTimeOffset? ReadBcSigningTime(SignerInformation signer)
    {
        var attribute = signer.SignedAttributes?[CmsAttributes.SigningTime];
        if (attribute is null || attribute.AttrValues.Count == 0)
            return null;
        try
        {
            var time = Org.BouncyCastle.Asn1.Cms.Time.GetInstance(attribute.AttrValues[0]);
            return new DateTimeOffset(DateTime.SpecifyKind(time.ToDateTime(), DateTimeKind.Utc));
        }
        catch
        {
            return null;
        }
    }

    private static CertificateSigner? PersonFromBc(BcCertificate cert, DateTimeOffset? signingTime)
    {
        if (cert.GetBasicConstraints() >= 0)
            return null;
        var subject = cert.SubjectDN.ToString();
        var surname = Dn(cert, X509Name.Surname);
        var given = Dn(cert, X509Name.GivenName);
        var common = Dn(cert, X509Name.CN);
        return BuildPerson(surname, given, common, subject, cert.NotBefore, cert.NotAfter, signingTime);
    }

    private static string Dn(BcCertificate cert, Org.BouncyCastle.Asn1.DerObjectIdentifier oid)
    {
        var values = cert.SubjectDN.GetValueList(oid);
        if (values is null || values.Count == 0)
            return "";
        return values[0]?.ToString()?.Trim() ?? "";
    }

    private static CertificateSigner? PersonFrom(string subject, DateTime notBefore, DateTime notAfter, DateTimeOffset? signingTime)
    {
        var surname = Attribute(subject, "SN") ?? Attribute(subject, "SURNAME") ?? "";
        var given = Attribute(subject, "G") ?? Attribute(subject, "GIVENNAME") ?? "";
        var common = Attribute(subject, "CN") ?? "";
        return BuildPerson(surname, given, common, subject, notBefore, notAfter, signingTime);
    }

    private static CertificateSigner? BuildPerson(
        string surnameRaw,
        string givenRaw,
        string commonRaw,
        string subject,
        DateTime notBefore,
        DateTime notAfter,
        DateTimeOffset? signingTime)
    {
        if (IsOrganization(surnameRaw))
            return null;
        var surname = SurnameToken(surnameRaw);
        if (surname.Length == 0)
            surname = SurnameToken(commonRaw);
        if (surname.Length == 0 || IsOrganization(surname) || IsOrganization(subject))
            return null;

        var given = Clean(givenRaw);
        var common = Clean(commonRaw);
        var display = given.Length > 0 ? surname + " " + given : (common.Length > 0 ? common : surname);
        var initials = InitialsText(given);
        var matchKey = initials.Length > 0 ? surname + " " + initials : surname;
        return new CertificateSigner(
            surname,
            given,
            common,
            display,
            matchKey,
            new DateTimeOffset(AsUtc(notBefore)),
            new DateTimeOffset(AsUtc(notAfter)),
            signingTime,
            Classify(notBefore, notAfter, signingTime));
    }

    private static CertTimeStatus Classify(DateTime notBefore, DateTime notAfter, DateTimeOffset? signingTime)
    {
        if (signingTime is null)
            return CertTimeStatus.SigningTimeMissing;
        var at = signingTime.Value.UtcDateTime;
        var start = AsUtc(notBefore);
        var end = AsUtc(notAfter);
        if (at < start.AddMinutes(-1))
            return CertTimeStatus.NotYetValid;
        if (at > end.AddMinutes(1))
            return CertTimeStatus.Expired;
        return CertTimeStatus.Valid;
    }

    private static DateTime AsUtc(DateTime value) => value.Kind switch
    {
        DateTimeKind.Utc => value,
        DateTimeKind.Local => value.ToUniversalTime(),
        _ => DateTime.SpecifyKind(value, DateTimeKind.Local).ToUniversalTime(),
    };

    private static DateTimeOffset ToOffset(DateTime value)
    {
        var specified = value.Kind switch
        {
            DateTimeKind.Utc => value,
            DateTimeKind.Local => value,
            _ => DateTime.SpecifyKind(value, DateTimeKind.Local),
        };
        return new DateTimeOffset(specified);
    }

    private static string? Attribute(string subject, string key)
    {
        foreach (Match match in DnAttribute().Matches(subject))
        {
            if (!match.Groups[1].Value.Equals(key, StringComparison.OrdinalIgnoreCase))
                continue;
            var value = Clean(match.Groups[2].Value);
            if (value.Length > 0)
                return value;
        }

        return null;
    }

    private static string SurnameToken(string value)
    {
        foreach (var raw in value.Split([' ', '\t', '\u00A0'], StringSplitOptions.RemoveEmptyEntries))
        {
            var token = Clean(raw).Trim('.');
            if (token.Length < 3)
                continue;
            var plain = token.Replace(".", "");
            if (plain.Length <= 2 || !plain.Any(char.IsLetter))
                continue;
            return token;
        }

        return "";
    }

    private static string InitialsText(string given)
    {
        if (given.Length == 0)
            return "";
        var letters = new List<char>();
        foreach (var raw in given.Split([' ', '\t', '\u00A0'], StringSplitOptions.RemoveEmptyEntries))
        {
            var plain = raw.Trim('.', ' ');
            if (plain.Length == 0)
                continue;
            if (plain.Length <= 2 && plain.All(char.IsLetter))
            {
                letters.AddRange(plain.Where(char.IsLetter));
                continue;
            }

            letters.Add(plain[0]);
        }

        if (letters.Count == 0)
            return "";
        return string.Join(".", letters.Select(ch => ch.ToString())) + ".";
    }

    private static string Clean(string value) => value.Trim().Trim('"');

    private static SignatureRead FromPeople(IReadOnlyList<CertificateSigner> people)
    {
        var surnames = people.Select(person => person.Surname).Where(surname => surname.Length > 0).ToList();
        var status = Severity.Ok;
        var parts = new List<string>();
        foreach (var person in people)
        {
            var note = person.TimeStatus switch
            {
                CertTimeStatus.Expired => "истёк на момент подписания",
                CertTimeStatus.NotYetValid => "ещё не действовал на момент подписания",
                CertTimeStatus.SigningTimeMissing => "нет времени подписания",
                _ => "действовал",
            };
            if (person.TimeStatus is CertTimeStatus.Expired or CertTimeStatus.NotYetValid)
                status = Severity.Critical;
            else if (person.TimeStatus == CertTimeStatus.SigningTimeMissing && status != Severity.Critical)
                status = Severity.Warning;
            parts.Add(person.DisplayName + ": " + note);
        }

        return new SignatureRead(surnames, null, people, status, string.Join("; ", parts));
    }

    private static SignatureRead Empty(string? error) => new([], error, [], Severity.Ok, "");

    private static bool IsCertificationAuthority(X509Certificate2 cert)
    {
        foreach (var extension in cert.Extensions)
        {
            if (extension is X509BasicConstraintsExtension basic && basic.CertificateAuthority)
                return true;
        }
        return IsOrganization(cert.Subject);
    }

    private static bool IsOrganization(string value)
    {
        var folded = value.ToLowerInvariant();
        return folded.Contains("минцифр")
               || folded.Contains("сертум")
               || folded.Contains("такском")
               || folded.Contains("taxcom")
               || folded.Contains("удостоверяющ")
               || folded.Contains("криптопро")
               || folded.Contains("crypto-pro");
    }

    [GeneratedRegex(@"(?:^|,)\s*([A-Za-z]+)\s*=\s*([^,]*)")]
    private static partial Regex DnAttribute();

    [GeneratedRegex(@"-----BEGIN [^-]+-----|-----END [^-]+-----")]
    private static partial Regex PemArmor();
}

public sealed record SignatureRead(
    IReadOnlyList<string> Surnames,
    string? Error,
    IReadOnlyList<CertificateSigner> People,
    Severity CertStatus,
    string CertValidityText);
