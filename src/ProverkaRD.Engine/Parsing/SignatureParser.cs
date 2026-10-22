using System.Security.Cryptography.Pkcs;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Text.RegularExpressions;
using Org.BouncyCastle.Cms;
using Org.BouncyCastle.Security;
using Org.BouncyCastle.X509.Store;
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
                return new SignatureRead(ReadSignedCms(bytes), null);
            }
            catch (Exception pkcsError)
            {
                try
                {
                    return new SignatureRead(ReadBouncyCastle(bytes), null);
                }
                catch (Exception bcError)
                {
                    return new SignatureRead([],
                        "Не удалось разобрать подпись: " + pkcsError.Message + " / " + bcError.Message);
                }
            }
        }
        catch (Exception ex)
        {
            return new SignatureRead([], "Не удалось прочитать файл подписи: " + ex.Message);
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

        if (SignsBytes(document, bytes))
            return true;
        if (bytes.Length == 0 || bytes[0] == 0x30)
            return false;
        var decoded = TryDecodeTextSignature(bytes);
        return decoded is not null && SignsBytes(document, decoded);
    }

    private static bool SignsBytes(byte[] document, byte[] signature) =>
        DetachedCmsSigns(document, signature)
        || AttachedCmsSigns(document, signature)
        || DigestMatches(document, signature);

    private static bool DetachedCmsSigns(byte[] document, byte[] signature)
    {
        try
        {
            var cms = new SignedCms(new ContentInfo(document), detached: true);
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

    private static List<string> ReadSignedCms(byte[] bytes)
    {
        var cms = new SignedCms();
        cms.Decode(bytes);
        var surnames = new List<string>();
        foreach (var cert in cms.Certificates.OfType<X509Certificate2>())
        {
            if (IsCertificationAuthority(cert))
                continue;
            var match = SurnameAttribute().Match(cert.Subject);
            if (!match.Success)
                continue;
            var surname = match.Groups[1].Value.Trim().Trim('"');
            if (surname.Length == 0 || IsOrganization(surname) || IsOrganization(cert.Subject))
                continue;
            surnames.Add(surname);
        }
        return surnames;
    }

    private static List<string> ReadBouncyCastle(byte[] bytes)
    {
        var cms = new CmsSignedData(bytes);
        var surnames = new List<string>();
        foreach (BcCertificate cert in cms.GetCertificates().EnumerateMatches(new X509CertStoreSelector()))
        {
            if (cert.GetBasicConstraints() >= 0)
                continue;
            var values = cert.SubjectDN.GetValueList(Org.BouncyCastle.Asn1.X509.X509Name.Surname);
            foreach (var value in values)
            {
                var surname = value?.ToString()?.Trim() ?? "";
                if (surname.Length == 0 || IsOrganization(surname))
                    continue;
                surnames.Add(surname);
            }
        }
        return surnames;
    }

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

    [GeneratedRegex(@"(?:^|,\s*)SN=([^,]+)")]
    private static partial Regex SurnameAttribute();

    [GeneratedRegex(@"-----BEGIN [^-]+-----|-----END [^-]+-----")]
    private static partial Regex PemArmor();
}

public sealed record SignatureRead(IReadOnlyList<string> Surnames, string? Error);
