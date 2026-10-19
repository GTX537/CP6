using System.Security.Cryptography;
using System.Text;
using CP6.Entity.DomainModels.Sys;

namespace CP6.Core.Services.Sys;

/// <summary>Non-secret account state version; lets downstream grants reject an older login after credential changes.</summary>
public static class AuthSessionVersion
{
    public static string For(Sys_User user) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(
        System.Text.Json.JsonSerializer.Serialize(new object?[] { "cp6-auth-state-v3",
            user.CredentialRevision, user.AuthenticationEpoch, user.Password, StoredTicks(user.PasswordChangedAt),
            user.TwoFactorEnabled, StoredTicks(user.TwoFactorEnrolledAt), user.TwoFactorEnabled ? user.TwoFactorSecret : null,
            user.ExternalProvider, user.ExternalSubject, user.QuickPinHash, user.MustChangePassword, user.AllowPasswordFallback }))));

    // PostgreSQL stores microseconds. A just-persisted tracked entity can still carry .NET's
    // final 100 ns digit; hash the same persisted instant before and after a database read.
    private static long? StoredTicks(DateTime? value) => value is { } time ? time.Ticks / 10 * 10 : null;
}
