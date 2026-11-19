using CP6.Core.Services.Sys;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace CP6.WebApi.Controllers.Sys;

[ApiController, Authorize, Route("api/dashboard/scoped-v1/preferences")]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public sealed class WorkspacePreferencesControllerV1(WorkspacePreferencesV1 preferences) : ControllerBase
{
    [HttpGet]
    public Task<IActionResult> Current(CancellationToken ct) => Reply(() => preferences.ReadAsync(ct));

    [HttpGet("operations/{operationRef:guid}")]
    public Task<IActionResult> Original(Guid operationRef, CancellationToken ct) => Reply(() => preferences.ReadOriginalAsync(operationRef, ct));

    [HttpPost, RequestSizeLimit(8192)]
    public Task<IActionResult> Save(WorkspacePreferenceCommandV1 command, CancellationToken ct) => Reply(() => preferences.SaveAsync(command, ct));

    private async Task<IActionResult> Reply<T>(Func<Task<T>> action)
    {
        try
        {
            var result = await action();
            return result is null ? NotFound(new { code = "WORKSPACE_PREFERENCE_OPERATION_NOT_FOUND" }) : Ok(result);
        }
        catch (WorkspacePreferenceExceptionV1 error) { return StatusCode(error.Status, new { code = error.Message }); }
    }
}
