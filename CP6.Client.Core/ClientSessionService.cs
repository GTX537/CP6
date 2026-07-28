using System.Net.Http.Headers;
using CP6.Client.Api;
using Microsoft.Extensions.Logging;

namespace CP6.Client.Core;

public interface IClientSessionService
{
    event EventHandler<TokenSession?>? SessionChanged;
    TokenSession? Current { get; }
    string? AccessToken { get; }
    Task<bool> RestoreAsync(CancellationToken ct = default);
    Task<NativeAuthResult> LoginAsync(string userName, string password, string? tenantCode, CancellationToken ct = default);
    Task<NativeAuthResult> QuickSwitchAsync(string tenantCode, string badgeNo, string pin, CancellationToken ct = default);
    Task<TwoFactorSetup> SetupTwoFactorAsync(string challenge, CancellationToken ct = default);
    Task RequestEmailOtpAsync(string challenge, CancellationToken ct = default);
    Task<NativeAuthResult> VerifyTwoFactorAsync(string challenge, string code, string? method, bool enroll, CancellationToken ct = default);
    Task AdoptAsync(TokenSession session, CancellationToken ct = default);
    Task RefreshMergedAsync(string? observedAccessToken = null, CancellationToken ct = default);
    Task LogoutAsync(CancellationToken ct = default);
}

public sealed class ClientSessionService : IClientSessionService
{
    private readonly Cp6ApiClient _anonymousApi;
    private readonly HttpClient _transport;
    private readonly IRefreshTokenStore _refreshTokens;
    private readonly ClientOptions _options;
    private readonly ILogger<ClientSessionService> _logger;
    private readonly SemaphoreSlim _refreshGate = new(1, 1);
    private TokenSession? _current;

    public ClientSessionService(
        IHttpClientFactory clients,
        IRefreshTokenStore refreshTokens,
        ClientOptions options,
        ILogger<ClientSessionService> logger)
    {
        _transport = clients.CreateClient(ClientServiceCollectionExtensions.RawClient);
        _anonymousApi = new Cp6ApiClient(_transport);
        _refreshTokens = refreshTokens;
        _options = options;
        _logger = logger;
    }

    public event EventHandler<TokenSession?>? SessionChanged;
    public TokenSession? Current => Volatile.Read(ref _current);
    public string? AccessToken => Current?.AccessToken;

    public async Task<bool> RestoreAsync(CancellationToken ct = default)
    {
        var refreshToken = await _refreshTokens.ReadAsync(ct);
        if (string.IsNullOrWhiteSpace(refreshToken)) return false;
        try
        {
            await AcceptAsync(await _anonymousApi.RefreshAsync(new NativeRefreshRequest
            {
                RefreshToken = refreshToken,
                Client = _options.Context,
            }, ct), ct);
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogWarning("Stored CP6 session could not be restored: {ErrorType}", ex.GetType().Name);
            await ClearAsync(ct);
            return false;
        }
    }

    public async Task<NativeAuthResult> LoginAsync(
        string userName,
        string password,
        string? tenantCode,
        CancellationToken ct = default)
    {
        var result = await _anonymousApi.LoginAsync(new NativeLoginRequest
        {
            UserName = userName,
            Password = password,
            TenantCode = tenantCode,
            Client = _options.Context,
        }, ct);
        if (result.Session != null) await AcceptAsync(result.Session, ct);
        return result;
    }

    public async Task<NativeAuthResult> QuickSwitchAsync(
        string tenantCode,
        string badgeNo,
        string pin,
        CancellationToken ct = default)
    {
        var result = await _anonymousApi.QuickSwitchAsync(new QuickSwitchRequest
        {
            TenantCode = tenantCode,
            BadgeNo = badgeNo,
            Pin = pin,
            Client = _options.Context,
        }, ct);
        if (result.Session != null) await AcceptAsync(result.Session, ct);
        return result;
    }

    public async Task<NativeAuthResult> VerifyTwoFactorAsync(
        string challenge,
        string code,
        string? method,
        bool enroll,
        CancellationToken ct = default)
    {
        var request = new NativeTwoFactorRequest
        {
            ChallengeToken = challenge,
            Code = code,
            Method = method,
            Client = _options.Context,
        };
        var result = enroll
            ? await _anonymousApi.EnrollTwoFactorAsync(request, ct)
            : await _anonymousApi.VerifyTwoFactorAsync(request, ct);
        if (result.Session != null) await AcceptAsync(result.Session, ct);
        return result;
    }

    public Task<TwoFactorSetup> SetupTwoFactorAsync(
        string challenge,
        CancellationToken ct = default)
        => _anonymousApi.SetupTwoFactorAsync(new NativeChallengeRequest
        {
            ChallengeToken = challenge,
            Client = _options.Context,
        }, ct);

    public Task RequestEmailOtpAsync(
        string challenge,
        CancellationToken ct = default)
        => _anonymousApi.RequestEmailOtpAsync(new NativeChallengeRequest
        {
            ChallengeToken = challenge,
            Client = _options.Context,
        }, ct);

    public async Task RefreshMergedAsync(
        string? observedAccessToken = null,
        CancellationToken ct = default)
    {
        await _refreshGate.WaitAsync(ct);
        try
        {
            if (!string.IsNullOrWhiteSpace(observedAccessToken)
                && !string.Equals(observedAccessToken, AccessToken, StringComparison.Ordinal))
                return;

            var refreshToken = Current?.RefreshToken ?? await _refreshTokens.ReadAsync(ct);
            if (string.IsNullOrWhiteSpace(refreshToken))
                throw new SessionExpiredException();

            try
            {
                var session = await _anonymousApi.RefreshAsync(new NativeRefreshRequest
                {
                    RefreshToken = refreshToken,
                    Client = _options.Context,
                }, ct);
                await AcceptAsync(session, ct);
            }
            catch
            {
                await ClearAsync(ct);
                throw new SessionExpiredException();
            }
        }
        finally
        {
            _refreshGate.Release();
        }
    }

    public Task AdoptAsync(TokenSession session, CancellationToken ct = default)
        => AcceptAsync(session, ct);

    public async Task LogoutAsync(CancellationToken ct = default)
    {
        var session = Current;
        try
        {
            if (session != null)
            {
                using var request = new HttpRequestMessage(HttpMethod.Post, "api/client-auth/logout")
                {
                    Content = System.Net.Http.Json.JsonContent.Create(new NativeLogoutRequest
                    {
                        RefreshToken = session.RefreshToken,
                        Client = _options.Context,
                    }),
                };
                request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", session.AccessToken);
                using var response = await _transport.SendAsync(request, ct);
            }
        }
        finally
        {
            await ClearAsync(ct);
        }
    }

    private async Task AcceptAsync(TokenSession session, CancellationToken ct)
    {
        await _refreshTokens.WriteAsync(session.RefreshToken, ct);
        Interlocked.Exchange(ref _current, session);
        SessionChanged?.Invoke(this, session);
    }

    private async Task ClearAsync(CancellationToken ct)
    {
        Interlocked.Exchange(ref _current, null);
        await _refreshTokens.ClearAsync(ct);
        SessionChanged?.Invoke(this, null);
    }
}

public sealed class SessionExpiredException : InvalidOperationException
{
    public SessionExpiredException() : base("E-CLIENT-SESSION-EXPIRED") { }
}
