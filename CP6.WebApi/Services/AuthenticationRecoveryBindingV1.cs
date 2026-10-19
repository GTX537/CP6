using System.Text.Json;

namespace CP6.WebApi.Services;

public static class AuthenticationRecoveryBindingV1
{
    public const string Header = "X-CP6-Authentication-Completion";

    // Optional client-held 256-bit attempt secret. Existing clients retain their existing flows;
    // without such a secret an initial response loss cannot be recovered by guessing a device/id.
    public static string Challenge(HttpRequest request)
    {
        if (!request.Headers.TryGetValue(Header, out var values)) return CP6.Core.Services.Sys.AuthenticationInteractionStoreV1.NewChallenge();
        if (values.Count != 1 || values[0] is not { Length: 64 } key || key.Any(x => !Uri.IsHexDigit(x)))
            throw new InvalidOperationException("REAUTH_REQUIRED");
        return key;
    }

    public static string NativeGrant(string grant, string verifier)
        => CP6.Core.Services.Sys.AuthenticationInteractionStoreV1.Hash(JsonSerializer.Serialize(new[] { "native-sso-recovery-v1", grant, verifier }));
}
