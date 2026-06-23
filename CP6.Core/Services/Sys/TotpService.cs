using OtpNet;
using Microsoft.Extensions.Options;
namespace CP6.Core.Services.Sys;
public class TotpService : ITotpService
{
    private readonly TwoFactorOptions _o;
    public TotpService(IOptions<SecurityOptions> opt) => _o = opt.Value.TwoFactor;
    public string GenerateSecret() => Base32Encoding.ToString(KeyGeneration.GenerateRandomKey(20));
    public string BuildOtpAuthUri(string secret, string userName)
    {
        var iss = Uri.EscapeDataString(_o.Issuer); var u = Uri.EscapeDataString(userName);
        return $"otpauth://totp/{iss}:{u}?secret={secret}&issuer={iss}&period=30&digits=6";
    }
    public bool Verify(string secret, string code)
    {
        if (string.IsNullOrWhiteSpace(code)) return false;
        try { return new Totp(Base32Encoding.ToBytes(secret)).VerifyTotp(code, out _, new VerificationWindow(previous: _o.CodeWindow, future: _o.CodeWindow)); }
        catch { return false; }
    }
}
