using CP6.Core.Services.Sys;
using CP6.WebApi.Localization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;

namespace CP6.WebApi.Services;

/// <summary>Keep expected authentication decisions distinct from an unexpected application fault.</summary>
public sealed class AuthenticationEntryFailureAttribute : ExceptionFilterAttribute, IAsyncResultFilter
{
    private static readonly object OutputKey = new();
    public static void TrackOutput(HttpContext context, string refresh) => context.Items[OutputKey] = refresh;

    public async Task OnResultExecutionAsync(ResultExecutingContext context, ResultExecutionDelegate next)
    {
        var http = context.HttpContext;
        if (!http.Items.TryGetValue(OutputKey, out var value) || value is not string raw) { await next(); return; }
        var output = http.Response.Body;
        await using var buffer = new MemoryStream();
        http.Response.Body = buffer;
        try
        {
            var executed = await next();
            if (executed.Exception != null || executed.Canceled || http.Response.StatusCode >= 400)
            {
                await RejectOutput(http);
                if (!http.Response.HasStarted) { http.Response.Headers.Remove("Set-Cookie"); http.Response.ContentLength = 0; }
                return;
            }
            // Actual formatter/JSON/cookie preparation finishes while the durable session is unusable.
            // No credential bytes are copied to the transport until the Owner confirms current completion.
            await http.RequestServices.GetRequiredService<IRefreshTokenService>().CompleteOutputAsync(raw);
            http.RequestAborted.ThrowIfCancellationRequested();
            http.Response.Body = output; buffer.Position = 0;
            await buffer.CopyToAsync(output, http.RequestAborted);
        }
        catch (Exception error)
        {
            if (!http.Response.HasStarted) { http.Response.Headers.Remove("Set-Cookie"); http.Response.ContentLength = null; }
            try { await RejectOutput(http); }
            catch (RefreshCompletionUnknownException unknown) { throw ToBusiness(unknown.Message); }
            if (error is InvalidOperationException or RefreshCompletionUnknownException) throw ToBusiness(error.Message);
            throw;
        }
        finally { http.Response.Body = output; }
    }

    private static async Task RejectOutput(HttpContext context)
    {
        if (context.Items.Remove(OutputKey, out var value) && value is string raw)
            await context.RequestServices.GetRequiredService<IRefreshTokenService>().RejectCompletionAsync(raw);
    }

    public static int Status(string code) => code switch
    {
        "REAUTH_REQUIRED" or "AUTH_PRIMARY_PROOF_REQUIRED" => 401,
        "AUTHENTICATION_DENIED" or "SSO_REQUIRED" or "ACCOUNT_REMEDIATION_REQUIRED" => 403,
        "AUTH_POLICY_UNAVAILABLE" or "AUTH_SESSION_AUTHORITY_UNAVAILABLE" or "SSO_IDENTITY_POLICY_UNAVAILABLE"
            or "AUTH_RESTRICTED_SESSION_UNAVAILABLE" or "AUTHENTICATION_COMPLETION_OUTCOME_UNKNOWN" => 503,
        _ => 400
    };

    public static BizException ToBusiness(string code) => new(code, Status(code));

    public override void OnException(ExceptionContext context)
    {
        var error = context.Exception;
        if (error is not (InvalidOperationException or RefreshCompletionUnknownException)) return;
        var code = error.Message;
        if (Status(code) == 400 && !code.StartsWith("E-SEC-", StringComparison.Ordinal)) return;
        context.Result = new ObjectResult(new { code, message = code }) { StatusCode = Status(code) };
        context.ExceptionHandled = true;
    }
}
