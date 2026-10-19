using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using CP6.Core.Services.Sys;
using CP6.Core.Utilities;
using CP6.Entity.DTOs.Client;
using CP6.WebApi.Services;
using CP6.WebApi.Localization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.IdentityModel.Tokens;
using Microsoft.AspNetCore.Authorization;

namespace CP6.WebApi.Controllers.Sys;

[AuthenticationEntryFailure]
public partial class AuthController
{
    private AuthenticationEntryServiceV1 Entries => HttpContext.RequestServices.GetRequiredService<AuthenticationEntryServiceV1>();
    private AuthenticationPolicyServiceV1 Policies => HttpContext.RequestServices.GetRequiredService<AuthenticationPolicyServiceV1>();

    [HttpPost("completion/status"), AllowAnonymous]
    public Task<IActionResult> CompletionStatus() => PgCompletionRecoveryAsync(false);

    [HttpPost("completion/close"), AllowAnonymous]
    public Task<IActionResult> CloseCompletion() => PgCompletionRecoveryAsync(true);

    [HttpPost("completion/browser/status"), AllowAnonymous]
    public Task<IActionResult> BrowserCompletionStatus() => BrowserRecoveryAsync(false);

    [HttpPost("completion/browser/close"), AllowAnonymous]
    public Task<IActionResult> BrowserCloseCompletion() => BrowserRecoveryAsync(true);

    private async Task<IActionResult> BrowserRecoveryAsync(bool close)
    {
        if (!Request.Headers.ContainsKey(AuthenticationRecoveryBindingV1.Header)) throw new InvalidOperationException("REAUTH_REQUIRED");
        var key = AuthenticationRecoveryBindingV1.Challenge(Request);
        return Ok(await new BrowserAuthenticationRecoveryV1(_context).ResolveAsync(key, close, HttpContext.RequestAborted));
    }

    private async Task<IActionResult> PgCompletionRecoveryAsync(bool close)
    {
        string credential, kind;
        if (Request.Headers.ContainsKey(AuthenticationRecoveryBindingV1.Header))
        { credential = AuthenticationRecoveryBindingV1.Challenge(Request); kind = "interaction"; }
        else if (Request.Cookies[AuthCookieWriter.RefreshCookie] is { Length: > 0 } refresh)
        { credential = refresh; kind = "refresh"; }
        else
        {
            var pending = Request.Cookies[AuthCookieWriter.PendingCookie];
            if (string.IsNullOrEmpty(pending)) throw AuthenticationEntryFailureAttribute.ToBusiness("REAUTH_REQUIRED");
            var cfg = _config.GetSection("JWT");
            try
            {
                var principal = new JwtSecurityTokenHandler().ValidateToken(pending, new TokenValidationParameters
                {
                    ValidateIssuer = true, ValidateAudience = true, ValidateLifetime = false, ValidateIssuerSigningKey = true,
                    ValidIssuer = cfg["Issuer"], ValidAudience = cfg["Audience"],
                    IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(cfg["Secret"]!))
                }, out _);
                if (principal.FindFirstValue("token_use") is not ("2fa_enroll" or "2fa_verify")) throw new InvalidOperationException();
                credential = principal.FindFirstValue("jti") ?? throw new InvalidOperationException(); kind = "interaction";
            }
            catch { throw AuthenticationEntryFailureAttribute.ToBusiness("REAUTH_REQUIRED"); }
        }
        var result = await new AuthenticationCompletionRecoveryV1(_context).ResolveAsync(new(kind, credential, "Web", null,
            kind == "interaction" ? AuthenticationEntryServiceV1.WebBinding(credential) : null), close, HttpContext.RequestAborted);
        return Ok(result);
    }

    private async Task<IActionResult> PgRefreshAsync(string raw)
    {
        TokenSessionDto? committed = null;
        try
        {
            var staged = new DefaultHttpContext();
            committed = await Entries.RefreshAsync(raw, null, ClientIp, ClientUa, result =>
            {
                if (Response.HasStarted) throw new InvalidOperationException("AUTH_RESPONSE_ALREADY_STARTED");
                _cookies.WriteAuthCookies(staged.Response, result.AccessToken, result.RefreshToken, AuthCookieWriter.NewCsrfToken());
                if (staged.Response.Headers.SetCookie.Count != 3) throw new InvalidOperationException("AUTHENTICATION_COOKIE_COMPLETION_INVALID");
            }, HttpContext.RequestAborted, deferOutput: true,
                browserAttempt: Request.Headers.ContainsKey(AuthenticationRecoveryBindingV1.Header) ? AuthenticationRecoveryBindingV1.Challenge(Request) : null);
            HttpContext.RequestAborted.ThrowIfCancellationRequested();
            Response.Headers.Append("Set-Cookie", staged.Response.Headers.SetCookie);
            AuthenticationEntryFailureAttribute.TrackOutput(HttpContext, committed.RefreshToken);
            return Ok(new { code = 0, userName = committed.Profile.UserName, mustChangePassword = false });
        }
        catch (RefreshCompletionUnknownException) { return StatusCode(503, new { code = "AUTHENTICATION_COMPLETION_OUTCOME_UNKNOWN" }); }
        catch (Exception error)
        {
            if (committed != null) await _refresh.RejectCompletionAsync(committed.RefreshToken);
            if (error is InvalidOperationException) throw AuthenticationEntryFailureAttribute.ToBusiness(error.Message);
            throw;
        }
    }

    private Task<IActionResult> PgWebBeginAsync(AuthenticationProofV1 proof, string challenge, string? landing = null)
        => PgWebOutputAsync(stage => Entries.BeginAsync(proof, challenge, null, ClientIp, ClientUa, stage, HttpContext.RequestAborted, deferOutput: true), landing);

    private async Task<IActionResult> PgWebFactorAsync(bool enroll, string? method, string code)
    {
        var challenge = await PgPendingAsync(enroll ? "2fa_enroll" : "2fa_verify");
        return await PgWebOutputAsync(stage => Entries.CompleteFactorAsync(challenge, null, enroll, method, code,
            ClientIp, ClientUa, stage, HttpContext.RequestAborted, deferOutput: true));
    }

    private async Task<IActionResult> PgWebOutputAsync(Func<Action<AuthenticationEntryResultV1>, Task<AuthenticationEntryResultV1>> action, string? landing = null)
    {
        AuthenticationEntryResultV1? committed = null;
        try
        {
            var staged = new DefaultHttpContext();
            committed = await action(result =>
            {
                HttpContext.RequestAborted.ThrowIfCancellationRequested();
                if (Response.HasStarted) throw new InvalidOperationException("AUTH_RESPONSE_ALREADY_STARTED");
                if (result.Session is { } session)
                {
                    _cookies.WriteAuthCookies(staged.Response, session.AccessToken, session.RefreshToken, AuthCookieWriter.NewCsrfToken());
                    _cookies.ClearPendingCookies(staged.Response);
                }
                else
                {
                    var jwt = _config.GetSection("JWT");
                    var token = JwtHelper.GeneratePendingToken(result.Proof.UserId.ToString(), result.Proof.TenantId, result.Purpose!, result.Challenge!,
                        jwt["Secret"]!, jwt["Issuer"]!, jwt["Audience"]!, _sec.TwoFactor.PendingTokenMinutes);
                    _cookies.ClearAuthCookies(staged.Response);
                    _cookies.WritePendingCookies(staged.Response, token, AuthCookieWriter.NewCsrfToken());
                }
            });
            HttpContext.RequestAborted.ThrowIfCancellationRequested();
            if (Response.HasStarted) throw new InvalidOperationException("AUTH_RESPONSE_ALREADY_STARTED");
            Response.Headers.Append("Set-Cookie", staged.Response.Headers.SetCookie);
            if (committed.Session != null) AuthenticationEntryFailureAttribute.TrackOutput(HttpContext, committed.Session.RefreshToken);
            if (landing != null) return Redirect(committed.Session == null ? landing + "?challenge=" + committed.Purpose : landing);
            return committed.Session == null ? Ok(new { twoFactorRequired = true, mustEnroll = committed.Purpose == "2fa_enroll" }) : Ok(committed.Session.Profile);
        }
        catch (RefreshCompletionUnknownException) { return StatusCode(503, new { code = "AUTHENTICATION_COMPLETION_OUTCOME_UNKNOWN" }); }
        catch (Exception error)
        {
            if (committed != null) await Entries.RejectOutputAsync(committed);
            if (error is InvalidOperationException)
            {
                if (landing != null) return Redirect(landing + "?error=" + Uri.EscapeDataString(error.Message));
                throw AuthenticationEntryFailureAttribute.ToBusiness(error.Message);
            }
            throw;
        }
    }

    private async Task<string> PgPendingAsync(string purpose)
    {
        try
        {
            var raw = Request.Cookies[AuthCookieWriter.PendingCookie];
            if (string.IsNullOrEmpty(raw)) throw new InvalidOperationException();
            var cfg = _config.GetSection("JWT");
            var principal = new JwtSecurityTokenHandler().ValidateToken(raw, new TokenValidationParameters
            {
                ValidateIssuer = true, ValidateAudience = true, ValidateLifetime = true, ValidateIssuerSigningKey = true,
                ValidIssuer = cfg["Issuer"], ValidAudience = cfg["Audience"], ClockSkew = TimeSpan.Zero,
                IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(cfg["Secret"]!))
            }, out _);
            var challenge = principal.FindFirstValue("jti") ?? throw new InvalidOperationException();
            if (Request.Headers.ContainsKey(AuthenticationRecoveryBindingV1.Header)
                && AuthenticationRecoveryBindingV1.Challenge(Request) != challenge) throw new InvalidOperationException();
            var pending = await Entries.ReadAsync(challenge, null, purpose, HttpContext.RequestAborted);
            if (principal.FindFirstValue(ClaimTypes.NameIdentifier) != pending.Proof.UserId.ToString()
                || principal.FindFirstValue("tenant_id") != pending.Proof.TenantId.ToString()
                || principal.FindFirstValue("token_use") != pending.Purpose) throw new InvalidOperationException();
            return challenge;
        }
        catch (OperationCanceledException) { throw; }
        catch { throw new BizException("E-SEC-013"); }
    }
}
