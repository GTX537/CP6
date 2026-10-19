using CP6.Core.Services.Sys;
using CP6.WebApi.Localization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;

namespace CP6.WebApi.Services;

/// <summary>Keep expected authentication decisions distinct from an unexpected application fault.</summary>
public sealed class AuthenticationEntryFailureAttribute : ExceptionFilterAttribute
{
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
