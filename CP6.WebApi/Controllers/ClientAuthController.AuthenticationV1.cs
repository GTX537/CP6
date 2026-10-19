using CP6.Core.Services.Sys;
using CP6.Entity.DTOs.Client;
using CP6.WebApi.Services;
using CP6.WebApi.Localization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Authorization;

namespace CP6.WebApi.Controllers;

[AuthenticationEntryFailure]
public sealed partial class ClientAuthController
{
    private AuthenticationEntryServiceV1 Entries => HttpContext.RequestServices.GetRequiredService<AuthenticationEntryServiceV1>();
    private AuthenticationPolicyServiceV1 Policies => HttpContext.RequestServices.GetRequiredService<AuthenticationPolicyServiceV1>();

    [HttpPost("completion/status"), AllowAnonymous]
    public Task<ActionResult<AuthenticationRecoveryStatusV1>> CompletionStatus(NativeCompletionRecoveryRequest request)
        => PgCompletionRecoveryAsync(request, false);

    [HttpPost("completion/close"), AllowAnonymous]
    public Task<ActionResult<AuthenticationRecoveryStatusV1>> CloseCompletion(NativeCompletionRecoveryRequest request)
        => PgCompletionRecoveryAsync(request, true);

    private async Task<ActionResult<AuthenticationRecoveryStatusV1>> PgCompletionRecoveryAsync(NativeCompletionRecoveryRequest request, bool close)
    {
        ValidateClient(request.Client);
        var kind = request.Kind;
        var credential = request.Credential;
        if (kind == "native-grant")
        {
            if (credential == null || credential.Length is < 32 or > 128 || request.CodeVerifier == null || request.CodeVerifier.Length is < 43 or > 128)
                throw AuthenticationEntryFailureAttribute.ToBusiness("REAUTH_REQUIRED");
            credential = AuthenticationRecoveryBindingV1.NativeGrant(credential, request.CodeVerifier); kind = "interaction";
        }
        return await new AuthenticationCompletionRecoveryV1(_db).ResolveAsync(new(kind, credential, request.Client.ClientKind,
            request.Client.DeviceId, AuthenticationEntryServiceV1.Binding(request.Client)), close, HttpContext.RequestAborted);
    }

    private async Task<ActionResult<TokenSessionDto>> PgNativeRefreshAsync(NativeRefreshRequest request)
    {
        TokenSessionDto? committed = null;
        try
        {
            committed = await Entries.RefreshAsync(request.RefreshToken, request.Client, ClientIp, ClientUa,
                result => { if (Response.HasStarted) throw new InvalidOperationException("AUTH_RESPONSE_ALREADY_STARTED"); }, HttpContext.RequestAborted, deferOutput: true);
            HttpContext.RequestAborted.ThrowIfCancellationRequested();
            AuthenticationEntryFailureAttribute.TrackOutput(HttpContext, committed.RefreshToken);
            return committed;
        }
        catch (RefreshCompletionUnknownException) { return StatusCode(503, new { code = "AUTHENTICATION_COMPLETION_OUTCOME_UNKNOWN" }); }
        catch (Exception error)
        {
            if (committed != null) await _refreshTokens.RejectCompletionAsync(committed.RefreshToken);
            if (error is InvalidOperationException) throw AuthenticationEntryFailureAttribute.ToBusiness(error.Message);
            throw;
        }
    }

    private async Task<ActionResult<NativeAuthResult>> PgNativeResultAsync(Func<Task<AuthenticationEntryResultV1>> action)
    {
        AuthenticationEntryResultV1? committed = null;
        try
        {
            committed = await action();
            HttpContext.RequestAborted.ThrowIfCancellationRequested();
            if (Response.HasStarted) throw new InvalidOperationException("AUTH_RESPONSE_ALREADY_STARTED");
            if (committed.Session != null) AuthenticationEntryFailureAttribute.TrackOutput(HttpContext, committed.Session.RefreshToken);
            return new NativeAuthResult { State = committed.Session != null ? "authenticated"
                : committed.Purpose == "2fa_enroll" ? "enrollmentRequired" : "twoFactorRequired",
                ChallengeToken = committed.Challenge, Session = committed.Session };
        }
        catch (RefreshCompletionUnknownException) { return StatusCode(503, new { code = "AUTHENTICATION_COMPLETION_OUTCOME_UNKNOWN" }); }
        catch (Exception error)
        {
            if (committed != null) await Entries.RejectOutputAsync(committed);
            if (error is InvalidOperationException) throw AuthenticationEntryFailureAttribute.ToBusiness(error.Message);
            throw;
        }
    }
}
