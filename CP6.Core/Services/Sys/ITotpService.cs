namespace CP6.Core.Services.Sys;
public interface ITotpService { string GenerateSecret(); string BuildOtpAuthUri(string secret,string userName); bool Verify(string secret,string code); }
