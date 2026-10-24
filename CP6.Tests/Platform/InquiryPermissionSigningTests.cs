using CP6.WebApi.Services;
using NSec.Cryptography;

namespace CP6.Tests.Platform;

public sealed class InquiryPermissionSigningTests
{
    internal const string Issuer = "https://iam.cp6.test";
    internal static InquiryPermissionSigningOptionsV1 Options()
    {
        using var key = Key.Create(SignatureAlgorithm.Ed25519, new KeyCreationParameters { ExportPolicy = KeyExportPolicies.AllowPlaintextExport });
        return new() { Enabled = true, Issuer = Issuer, KeyId = "main-inquiry-" + Guid.NewGuid().ToString("N"),
            PrivateKeyBase64 = Convert.ToBase64String(key.Export(KeyBlobFormat.RawPrivateKey)),
            PublicKeyBase64 = Convert.ToBase64String(key.PublicKey.Export(KeyBlobFormat.RawPublicKey)),
            ActiveFromUtc = DateTimeOffset.UtcNow.AddMinutes(-1) };
    }
    internal static InquiryPermissionSigningKeyV1 Signer(InquiryPermissionSigningOptionsV1 options)
        => new(options, new() { Enabled = true, Issuer = Issuer });

    [Theory]
    [InlineData("issuer")][InlineData("null-issuer")][InlineData("null-key")]
    [InlineData("wrong-public")][InlineData("private-length")][InlineData("window")]
    public void Invalid_trusted_configuration_fails_without_disclosing_keys(string kind)
    {
        var o = Options();
        switch (kind)
        {
            case "issuer": o.Issuer = "https://iam.other.test"; break;
            case "null-issuer": o.Issuer = null!; break;
            case "null-key": o.KeyId = null!; break;
            case "wrong-public": o.PublicKeyBase64 = Options().PublicKeyBase64; break;
            case "private-length": o.PrivateKeyBase64 = Convert.ToBase64String(new byte[31]); break;
            case "window": o.ActiveToUtc = o.ActiveFromUtc; break;
        }
        var error = Assert.Throws<InvalidOperationException>(() => Signer(o));
        Assert.Equal("INQUIRY_SIGNING_CONFIGURATION_INVALID", error.Message);
    }
    [Fact]
    public void Unconfigured_host_remains_disabled()
    { using var key = new InquiryPermissionSigningKeyV1(new(), new()); Assert.False(key.Enabled); }
}
