using CP6.Core.Services.Sys;
using Microsoft.Extensions.Options;
using Xunit;
namespace CP6.Tests.Sys;
public class TotpServiceTests
{
    private static TotpService Make() => new(Options.Create(new SecurityOptions{ TwoFactor = new TwoFactorOptions{ Issuer="CP6", CodeWindow=1 } }));
    [Fact] public void Secret_generated_and_uri_contains_issuer_and_user()
    {
        var svc = Make(); var secret = svc.GenerateSecret();
        Assert.False(string.IsNullOrWhiteSpace(secret));
        var uri = svc.BuildOtpAuthUri(secret, "alice");
        Assert.StartsWith("otpauth://totp/", uri);
        Assert.Contains("issuer=CP6", uri);
        Assert.Contains("alice", uri);
    }
    [Fact] public void Verify_accepts_current_code_rejects_wrong()
    {
        var svc = Make(); var secret = svc.GenerateSecret();
        var now = new OtpNet.Totp(OtpNet.Base32Encoding.ToBytes(secret)).ComputeTotp();
        Assert.True(svc.Verify(secret, now));
        Assert.False(svc.Verify(secret, "000000"));
        Assert.False(svc.Verify(secret, ""));
    }
}
