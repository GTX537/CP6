using CP6.Core.Services.Sys;
using CP6.Entity.DTOs.Client;
using CP6.WebApi.Services;
using CP6.WebApi.Localization;
using Microsoft.AspNetCore.Mvc;

namespace CP6.WebApi.Controllers;

[AuthenticationEntryFailure]
public sealed partial class ClientAuthController
{
    private AuthenticationEntryServiceV1 Entries => HttpContext.RequestServices.GetRequiredService<AuthenticationEntryServiceV1>();
    private AuthenticationPolicyServiceV1 Policies => HttpContext.RequestServices.GetRequiredService<AuthenticationPolicyServiceV1>();

    private async Task<ActionResult<NativeAuthResult>> PgNativeResultAsync(Func<Task<AuthenticationEntryResultV1>> action)
    {
        AuthenticationEntryResultV1? committed = null;
        try
        {
            committed = await action();
            HttpContext.RequestAborted.ThrowIfCancellationRequested();
            if (Response.HasStarted) throw new InvalidOperationException("AUTH_RESPONSE_ALREADY_STARTED");
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
