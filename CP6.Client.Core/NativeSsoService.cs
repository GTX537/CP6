using System.Security.Cryptography;
using System.Text.Json;
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

/// <summary>A support locator from an error callback, never proof of mapping success or authorization.</summary>
public sealed class NativeSsoMappingRecoveryException(string operationRef)
    : InvalidOperationException("SSO 身份映射结果待确认，请由本租户管理员查询原操作：" + operationRef)
{
    public string OperationRef { get; } = operationRef;
}

public sealed class NativeSsoService
{
    private readonly HttpClient _transport;
    private readonly ClientOptions _options;
    private readonly ISystemBrowser _browser;
    private readonly IClientSessionService _sessions;
    private readonly IPkceVerifierStore _verifiers;
    private readonly NativeDeviceActivationService? _activation;
    private readonly Guid _instance = Guid.NewGuid();
    private readonly SemaphoreSlim _flow = new(1, 1);

    public NativeSsoService(
        IHttpClientFactory clients,
        ClientOptions options,
        ISystemBrowser browser,
        IClientSessionService sessions,
        IPkceVerifierStore verifiers,
        NativeDeviceActivationService? activation = null)
    {
        _transport = clients.CreateClient(ClientServiceCollectionExtensions.RawClient);
        _options = options;
        _browser = browser;
        _sessions = sessions;
        _verifiers = verifiers;
        _activation = activation;
    }

    public Task StartAsync(string tenantCode, CancellationToken ct = default)
        => InFlowAsync(async () =>
        {
            await StartCoreAsync(tenantCode, ct);
            return true;
        }, ct);

    private async Task StartCoreAsync(string tenantCode, CancellationToken ct)
    {
        var redirectUri = ExpectedRedirectUri().AbsoluteUri;
        var previous = await TakePendingAsync(ct);
        if (previous is { Grant: not null }) await RecoverAsync(previous, ct);
        var attempt = await _sessions.BeginInteractionAsync(ct);
        var verifier = Base64Url(RandomNumberGenerator.GetBytes(32));
        var pending = new PendingSso(verifier, _instance, attempt, _options.ApiBaseAddress, _options.Context.DeviceId,
            CurrentActor(), null);
        await SavePendingAsync(pending, ct);
        var challenge = Base64Url(SHA256.HashData(System.Text.Encoding.ASCII.GetBytes(verifier)));
        var response = await Api(pending).StartSsoAsync(new NativeSsoStartRequest
        {
            TenantCode = tenantCode,
            RedirectUri = redirectUri,
            CodeChallenge = challenge,
            Client = await AuthenticationContextAsync(ct),
        }, ct);
        if (!_sessions.IsCurrent(attempt)) throw new ClientInteractionChangedException();
        await _browser.OpenAsync(new Uri(response.AuthorizeUrl), ct);
    }

    public Task<NativeAuthResult> CompleteAsync(Uri callback, CancellationToken ct = default)
        => InFlowAsync(() => CompleteCoreAsync(callback, ct), ct);

    private async Task<T> InFlowAsync<T>(Func<Task<T>> action, CancellationToken ct)
    {
        await _flow.WaitAsync(ct);
        try { return await action(); }
        finally { _flow.Release(); }
    }

    private async Task<NativeAuthResult> CompleteCoreAsync(Uri callback, CancellationToken ct)
    {
        if (!IsExpectedCallback(callback) ||
            !TryParseQuery(callback.Query, out var query))
            throw new InvalidOperationException("E-CLIENT-SSO-CALLBACK");
        if (query.TryGetValue("error", out var error) && error == "SSO_IDENTITY_MAPPING_UNKNOWN"
            && !query.ContainsKey("grantCode") && query.TryGetValue("operationRef", out var reference)
            && reference.Length == 64 && reference.All(Uri.IsHexDigit))
        {
            // Deep links are untrusted: do not exchange a grant, consume PKCE, adopt a session,
            // query other tenants, or automatically close an operation based on this display hint.
            throw new NativeSsoMappingRecoveryException(reference);
        }
        if (query.ContainsKey("error") ||
            !query.TryGetValue("grantCode", out var grantCode) ||
            !IsBase64Url(grantCode))
        {
            throw new InvalidOperationException("E-CLIENT-SSO-CALLBACK");
        }

        var pending = await TakePendingAsync(ct) ?? throw new InvalidOperationException("E-CLIENT-SSO-NOT-STARTED");
        if (pending.Server != _options.ApiBaseAddress || pending.Device != _options.Context.DeviceId)
            throw new ClientInteractionChangedException();
        if (pending.Grant != null)
        {
            if (pending.Grant != grantCode)
            {
                await SavePendingAsync(pending, ct);
                throw new ClientInteractionChangedException();
            }
            await RecoverAsync(pending, ct);
            throw new InvalidOperationException("原认证交互已关闭，请重新认证。");
        }

        var attempt = pending.Attempt;
        if (pending.Instance != _instance)
        {
            // Windows 可由外部浏览器唤起新进程。只延续同来源、设备及启动前相同主体的未完成交互。
            if (_sessions.InteractionVersion != 0 || pending.Actor != CurrentActor())
                throw new ClientInteractionChangedException();
            attempt = await _sessions.BeginInteractionAsync(ct);
        }
        if (!_sessions.IsCurrent(attempt)) throw new ClientInteractionChangedException();
        var context = await AuthenticationContextAsync(ct);
        var attempted = pending with { Grant = grantCode };
        // 先保存原交互，进程退出或确认丢失以后只能读回，不能重新换发。
        await SavePendingAsync(attempted, ct);
        NativeAuthResult result;
        try
        {
            result = await Api(pending).ExchangeSsoAsync(new NativeSsoExchangeRequest
            {
                GrantCode = grantCode,
                CodeVerifier = pending.Verifier,
                Client = context,
            }, ct);
        }
        catch (Exception failure) when (IsUnknown(failure, ct))
        {
            await RecoverAsync(attempted, ct);
            throw new InvalidOperationException("原认证交互已关闭，请重新认证。");
        }
        catch (ApiException)
        {
            await _verifiers.TakeAsync(CancellationToken.None);
            throw;
        }
        await _sessions.AcceptInteractionAsync(attempt, result, ct);
        await _verifiers.TakeAsync(CancellationToken.None);
        return result;
    }

    private async Task RecoverAsync(PendingSso pending, CancellationToken ct)
    {
        await SavePendingAsync(pending, ct);
        if (pending.Server != _options.ApiBaseAddress || pending.Device != _options.Context.DeviceId)
            throw new ClientInteractionChangedException();
        var request = new NativeCompletionRecoveryRequest
        {
            Kind = "native-grant",
            Credential = pending.Grant!,
            CodeVerifier = pending.Verifier,
            Client = await AuthenticationContextAsync(ct),
        };
        try
        {
            var original = await Api(pending).GetCompletionStatusAsync(request, ct);
            var closed = original.State == "CLOSED" ? original : await Api(pending).CloseCompletionAsync(request, ct);
            if (closed.State == "CLOSED")
            {
                await _verifiers.TakeAsync(CancellationToken.None);
                return;
            }
        }
        catch (HttpRequestException)
        {
            // 查询尚不能证明原交互已收敛。保留同一原 grant，仅允许再次查询。
        }
        throw new InvalidOperationException("原认证结果仍待确认，请联网后重新查询。");
    }

    private async Task<PendingSso?> TakePendingAsync(CancellationToken ct)
    {
        var saved = await _verifiers.TakeAsync(ct);
        if (string.IsNullOrEmpty(saved)) return null;
        if (saved.StartsWith('{')) return JsonSerializer.Deserialize<PendingSso>(saved);
        // 兼容旧版加密区的单一 verifier。仅在当前无会话的启动上下文接续。
        return new(saved, Guid.Empty, default, _options.ApiBaseAddress, _options.Context.DeviceId, null, null);
    }

    private Task SavePendingAsync(PendingSso pending, CancellationToken ct)
        => _verifiers.WriteAsync(JsonSerializer.Serialize(pending), ct);

    private Cp6ApiClient Api(PendingSso pending)
        => new(_transport, new ClientRequestScope(pending.Server, Guid.Empty, Guid.Empty, pending.Device));

    private Task<ClientContext> AuthenticationContextAsync(CancellationToken ct)
        => _activation?.AuthenticationContextAsync(ct) ?? Task.FromResult(_options.Context);

    private string? CurrentActor()
    {
        if (_sessions.Current == null) return null;
        var scope = ClientLabelGatewayContext.ReadIdentity(_sessions.AccessToken, _options.ApiBaseAddress, _options.Context.DeviceId);
        return scope.TenantId.ToString("D") + "/" + scope.ActorId.ToString("D");
    }

    private static bool IsUnknown(Exception error, CancellationToken ct)
        => error is ApiException api ? (int?)api.StatusCode >= 500
            : error is HttpRequestException or JsonException || error is OperationCanceledException && !ct.IsCancellationRequested;

    private sealed record PendingSso(string Verifier, Guid Instance, NativeAuthAttempt Attempt, Uri Server, string Device,
        string? Actor, string? Grant);

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
