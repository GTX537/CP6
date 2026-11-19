using System.Net.Http.Headers;
using System.Net.Http.Json;
using CP6.Client.Api;
using Microsoft.Extensions.Logging;

namespace CP6.Client.Core;

public readonly record struct NativeAuthAttempt(long Version, Uri? Server, string? DeviceId);

public interface IClientSessionService
{
    event EventHandler<TokenSession?>? SessionChanged;
    TokenSession? Current { get; }
    string? AccessToken { get; }
    long InteractionVersion => 0;
    Task<bool> RestoreAsync(CancellationToken ct = default);
    Task<NativeAuthResult> LoginAsync(string userName, string password, string? tenantCode, CancellationToken ct = default);
    Task<NativeAuthResult> QuickSwitchAsync(string tenantCode, string badgeNo, string pin, CancellationToken ct = default);
    Task<TwoFactorSetup> SetupTwoFactorAsync(string challenge, CancellationToken ct = default);
    Task RequestEmailOtpAsync(string challenge, CancellationToken ct = default);
    Task<NativeAuthResult> VerifyTwoFactorAsync(string challenge, string code, string? method, bool enroll, CancellationToken ct = default);
    Task AdoptAsync(TokenSession session, CancellationToken ct = default);
    Task RefreshMergedAsync(string? observedAccessToken = null, CancellationToken ct = default);
    Task LogoutAsync(CancellationToken ct = default);

    Task<NativeAuthAttempt> BeginInteractionAsync(CancellationToken ct = default) => Task.FromResult(default(NativeAuthAttempt));

    Task AcceptInteractionAsync(NativeAuthAttempt attempt, NativeAuthResult result, CancellationToken ct = default)
        => result.Session == null ? Task.CompletedTask : AdoptAsync(result.Session, ct);

    bool IsCurrent(NativeAuthAttempt attempt) => true;

    async Task<bool> InvalidateIfCurrentAsync(TokenSession expected, CancellationToken ct = default)
    {
        if (!ReferenceEquals(Current, expected)) return false;
        await LogoutAsync(ct);
        return true;
    }
}

public sealed class ClientSessionService : IClientSessionService
{
    private readonly HttpClient _transport;
    private readonly IRefreshTokenStore _refreshTokens;
    private readonly ClientOptions _options;
    private readonly NativeDeviceActivationService? _activation;
    private readonly ILogger<ClientSessionService> _logger;
    private readonly SemaphoreSlim _refreshGate = new(1, 1);
    private readonly SemaphoreSlim _stateGate = new(1, 1);
    private TokenSession? _current;
    private PendingFactor? _pending;
    private long _interactionVersion;

    public ClientSessionService(
        IHttpClientFactory clients,
        IRefreshTokenStore refreshTokens,
        ClientOptions options,
        ILogger<ClientSessionService> logger,
        NativeDeviceActivationService? activation = null)
    {
        _transport = clients.CreateClient(ClientServiceCollectionExtensions.RawClient);
        _refreshTokens = refreshTokens;
        _options = options;
        _activation = activation;
        _logger = logger;
    }

    public event EventHandler<TokenSession?>? SessionChanged;
    public TokenSession? Current => Volatile.Read(ref _current);
    public string? AccessToken => Current?.AccessToken;
    public long InteractionVersion => Interlocked.Read(ref _interactionVersion);

    public async Task<NativeAuthAttempt> BeginInteractionAsync(CancellationToken ct = default)
    {
        await _stateGate.WaitAsync(ct);
        try
        {
            InvalidateInteraction();
            return CaptureInteraction();
        }
        finally { _stateGate.Release(); }
    }

    public bool IsCurrent(NativeAuthAttempt attempt)
        => attempt.Version == InteractionVersion && attempt.Server == _options.ApiBaseAddress
            && attempt.DeviceId == _options.Context.DeviceId;

    public async Task AcceptInteractionAsync(NativeAuthAttempt attempt, NativeAuthResult result, CancellationToken ct = default)
    {
        await _stateGate.WaitAsync(ct);
        try
        {
            RequireCurrent(attempt);
            _pending = result.ChallengeToken is { Length: > 0 } challenge
                ? new PendingFactor(attempt, challenge) : null;
            if (result.Session != null)
            {
                InvalidateInteraction();
                await SetSessionAsync(result.Session, ct);
            }
        }
        finally { _stateGate.Release(); }
    }

    public async Task<bool> RestoreAsync(CancellationToken ct = default)
    {
        NativeAuthAttempt attempt;
        string? refreshToken;
        await _stateGate.WaitAsync(ct);
        try
        {
            if (Current != null) return true;
            attempt = CaptureInteraction();
            refreshToken = await _refreshTokens.ReadAsync(ct);
        }
        finally { _stateGate.Release(); }
        if (string.IsNullOrWhiteSpace(refreshToken)) return false;
        try
        {
            RequireCurrent(attempt);
            var restored = await RefreshAsync(attempt, refreshToken, ct);
            return await SetRefreshResultAsync(attempt, null, restored, ct);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
        catch (Exception ex)
        {
            _logger.LogWarning("Stored CP6 session could not be restored: {ErrorType}", ex.GetType().Name);
            await SetRefreshResultAsync(attempt, null, null, ct);
            return false;
        }
    }

    public async Task<NativeAuthResult> LoginAsync(
        string userName, string password, string? tenantCode, CancellationToken ct = default)
    {
        var attempt = await BeginInteractionAsync(ct);
        var result = await AnonymousApi(attempt).LoginAsync(new NativeLoginRequest
        {
            UserName = userName,
            Password = password,
            TenantCode = tenantCode,
            Client = await AuthenticationContextAsync(ct),
        }, ct);
        await AcceptInteractionAsync(attempt, result, ct);
        return result;
    }

    public async Task<NativeAuthResult> QuickSwitchAsync(
        string tenantCode, string badgeNo, string pin, CancellationToken ct = default)
    {
        var attempt = await BeginInteractionAsync(ct);
        var result = await AnonymousApi(attempt).QuickSwitchAsync(new QuickSwitchRequest
        {
            TenantCode = tenantCode,
            BadgeNo = badgeNo,
            Pin = pin,
            Client = await AuthenticationContextAsync(ct),
        }, ct);
        await AcceptInteractionAsync(attempt, result, ct);
        return result;
    }

    public async Task<NativeAuthResult> VerifyTwoFactorAsync(
        string challenge, string code, string? method, bool enroll, CancellationToken ct = default)
    {
        var attempt = RequirePending(challenge);
        var request = new NativeTwoFactorRequest
        {
            ChallengeToken = challenge,
            Code = code,
            Method = method,
            Client = await AuthenticationContextAsync(ct),
        };
        var result = enroll
            ? await AnonymousApi(attempt).EnrollTwoFactorAsync(request, ct)
            : await AnonymousApi(attempt).VerifyTwoFactorAsync(request, ct);
        await AcceptInteractionAsync(attempt, result, ct);
        return result;
    }

    public async Task<TwoFactorSetup> SetupTwoFactorAsync(string challenge, CancellationToken ct = default)
    {
        var attempt = RequirePending(challenge);
        var result = await AnonymousApi(attempt).SetupTwoFactorAsync(new NativeChallengeRequest
        {
            ChallengeToken = challenge,
            Client = await AuthenticationContextAsync(ct),
        }, ct);
        RequireCurrent(attempt);
        return result;
    }

    public async Task RequestEmailOtpAsync(string challenge, CancellationToken ct = default)
    {
        var attempt = RequirePending(challenge);
        await AnonymousApi(attempt).RequestEmailOtpAsync(new NativeChallengeRequest
        {
            ChallengeToken = challenge,
            Client = await AuthenticationContextAsync(ct),
        }, ct);
        RequireCurrent(attempt);
    }

    public async Task RefreshMergedAsync(string? observedAccessToken = null, CancellationToken ct = default)
    {
        await _refreshGate.WaitAsync(ct);
        try
        {
            var attempt = CaptureInteraction();
            var expected = Current;
            if (!string.IsNullOrWhiteSpace(observedAccessToken)
                && !string.Equals(observedAccessToken, expected?.AccessToken, StringComparison.Ordinal))
                return;

            var refreshToken = expected?.RefreshToken;
            if (string.IsNullOrWhiteSpace(refreshToken)) throw new SessionExpiredException();
            try
            {
                if (!IsCurrent(attempt)) return;
                var replacement = await RefreshAsync(attempt, refreshToken, ct);
                await SetRefreshResultAsync(attempt, expected, replacement, ct);
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
            catch
            {
                if (await SetRefreshResultAsync(attempt, expected, null, ct))
                    throw new SessionExpiredException();
            }
        }
        finally { _refreshGate.Release(); }
    }

    public async Task AdoptAsync(TokenSession session, CancellationToken ct = default)
    {
        var attempt = await BeginInteractionAsync(ct);
        await AcceptInteractionAsync(attempt, new NativeAuthResult { Session = session }, ct);
    }

    public async Task<bool> InvalidateIfCurrentAsync(TokenSession expected, CancellationToken ct = default)
    {
        await _stateGate.WaitAsync(ct);
        try
        {
            if (!ReferenceEquals(expected, Current)) return false;
            InvalidateInteraction();
            await SetSessionAsync(null, CancellationToken.None);
            return true;
        }
        finally { _stateGate.Release(); }
    }

    public async Task LogoutAsync(CancellationToken ct = default)
    {
        TokenSession? session;
        NativeAuthAttempt original;
        await _stateGate.WaitAsync(ct);
        try
        {
            session = Current;
            original = CaptureInteraction();
            InvalidateInteraction();
            await SetSessionAsync(null, CancellationToken.None);
        }
        finally { _stateGate.Release(); }
        if (session == null) return;
        using var request = new HttpRequestMessage(HttpMethod.Post, new Uri(original.Server!, "api/client-auth/logout"))
        {
            Content = JsonContent.Create(new NativeLogoutRequest
            {
                RefreshToken = session.RefreshToken,
                Client = _options.Context,
            }),
        };
        request.Options.Set(ClientRequestScope.Key, RawScope(original));
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", session.AccessToken);
        using var response = await _transport.SendAsync(request, ct);
    }

    private async Task<TokenSession> RefreshAsync(NativeAuthAttempt attempt, string refreshToken, CancellationToken ct)
        => await AnonymousApi(attempt).RefreshAsync(new NativeRefreshRequest
            { RefreshToken = refreshToken, Client = await AuthenticationContextAsync(ct) }, ct);

    private Cp6ApiClient AnonymousApi(NativeAuthAttempt attempt) => new(_transport, RawScope(attempt));

    private static ClientRequestScope RawScope(NativeAuthAttempt attempt)
        => new(attempt.Server!, Guid.Empty, Guid.Empty, attempt.DeviceId!);

    private Task<ClientContext> AuthenticationContextAsync(CancellationToken ct)
        => _activation?.AuthenticationContextAsync(ct) ?? Task.FromResult(_options.Context);

    private NativeAuthAttempt CaptureInteraction()
        => new(InteractionVersion, _options.ApiBaseAddress, _options.Context.DeviceId);

    private NativeAuthAttempt RequirePending(string challenge)
    {
        var pending = Volatile.Read(ref _pending);
        if (pending == null || pending.Challenge != challenge) throw new ClientInteractionChangedException();
        RequireCurrent(pending.Attempt);
        return pending.Attempt;
    }

    private void RequireCurrent(NativeAuthAttempt attempt)
    {
        if (!IsCurrent(attempt)) throw new ClientInteractionChangedException();
    }

    private void InvalidateInteraction()
    {
        Interlocked.Increment(ref _interactionVersion);
        _pending = null;
    }

    private async Task<bool> SetRefreshResultAsync(NativeAuthAttempt attempt, TokenSession? expected, TokenSession? replacement, CancellationToken ct)
    {
        await _stateGate.WaitAsync(ct);
        try
        {
            if (!IsCurrent(attempt) || !ReferenceEquals(expected, Current)) return false;
            if (replacement == null) InvalidateInteraction();
            await SetSessionAsync(replacement, replacement == null ? CancellationToken.None : ct);
            return true;
        }
        finally { _stateGate.Release(); }
    }

    private async Task SetSessionAsync(TokenSession? session, CancellationToken ct)
    {
        if (session == null)
        {
            Interlocked.Exchange(ref _current, null);
            await _refreshTokens.ClearAsync(ct);
        }
        else
        {
            await _refreshTokens.WriteAsync(session.RefreshToken, ct);
            Interlocked.Exchange(ref _current, session);
        }
        SessionChanged?.Invoke(this, session);
    }

    private sealed record PendingFactor(NativeAuthAttempt Attempt, string Challenge);
}

public sealed class SessionExpiredException : InvalidOperationException
{
    public SessionExpiredException() : base("E-CLIENT-SESSION-EXPIRED") { }
}

public sealed class ClientInteractionChangedException : InvalidOperationException
{
    public ClientInteractionChangedException() : base("当前认证或操作上下文已变化，请按原操作重新核对。") { }
}
