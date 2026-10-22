using System.Security.Cryptography.Pkcs;
using System.Security.Cryptography.X509Certificates;
using System.Text.RegularExpressions;
using Org.BouncyCastle.Cms;
using Org.BouncyCastle.X509.Store;
using BcCertificate = Org.BouncyCastle.X509.X509Certificate;

namespace ProverkaRD.Engine.Parsing;

public static partial class SignatureParser
{
    public static string? FindSignature(string documentPath)
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
}

public sealed record SignatureRead(IReadOnlyList<string> Surnames, string? Error);
