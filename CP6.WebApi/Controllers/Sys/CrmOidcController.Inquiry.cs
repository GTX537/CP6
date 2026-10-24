using System.Data.Common;
using CP6.Core.Services.Sys;
using CP6.WebApi.Services;
using Microsoft.AspNetCore.Mvc;

namespace CP6.WebApi.Controllers.Sys;

public sealed partial class CrmOidcController
{
    [HttpPost("/connect/inquiry-permission")]
    [Consumes("application/json")]
    [RequestSizeLimit(4096)]
    public async Task<IActionResult> InquiryPermission(InquiryPermissionRequestV1 request)
    {
        PreventCaching();
        if (!options.Enabled || inquiryPermissions?.Enabled != true) return NotFound();
        if (!IsServiceTransportAllowed()) return OAuthError("invalid_request");
        try
        {
            var result = await inquiryPermissions.IssueAsync(request, async (db, _) =>
            {
                var session = await ReadBearerAsync(db);
                return session is null ? null : new InquiryPermissionSubjectV1(session.Value.User.TenantId,
                    session.Value.User.Id, session.Value.Grant.Id, session.Value.ExpiresAtUtc);
            }, HttpContext.RequestAborted);
            return Content(result, "application/json");
        }
        catch (InquiryPermissionExceptionV1 error) { return OAuthError(error.Message, error.Status); }
        catch (IamAdministrationExceptionV1) { return OAuthError("inquiry_authority_unavailable", 503); }
        catch (CurrentPermissionUnavailableException) { return OAuthError("inquiry_authority_unavailable", 503); }
        catch (DbException) { return OAuthError("inquiry_authority_unavailable", 503); }
    }

    [HttpPost("/connect/inquiry-service-permission")]
    [Consumes("application/json")]
    [RequestSizeLimit(4096)]
    public async Task<IActionResult> InquiryServicePermission(InquiryPermissionRequestV1 request)
    {
        PreventCaching();
        if (!options.Enabled || inquiryServicePermissions?.Enabled != true) return NotFound();
        if (!IsServiceTransportAllowed()) return OAuthError("invalid_request");
        try
        {
            var result = await inquiryServicePermissions.IssueAsync(request, Request.Headers.Authorization, HttpContext.RequestAborted);
            return Content(result, "application/json");
        }
        catch (InquiryPermissionExceptionV1 error) { return OAuthError(error.Message, error.Status); }
        catch (IamAdministrationExceptionV1) { return OAuthError("inquiry_authority_unavailable", 503); }
        catch (CurrentPermissionUnavailableException) { return OAuthError("inquiry_authority_unavailable", 503); }
        catch (DbException) { return OAuthError("inquiry_authority_unavailable", 503); }
    }
    [HttpPost("/connect/inquiry-service-context")]
    [Consumes("application/json")]
    [RequestSizeLimit(4096)]
    public async Task<IActionResult> InquiryServiceContext(InquiryServiceContextRequestV1 request)
    {
        PreventCaching();
        if (!options.Enabled || inquiryServicePermissions?.ContextEnabled != true) return NotFound();
        if (!IsServiceTransportAllowed()) return OAuthError("invalid_request");
        try
        {
            var result = await inquiryServicePermissions.IssueContextAsync(request, Request.Headers.Authorization, HttpContext.RequestAborted);
            return Content(result, "application/json");
        }
        catch (InquiryPermissionExceptionV1 error) { return OAuthError(error.Message, error.Status); }
        catch (IamAdministrationExceptionV1) { return OAuthError("inquiry_authority_unavailable", 503); }
        catch (CurrentPermissionUnavailableException) { return OAuthError("inquiry_authority_unavailable", 503); }
        catch (DbException) { return OAuthError("inquiry_authority_unavailable", 503); }
    }

}
