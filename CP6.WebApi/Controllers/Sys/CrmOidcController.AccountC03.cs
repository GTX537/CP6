using System.Data.Common;
using System.Text.Json;
using CP6.Core.Services.Sys;
using CP6.Core.Services.CrmIdentity;
using CP6.WebApi.Services;
using Microsoft.AspNetCore.Mvc;

namespace CP6.WebApi.Controllers.Sys;

public sealed partial class CrmOidcController
{
    [HttpPost("/connect/account-c03-service-identity")]
    [Consumes("application/json")]
    [RequestSizeLimit(4096)]
    public async Task<IActionResult> AccountC03ServiceIdentity([FromBody] JsonElement input)
    {
        PreventCaching();
        var issuer = HttpContext.RequestServices.GetService<AccountC03ServiceIdentityIssuer>();
        if (!options.Enabled || issuer is null) return NotFound();
        if (!IsServiceTransportAllowed() || Request.Query.Count != 0 || input.ValueKind != JsonValueKind.Object ||
            input.EnumerateObject().Count() != 2 || !input.TryGetProperty("purpose", out var purpose) ||
            purpose.ValueKind != JsonValueKind.String || purpose.GetString() != AccountC03ServiceIdentityIssuer.Purpose ||
            !input.TryGetProperty("requestNonce", out var nonce) || nonce.ValueKind != JsonValueKind.String)
            return OAuthError("invalid_request");
        try
        {
            var credential = await issuer.IssueAsync(nonce.GetString()!, Request.Headers.Authorization, HttpContext.RequestAborted);
            return Ok(new { identityToken = credential });
        }
        catch (InquiryPermissionExceptionV1 error) { return OAuthError(error.Message, error.Status); }
        catch (IdentityReadException) { return OAuthError("account_c03_identity_unavailable", 503); }
        catch (IamAdministrationExceptionV1) { return OAuthError("account_c03_identity_unavailable", 503); }
        catch (DbException) { return OAuthError("account_c03_identity_unavailable", 503); }
    }
}
