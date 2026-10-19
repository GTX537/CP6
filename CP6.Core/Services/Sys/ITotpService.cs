namespace CP6.Core.Services.Sys;
public interface ITotpService
{
    string GenerateSecret();
    string BuildOtpAuthUri(string secret, string userName);
    bool Verify(string secret, string code);
    bool TryVerify(string secret, string code, out long matchedCounter) { matchedCounter = -1; return false; }
}
