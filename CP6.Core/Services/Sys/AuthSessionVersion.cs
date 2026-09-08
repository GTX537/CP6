using System.Security.Cryptography;
using System.Text;
using CP6.Entity.DomainModels.Sys;

namespace CP6.Core.Services.Sys;

/// <summary>Non-secret account state version; lets downstream grants reject an older login after credential changes.</summary>
public static class AuthSessionVersion
{
    public static string For(Sys_User user) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(
        string.Join('|', user.AuthenticationEpoch.ToString(), user.Password,
            user.PasswordChangedAt?.Ticks.ToString(), user.TwoFactorEnabled.ToString(),
            user.TwoFactorEnrolledAt?.Ticks.ToString(), user.ExternalProvider, user.ExternalSubject))));
}
