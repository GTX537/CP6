using System.Security.Claims;
using CP6.Core.EFDbContext;
using CP6.Core.Services.Common;
using CP6.Core.Services.Sys;
using CP6.Core.Services.Wms;
using CP6.Entity.DomainModels.Sys;
using CP6.Entity.DomainModels.Wms;
using CP6.Entity.DTOs.Client;
using CP6.WebApi.Localization;
using CP6.WebApi.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace CP6.WebApi.Controllers;

/// <summary>
/// Windows/Android 原生客户端认证。永不写认证 Cookie；access/refresh 仅在 JSON body 返回。
/// Web 登录继续使用 /api/auth 的 httpOnly Cookie 流程。
/// </summary>
[ApiController]
[Route("api/client-auth")]
public sealed partial class ClientAuthController : ControllerBase
{
    private readonly CP6Context _db;
    private readonly ITenantContext _tenant;
    private readonly IPasswordHasher _hasher;
    private readonly IPasswordPolicyService _passwordPolicy;
    private readonly ILoginSecurityService _loginSecurity;
    private readonly ISecurityAuditService _audit;
    private readonly IRefreshTokenService _refreshTokens;
    private readonly ITokenBlacklistService _blacklist;
    private readonly ITenantSsoConfigService _ssoConfig;
    private readonly ISsoService _sso;
    private readonly ISsoStateStore _ssoState;
    private readonly INativeSsoGrantStore _nativeGrants;
    private readonly ITwoFactorService _twoFactor;
    private readonly IPendingTokenStore _pending;
    private readonly IAuthSessionService _sessions;
    private readonly IClientDeviceService _devices;
    private readonly SecurityOptions _security;

    public ClientAuthController(
        CP6Context db,
        ITenantContext tenant,
        IPasswordHasher hasher,
        IPasswordPolicyService passwordPolicy,
        ILoginSecurityService loginSecurity,
        ISecurityAuditService audit,
        IRefreshTokenService refreshTokens,
        ITokenBlacklistService blacklist,
        ITenantSsoConfigService ssoConfig,
        ISsoService sso,
        ISsoStateStore ssoState,
        INativeSsoGrantStore nativeGrants,
        ITwoFactorService twoFactor,
        IPendingTokenStore pending,
        IAuthSessionService sessions,
        IClientDeviceService devices,
        IOptions<SecurityOptions> security)
    {
        _db = db;
        _tenant = tenant;
        _hasher = hasher;
        _passwordPolicy = passwordPolicy;
        _loginSecurity = loginSecurity;
        _audit = audit;
        _refreshTokens = refreshTokens;
        _blacklist = blacklist;
        _ssoConfig = ssoConfig;
        _sso = sso;
        _ssoState = ssoState;
        _nativeGrants = nativeGrants;
        _twoFactor = twoFactor;
        _pending = pending;
        _sessions = sessions;
        _devices = devices;
        _security = security.Value;
    }

    private string? ClientIp => HttpContext.Connection.RemoteIpAddress?.ToString();
    private string? ClientUa => Request.Headers.UserAgent.ToString();

    [HttpPost("login")]
    [AllowAnonymous]
    public async Task<ActionResult<NativeAuthResult>> Login(NativeLoginRequest request)
    {
        ValidateClient(request.Client);
        Sys_User? user;
        if (!string.IsNullOrWhiteSpace(request.TenantCode))
        {
            var code = request.TenantCode.Trim();
            var tenant = await _db.Sys_Tenants.IgnoreQueryFilters()
                .FirstOrDefaultAsync(x => x.TenantCode == code);
            if (tenant is null) throw new BizException("租户不存在");
            if (!tenant.Enable) throw new BizException("租户已停用");
            user = await _db.Sys_Users.IgnoreQueryFilters()
                .FirstOrDefaultAsync(x => x.UserName == request.UserName && x.TenantId == tenant.Id);
        }
        else
        {
            var matches = await _db.Sys_Users.IgnoreQueryFilters()
                .Where(x => x.UserName == request.UserName)
                .Take(2)
                .ToListAsync();
            if (matches.Count > 1)
                return BadRequest(new { message = "该用户名存在于多个租户，请提供租户编码", needTenant = true });
            user = matches.FirstOrDefault();
        }

        if (user is null)
        {
            await _audit.LogAsync(
                SecurityEventType.LoginFailed, null, request.UserName, request.TenantCode,
                ClientIp, ClientUa, ClientReason(request.Client, "user not found"));
            throw new BizException("E-SEC-001");
        }

        try { _loginSecurity.EnsureNotLocked(user); }
        catch (InvalidOperationException)
        {
            await _audit.LogAsync(
                SecurityEventType.AccountLocked, user.Id, user.UserName, request.TenantCode,
                ClientIp, ClientUa, ClientReason(request.Client, "locked"));
            throw new BizException("E-SEC-002");
        }

        var originalPolicy = _db.Database.IsNpgsql() ? await Policies.ReadAsync(user.TenantId, HttpContext.RequestAborted) : null;
        if (!_hasher.Verify(request.Password, user.Password))
        {
            await _loginSecurity.RecordFailureAsync(user);
            await _audit.LogAsync(
                SecurityEventType.LoginFailed, user.Id, user.UserName, request.TenantCode,
                ClientIp, ClientUa, ClientReason(request.Client, "wrong password"));
            throw new BizException("E-SEC-001");
        }
        var originalProof = originalPolicy?.VerifiedPrimary(user, AuthenticationPrimaryMethod.Password, request.Client.ClientKind,
            AuthenticationEntryServiceV1.Binding(request.Client), Guid.NewGuid().ToString("N"), DateTimeOffset.UtcNow);
        if (!user.Enable) throw new BizException("E-SEC-003");

        var tenantState = await _db.Sys_Tenants.IgnoreQueryFilters()
            .FirstOrDefaultAsync(x => x.Id == user.TenantId);
        if (tenantState is { Enable: false }) throw new BizException("租户已停用");

        var sso = await _ssoConfig.GetByTenantIdAsync(user.TenantId);
        if (sso is { Enabled: true, Enforced: true } && !user.AllowPasswordFallback)
            throw new BizException("E-SEC-021");

        _tenant.CurrentTenantId = user.TenantId;
        if (originalProof != null) return await PgNativeResultAsync(() => Entries.BeginAsync(originalProof,
            AuthenticationRecoveryBindingV1.Challenge(Request), request.Client, ClientIp, ClientUa, null, HttpContext.RequestAborted, deferOutput: true));
        var mode = _twoFactor.ResolveTenantMode(user.TenantId);
        if (_twoFactor.IsChallengeRequired(user, mode))
        {
            var purpose = _twoFactor.MustEnroll(user, mode) ? "2fa_enroll" : "2fa_verify";
            var challenge = _pending.Create(user.Id, user.TenantId, purpose);
            return new NativeAuthResult
            {
                State = purpose == "2fa_enroll" ? "enrollmentRequired" : "twoFactorRequired",
                ChallengeToken = challenge
            };
        }

        return new NativeAuthResult
        {
            State = "authenticated",
            Session = await IssueSessionAsync(user, request.Client, "password")
        };
    }

    [HttpPost("2fa/setup")]
    [AllowAnonymous]
    public async Task<IActionResult> TwoFactorSetup(NativeChallengeRequest request)
    {
        if (_db.Database.IsNpgsql()) return Ok(await Entries.SetupAsync(request.ChallengeToken, request.Client, HttpContext.RequestAborted));
        var (user, _, _) = await ReadChallengeAsync(request, expectedPurpose: "2fa_enroll");
        string uri;
        try { uri = _twoFactor.BeginEnrollment(user); }
        catch (InvalidOperationException ex) { throw new BizException(ex.Message); }
        await _db.SaveChangesAsync();
        return Ok(new { otpauthUri = uri, secret = user.TwoFactorSecret });
    }

    [HttpPost("2fa/enroll")]
    [AllowAnonymous]
    public async Task<ActionResult<NativeAuthResult>> TwoFactorEnroll(NativeTwoFactorRequest request)
    {
        if (_db.Database.IsNpgsql()) return await PgNativeResultAsync(() => Entries.CompleteFactorAsync(request.ChallengeToken, request.Client, true, "totp", request.Code, ClientIp, ClientUa, null, HttpContext.RequestAborted, deferOutput: true));
        var (user, challenge, _) = await ReadChallengeAsync(request, "2fa_enroll");
        if (!await _twoFactor.ConfirmEnrollmentAsync(user, request.Code))
        {
            await _audit.LogAsync(
                SecurityEventType.TwoFactorFailed, user.Id, user.UserName, null,
                ClientIp, ClientUa, ClientReason(request.Client, "enroll"));
            throw new BizException("E-SEC-011");
        }
        await _db.SaveChangesAsync();
        _pending.Consume(challenge);
        return new NativeAuthResult
        {
            State = "authenticated",
            Session = await IssueSessionAsync(user, request.Client, "2fa-enroll")
        };
    }

    [HttpPost("2fa/verify")]
    [AllowAnonymous]
    public async Task<ActionResult<NativeAuthResult>> TwoFactorVerify(NativeTwoFactorRequest request)
    {
        if (_db.Database.IsNpgsql()) return await PgNativeResultAsync(() => Entries.CompleteFactorAsync(request.ChallengeToken, request.Client, false, request.Method, request.Code, ClientIp, ClientUa, null, HttpContext.RequestAborted, deferOutput: true));
        var (user, challenge, _) = await ReadChallengeAsync(request, "2fa_verify");
        try { _loginSecurity.EnsureNotLocked(user); }
        catch (InvalidOperationException) { throw new BizException("E-SEC-002"); }

        var ok = string.Equals(request.Method, "email", StringComparison.OrdinalIgnoreCase)
            ? await _twoFactor.VerifyEmailOtpAsync(challenge, request.Code)
            : _twoFactor.VerifyTotp(user, request.Code);
        if (!ok)
        {
            await _loginSecurity.RecordFailureAsync(user);
            await _audit.LogAsync(
                SecurityEventType.TwoFactorFailed, user.Id, user.UserName, null,
                ClientIp, ClientUa, ClientReason(request.Client, request.Method));
            throw new BizException("E-SEC-011");
        }

        _pending.Consume(challenge);
        await _audit.LogAsync(
            SecurityEventType.TwoFactorVerified, user.Id, user.UserName, null,
            ClientIp, ClientUa, ClientReason(request.Client, request.Method));
        return new NativeAuthResult
        {
            State = "authenticated",
            Session = await IssueSessionAsync(user, request.Client, "2fa")
        };
    }

    [HttpPost("2fa/email-otp")]
    [AllowAnonymous]
    public async Task<IActionResult> TwoFactorEmailOtp(NativeChallengeRequest request)
    {
        if (_db.Database.IsNpgsql()) { await Entries.SendEmailAsync(request.ChallengeToken, request.Client, HttpContext.RequestAborted); return Ok(new { code = 0 }); }
        var (user, challenge, purpose) = await ReadChallengeAsync(request);
        if (purpose == "2fa_enroll") throw new BizException("E-SEC-014");
        try { await _twoFactor.SendEmailOtpAsync(user, challenge); }
        catch (InvalidOperationException ex) { throw new BizException(ex.Message); }
        return Ok(new { code = 0 });
    }

    [HttpPost("sso/start")]
    [AllowAnonymous]
    public async Task<ActionResult<NativeSsoStartResponse>> SsoStart(NativeSsoStartRequest request)
    {
        ValidateClient(request.Client);
        string requestId;
        try
        {
            requestId = await _nativeGrants.CreateRequestAsync(
                request.RedirectUri,
                request.CodeChallenge,
                request.Client,
                HttpContext.RequestAborted);
            var url = await _sso.BuildAuthorizeUrlAsync(
                request.TenantCode, NativeSsoCallbackUri(), requestId);
            if (_db.Database.IsNpgsql())
            {
                var state = Microsoft.AspNetCore.WebUtilities.QueryHelpers.ParseQuery(new Uri(url).Query)["state"].ToString();
                await _sso.BindStateAsync(state, request.Client.ClientKind, AuthenticationEntryServiceV1.Binding(request.Client));
            }
            return new NativeSsoStartResponse { AuthorizeUrl = url };
        }
        catch (InvalidOperationException ex) { throw new BizException(ex.Message); }
    }

    [HttpGet("sso/callback")]
    [AllowAnonymous]
    public async Task<IActionResult> SsoCallback(string code, string state)
    {
        var ssoState = _ssoState.Get(state);
        var requestId = ssoState?.ReturnUrl;
        if (string.IsNullOrEmpty(requestId)) throw new BizException("E-SEC-022");
        var request = await _nativeGrants.GetRequestAsync(
            requestId,
            HttpContext.RequestAborted);
        if (request is null) throw new BizException("E-SEC-022");

        try
        {
            if (_db.Database.IsNpgsql())
            {
                var proof = await _sso.HandleVerifiedCallbackAsync(code, state, NativeSsoCallbackUri(), request.Client.ClientKind, AuthenticationEntryServiceV1.Binding(request.Client));
                var preparedGrant = await _nativeGrants.CompleteWithProofAsync(requestId, proof, HttpContext.RequestAborted);
                return Redirect($"{request.RedirectUri}?grantCode={Uri.EscapeDataString(preparedGrant)}");
            }
            var user = await _sso.HandleCallbackAsync(code, state, NativeSsoCallbackUri());
            var grant = await _nativeGrants.CompleteAsync(
                requestId,
                user.Id,
                user.TenantId,
                HttpContext.RequestAborted);
            await _audit.LogAsync(
                SecurityEventType.SsoLoginSuccess, user.Id, user.UserName, null,
                ClientIp, ClientUa, ClientReason(request.Client, "sso"));
            return Redirect($"{request.RedirectUri}?grantCode={Uri.EscapeDataString(grant)}");
        }
        catch (InvalidOperationException ex)
        {
            await _audit.LogAsync(
                SecurityEventType.SsoLoginFailed, null, null, null,
                ClientIp, ClientUa, ClientReason(request.Client, ex.Message));
            var mapping = ex is SsoMappingRecoveryRequiredV1 recovery ? "&operationRef=" + Uri.EscapeDataString(recovery.OperationRef) : "";
            return Redirect(
                $"{request.RedirectUri}?error={Uri.EscapeDataString(ex.Message)}{mapping}");
        }
    }

    [HttpPost("sso/exchange")]
    [AllowAnonymous]
    public async Task<ActionResult<NativeAuthResult>> SsoExchange(NativeSsoExchangeRequest request)
    {
        try
        {
            var grant = await _nativeGrants.ConsumeGrantAsync(
                request.GrantCode,
                request.CodeVerifier,
                request.Client,
                HttpContext.RequestAborted);
            if (_db.Database.IsNpgsql())
            {
                if (grant.Proof == null || grant.Proof.UserId != grant.UserId || grant.Proof.TenantId != grant.TenantId)
                    throw new InvalidOperationException("AUTH_PRIMARY_PROOF_REQUIRED");
                return await PgNativeResultAsync(() => Entries.BeginAsync(grant.Proof, AuthenticationRecoveryBindingV1.NativeGrant(request.GrantCode, request.CodeVerifier),
                    request.Client, ClientIp, ClientUa, null, HttpContext.RequestAborted, deferOutput: true));
            }
            _tenant.CurrentTenantId = grant.TenantId;
            var user = await _db.Sys_Users.IgnoreQueryFilters()
                .FirstOrDefaultAsync(x => x.Id == grant.UserId && x.TenantId == grant.TenantId)
                ?? throw new InvalidOperationException("E-SEC-027");
            if (!user.Enable) throw new InvalidOperationException("E-SEC-027");
            return new NativeAuthResult
            {
                State = "authenticated",
                Session = await IssueSessionAsync(user, request.Client, "sso")
            };
        }
        catch (InvalidOperationException ex) { throw new BizException(ex.Message); }
    }

    [HttpPost("refresh")]
    [AllowAnonymous]
    public async Task<ActionResult<TokenSessionDto>> Refresh(NativeRefreshRequest request)
    {
        ValidateClient(request.Client);
        if (_db.Database.IsNpgsql()) return await PgNativeRefreshAsync(request);
        try
        {
            var (raw, user) = await _refreshTokens.RotateForCompletionAsync(
                request.RefreshToken, ClientIp, ClientUa, ToRefreshContext(request.Client));
            _tenant.CurrentTenantId = user.TenantId;
            try
            {
                await _devices.EnsureLoginAllowedAsync(request.Client, user.TenantId, HttpContext.RequestAborted);
                var mustChange = user.MustChangePassword || _passwordPolicy.IsExpired(user);
                var token = _sessions.BuildAccessToken(user, Guid.NewGuid().ToString(), mustChange, user.IsPlatformAdmin);
                var profile = await _sessions.BuildProfileAsync(user, mustChange);
                await _audit.LogAsync(SecurityEventType.TokenRefreshed, user.Id, user.UserName, null,
                    ClientIp, ClientUa, ClientReason(request.Client, "refresh"));
                HttpContext.RequestAborted.ThrowIfCancellationRequested();
                await _refreshTokens.CompleteRotationAsync(raw);
                HttpContext.RequestAborted.ThrowIfCancellationRequested();
                return new TokenSessionDto
                {
                    AccessToken = token, AccessExpiresAt = _sessions.AccessExpiresAt,
                    RefreshToken = raw, RefreshExpiresAt = _sessions.RefreshExpiresAt, Profile = profile
                };
            }
            catch
            {
                await _refreshTokens.RejectCompletionAsync(raw);
                throw;
            }
        }
        catch (RefreshCompletionUnknownException)
        {
            return StatusCode(503, new { code = "AUTHENTICATION_COMPLETION_OUTCOME_UNKNOWN" });
        }
        catch (InvalidOperationException ex)
        {
            if (ex.Message == "E-SEC-008")
                await _audit.LogAsync(
                    SecurityEventType.TokenReuseDetected, null, null, null,
                    ClientIp, ClientUa, ClientReason(request.Client, "reuse"));
            throw new BizException(ex.Message);
        }
    }

    [HttpPost("logout")]
    [Authorize]
    public async Task<IActionResult> Logout(NativeLogoutRequest request)
    {
        ValidateClient(request.Client);
        var jti = User.FindFirst("jti")?.Value;
        if (!string.IsNullOrEmpty(jti))
        {
            var ttl = TimeSpan.FromMinutes(_security.Token.AccessTokenMinutes);
            if (long.TryParse(User.FindFirst("exp")?.Value, out var exp))
            {
                var remaining = DateTimeOffset.FromUnixTimeSeconds(exp) - DateTimeOffset.UtcNow;
                if (remaining > TimeSpan.Zero) ttl = remaining;
            }
            await _blacklist.BlacklistAsync(jti, ttl);
        }
        if (!string.IsNullOrWhiteSpace(request.RefreshToken))
            await _refreshTokens.RevokeAsync(request.RefreshToken);

        var userId = Guid.TryParse(
            User.FindFirstValue(ClaimTypes.NameIdentifier), out var parsed)
            ? parsed
            : (Guid?)null;
        await _audit.LogAsync(
            SecurityEventType.Logout, userId, User.Identity?.Name, null,
            ClientIp, ClientUa, ClientReason(request.Client, "logout"));
        return Ok(new { code = 0, message = "OK" });
    }

    [HttpPost("quick-switch")]
    [AllowAnonymous]
    public async Task<ActionResult<NativeAuthResult>> QuickSwitch(
        QuickSwitchRequest request,
        CancellationToken ct)
    {
        ValidateClient(request.Client);
        if (string.IsNullOrWhiteSpace(request.TenantCode)
            || string.IsNullOrWhiteSpace(request.BadgeNo)
            || request.Pin.Length != 6
            || request.Pin.Any(ch => !char.IsDigit(ch)))
            throw new BizException("WM-DEVICE-QUICK-SWITCH-DATA");

        var tenant = await _db.Sys_Tenants.IgnoreQueryFilters()
            .FirstOrDefaultAsync(x => x.TenantCode == request.TenantCode, ct)
            ?? throw new BizException("WM-DEVICE-TENANT-NOT-FOUND");
        _tenant.CurrentTenantId = tenant.Id;
        ClientDevice device;
        try
        {
            device = await _devices.GetQuickSwitchDeviceAsync(
                request.Client, tenant.Id, ct);
        }
        catch (InvalidOperationException ex)
        {
            throw new BizException(ex.Message);
        }

        var badge = request.BadgeNo.Trim();
        var originalPolicy = _db.Database.IsNpgsql() ? await Policies.ReadAsync(tenant.Id, ct) : null;
        var user = await _db.Sys_Users.IgnoreQueryFilters()
            .FirstOrDefaultAsync(x => x.TenantId == tenant.Id
                                      && x.BadgeNo == badge
                                      && x.Enable, ct);
        var valid = user is not null
                    && !string.IsNullOrWhiteSpace(user.QuickPinHash)
                    && _hasher.Verify(request.Pin, user.QuickPinHash);
        if (!valid)
        {
            await using var failureTransaction = _db.Database.IsNpgsql() ? await _db.Database.BeginTransactionAsync(ct) : null;
            if (failureTransaction != null)
            {
                await AuthenticationCompletionGateV1.AcquireAsync(_db, ct);
                await _db.Entry(device).ReloadAsync(ct);
            }
            device.QuickSwitchFailureCount++;
            if (device.QuickSwitchFailureCount >= 5)
            {
                device.FullAuthExpiresAt = null;
                device.CurrentUser = null;
            }
            await _db.SaveChangesAsync(ct);
            await _audit.LogAsync(
                SecurityEventType.LoginFailed, user?.Id, user?.UserName,
                tenant.TenantCode, ClientIp, ClientUa,
                ClientReason(request.Client, "quick-switch-failed"));
            if (failureTransaction != null) await failureTransaction.CommitAsync(ct);
            throw new BizException(device.QuickSwitchFailureCount >= 5
                ? "WM-DEVICE-FULL-AUTH-REQUIRED"
                : "WM-DEVICE-PIN-INVALID");
        }

        if (originalPolicy != null)
        {
            var proof = originalPolicy.VerifiedPrimary(user!, AuthenticationPrimaryMethod.QuickSwitchPin,
                request.Client.ClientKind, AuthenticationEntryServiceV1.Binding(request.Client), Guid.NewGuid().ToString("N"), DateTimeOffset.UtcNow);
            return await PgNativeResultAsync(() => Entries.BeginAsync(proof, AuthenticationRecoveryBindingV1.Challenge(Request),
                request.Client, ClientIp, ClientUa, null, ct, deferOutput: true));
        }

        device.QuickSwitchFailureCount = 0;
        device.CurrentUser = user!.UserName;
        device.LastSeenAt = DateTime.UtcNow;
        await _db.SaveChangesAsync(ct);
        return new NativeAuthResult
        {
            State = "authenticated",
            Session = await IssueSessionAsync(
                user, request.Client, "quick-switch", fullAuthentication: false)
        };
    }

    private async Task<(Sys_User user, string challenge, string purpose)> ReadChallengeAsync(
        NativeChallengeRequest request, string? expectedPurpose = null)
    {
        ValidateClient(request.Client);
        var pending = _pending.Get(request.ChallengeToken);
        if (pending is null || (expectedPurpose is not null
            && !string.Equals(pending.Value.purpose, expectedPurpose, StringComparison.Ordinal)))
            throw new BizException("E-SEC-013");
        var user = await _db.Sys_Users.IgnoreQueryFilters()
            .FirstOrDefaultAsync(x => x.Id == pending.Value.userId)
            ?? throw new BizException("E-SEC-013");
        _tenant.CurrentTenantId = user.TenantId;
        return (user, request.ChallengeToken, pending.Value.purpose);
    }

    private async Task<TokenSessionDto> IssueSessionAsync(
        Sys_User user,
        ClientContextDto client,
        string method,
        bool fullAuthentication = true)
    {
        _tenant.CurrentTenantId = user.TenantId;
        try
        {
            await _devices.EnsureLoginAllowedAsync(
                client, user.TenantId, HttpContext.RequestAborted);
        }
        catch (InvalidOperationException ex)
        {
            throw new BizException(ex.Message);
        }
        var mustChange = user.MustChangePassword || _passwordPolicy.IsExpired(user);
        var jti = Guid.NewGuid().ToString();
        var access = _sessions.BuildAccessToken(user, jti, mustChange, user.IsPlatformAdmin);
        var profile = await _sessions.BuildProfileAsync(user, mustChange);
        await _loginSecurity.RecordSuccessAsync(user, ClientIp);
        await _audit.LogAsync(
            SecurityEventType.LoginSuccess, user.Id, user.UserName, null,
            ClientIp, ClientUa, ClientReason(client, method));
        var refresh = await _refreshTokens.IssueAsync(
            user, ClientIp, ClientUa, ToRefreshContext(client));
        if (fullAuthentication)
            await _devices.MarkFullAuthenticationAsync(
                client, user.TenantId, user.UserName, HttpContext.RequestAborted);
        return new TokenSessionDto
        {
            AccessToken = access,
            AccessExpiresAt = _sessions.AccessExpiresAt,
            RefreshToken = refresh,
            RefreshExpiresAt = _sessions.RefreshExpiresAt,
            Profile = profile
        };
    }

    private string NativeSsoCallbackUri()
    {
        var root = string.IsNullOrWhiteSpace(_security.Sso.PublicBaseUrl)
            ? $"{Request.Scheme}://{Request.Host}"
            : _security.Sso.PublicBaseUrl;
        return root!.TrimEnd('/') + "/api/client-auth/sso/callback";
    }

    private static RefreshTokenClientContext ToRefreshContext(ClientContextDto client)
        => new(client.ClientKind, client.DeviceId, client.AppVersion);

    private static string ClientReason(ClientContextDto client, string? detail)
        => $"client={client.ClientKind};device={client.DeviceId};version={client.AppVersion};detail={detail}";

    private static void ValidateClient(ClientContextDto client)
    {
        try { ClientContextValidator.Validate(client); }
        catch (InvalidOperationException ex) { throw new BizException(ex.Message); }
    }
}
