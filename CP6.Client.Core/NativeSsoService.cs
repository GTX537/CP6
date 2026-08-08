using System.Security.Cryptography;
using CP6.Client.Api;

namespace CP6.Client.Core;

public interface ISystemBrowser
{
    Task OpenAsync(Uri uri, CancellationToken ct = default);
}

public interface IPkceVerifierStore
{
    Task WriteAsync(string verifier, CancellationToken ct = default);
    Task<string?> TakeAsync(CancellationToken ct = default);
}

public sealed class MemoryPkceVerifierStore : IPkceVerifierStore
{
    private string? _value;

    public Task WriteAsync(string verifier, CancellationToken ct = default)
    {
        Interlocked.Exchange(ref _value, verifier);
        return Task.CompletedTask;
    }

    public Task<string?> TakeAsync(CancellationToken ct = default)
        => Task.FromResult(Interlocked.Exchange(ref _value, null));
}

public sealed class NativeSsoService
{
    private readonly Cp6ApiClient _api;
    private readonly ClientOptions _options;
    private readonly ISystemBrowser _browser;
    private readonly IClientSessionService _sessions;
    private readonly IPkceVerifierStore _verifiers;

    public NativeSsoService(
        IHttpClientFactory clients,
        ClientOptions options,
        ISystemBrowser browser,
        IClientSessionService sessions,
        IPkceVerifierStore verifiers)
    {
        _api = new Cp6ApiClient(clients.CreateClient(ClientServiceCollectionExtensions.RawClient));
        _options = options;
        _browser = browser;
        _sessions = sessions;
        _verifiers = verifiers;
    }

    public async Task StartAsync(
        string tenantCode,
        CancellationToken ct = default)
    {
        var redirectUri = ExpectedRedirectUri().AbsoluteUri;
        var verifier = Base64Url(RandomNumberGenerator.GetBytes(32));
        await _verifiers.WriteAsync(verifier, ct);
        var challenge = Base64Url(SHA256.HashData(System.Text.Encoding.ASCII.GetBytes(verifier)));
        var response = await _api.StartSsoAsync(new NativeSsoStartRequest
        {
            TenantCode = tenantCode,
            RedirectUri = redirectUri,
            CodeChallenge = challenge,
            Client = _options.Context,
        }, ct);
        await _browser.OpenAsync(new Uri(response.AuthorizeUrl), ct);
    }

    public async Task<NativeAuthResult> CompleteAsync(Uri callback, CancellationToken ct = default)
    {
        if (!IsExpectedCallback(callback) ||
            !TryParseQuery(callback.Query, out var query) ||
            query.ContainsKey("error") ||
            !query.TryGetValue("grantCode", out var grantCode) ||
            !IsBase64Url(grantCode))
        {
            throw new InvalidOperationException("E-CLIENT-SSO-CALLBACK");
        }

        var verifier = await _verifiers.TakeAsync(ct)
                       ?? throw new InvalidOperationException(
                           "E-CLIENT-SSO-NOT-STARTED");
        var result = await _api.ExchangeSsoAsync(new NativeSsoExchangeRequest
        {
            GrantCode = grantCode,
            CodeVerifier = verifier,
            Client = _options.Context,
        }, ct);
        if (result.Session != null) await _sessions.AdoptAsync(result.Session, ct);
        return result;
    }

    private Uri ExpectedRedirectUri()
    {
        var value = _options.Platform.ToLowerInvariant() switch
        {
            "windows" => "cp6-desktop://auth/callback",
            "android" => "cp6-mobile://auth/callback",
            _ => throw new InvalidOperationException(
                "E-CLIENT-SSO-PLATFORM")
        };
        return new Uri(value);
    }

    private bool IsExpectedCallback(Uri callback)
    {
        if (!callback.IsAbsoluteUri ||
            !string.IsNullOrEmpty(callback.UserInfo) ||
            !string.IsNullOrEmpty(callback.Fragment))
        {
            return false;
        }

        var expected = ExpectedRedirectUri();
        return string.Equals(
                   callback.Scheme,
                   expected.Scheme,
                   StringComparison.OrdinalIgnoreCase) &&
               string.Equals(
                   callback.Host,
                   expected.Host,
                   StringComparison.OrdinalIgnoreCase) &&
               string.Equals(
                   callback.AbsolutePath,
                   expected.AbsolutePath,
                   StringComparison.Ordinal);
    }

    private static bool TryParseQuery(
        string query,
        out Dictionary<string, string> values)
    {
        values = new Dictionary<string, string>(
            StringComparer.OrdinalIgnoreCase);
        try
        {
            foreach (var item in query
                         .TrimStart('?')
                         .Split(
                             '&',
                             StringSplitOptions.RemoveEmptyEntries))
            {
                var parts = item.Split('=', 2);
                var key = Uri.UnescapeDataString(parts[0]);
                var value = parts.Length == 2
                    ? Uri.UnescapeDataString(parts[1])
                    : string.Empty;
                if (string.IsNullOrWhiteSpace(key) ||
                    !values.TryAdd(key, value))
                {
                    return false;
                }
            }
            return true;
        }
        catch (UriFormatException)
        {
            return false;
        }
    }

    private static bool IsBase64Url(string? value) =>
        value is { Length: >= 32 and <= 128 } &&
        value.All(character =>
            char.IsAsciiLetterOrDigit(character) ||
            character is '-' or '_');

    private static string Base64Url(byte[] bytes)
        => Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');
}
