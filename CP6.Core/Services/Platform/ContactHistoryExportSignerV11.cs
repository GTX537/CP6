using System.Security.Cryptography;
using System.Text;
using static CP6.Core.Services.Platform.ContactHistoryExportWireV11;

namespace CP6.Core.Services.Platform;

public sealed class ContactHistoryExportSignerV11
{
    private readonly byte[] key;

    public ContactHistoryExportSignerV11(string epoch, string keyId, string privateKeyPem, int downloadWindowSeconds)
    {
        Epoch = RegisteredKey(epoch);
        KeyId = RegisteredKey(keyId);
        DownloadWindow = TimeSpan.FromSeconds(downloadWindowSeconds is >= 1 and <= 60 ? downloadWindowSeconds :
            throw new InvalidOperationException("CONTACT_EXPORT_WINDOW_INVALID"));
        using var signer = ECDsa.Create();
        signer.ImportFromPem(privateKeyPem);
        if (signer.ExportParameters(false).Curve.Oid.Value != ECCurve.NamedCurves.nistP256.Oid.Value)
            throw new InvalidOperationException("CONTACT_EXPORT_SIGNING_KEY_INVALID");
        key = signer.ExportPkcs8PrivateKey();
        PublicKey = Convert.ToBase64String(signer.ExportSubjectPublicKeyInfo());
    }

    public string Epoch { get; }
    public string KeyId { get; }
    public string PublicKey { get; }
    internal TimeSpan DownloadWindow { get; }

    private static string RegisteredKey(string value) => value is { Length: > 0 and <= 80 } &&
        value.All(c => char.IsAsciiLetterOrDigit(c) || c is '-' or '_' or '.') ? value :
        throw new InvalidOperationException("CONTACT_EXPORT_AUTHORITY_INVALID");

    private static byte[] Statement<T>(string domain, T value) => [.. Encoding.UTF8.GetBytes(domain + "\n"), .. Canonical(value)];

    internal string Sign<T>(string domain, T value)
    {
        using var signer = ECDsa.Create();
        signer.ImportPkcs8PrivateKey(key, out _);
        return Convert.ToBase64String(signer.SignData(Statement(domain, value), HashAlgorithmName.SHA256,
            DSASignatureFormat.IeeeP1363FixedFieldConcatenation));
    }

    internal bool Verify<T>(string domain, T value, string signature)
    {
        Span<byte> decoded = stackalloc byte[64];
        if (!Convert.TryFromBase64String(signature, decoded, out var bytes) || bytes != decoded.Length ||
            Convert.ToBase64String(decoded) != signature) return false;
        using var signer = ECDsa.Create();
        signer.ImportSubjectPublicKeyInfo(Convert.FromBase64String(PublicKey), out _);
        return signer.VerifyData(Statement(domain, value), decoded, HashAlgorithmName.SHA256,
            DSASignatureFormat.IeeeP1363FixedFieldConcatenation);
    }
}
