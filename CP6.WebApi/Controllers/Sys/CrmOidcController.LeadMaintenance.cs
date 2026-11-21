using System.Data.Common;
using System.Text.Json;
using CP6.Core.Services.CrmIdentity;
using CP6.Core.Services.Platform;
using CP6.Core.Services.Sys;
using CP6.WebApi.Services;
using Microsoft.AspNetCore.Mvc;

namespace CP6.WebApi.Controllers.Sys;

public sealed partial class CrmOidcController
{
    [HttpPost("/connect/lead-maintenance-context-v1")]
    [Consumes("application/json")]
    [RequestSizeLimit(4096)]
    public async Task<IActionResult> LeadMaintenanceContext(JsonElement request)
    {
        PreventCaching();
        if (!options.Enabled || leadMaintenance?.Enabled != true) return NotFound();
        if (!IsServiceTransportAllowed()) return OAuthError("invalid_request");
        LeadMaintenanceRequestV1 command;
        try { command = LeadMaintenanceContractV1.Read<LeadMaintenanceRequestV1>(request); }
        catch (JsonException) { return OAuthError("invalid_request"); }
        try
        {
            return Content(await leadMaintenance.IssueAsync(command, Request.Headers.Authorization, HttpContext.RequestAborted),
                "application/json");
        }
        catch (InquiryPermissionExceptionV1 error) { return OAuthError(error.Message, error.Status); }
        catch (Exception error) when (error is IamAdministrationExceptionV1 or CurrentPermissionUnavailableException
            or TenantManagementExceptionV1 or IdentityReadException or DbException or JsonException)
        {
            return OAuthError("lead_maintenance_authority_unavailable", 503);
        }
    }
}
