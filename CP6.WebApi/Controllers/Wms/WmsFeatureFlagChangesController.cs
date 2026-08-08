using CP6.Core.Auth;
using CP6.Core.Services.Sys;
using CP6.Core.Services.Wms;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace CP6.WebApi.Controllers.Wms;

[ApiController]
[Route("api/v2/admin/wms-feature-changes")]
[Authorize]
public sealed class WmsFeatureFlagChangesController : ControllerBase
{
    private readonly IWmsFeatureFlagChangeService _service;
    private readonly ICurrentPermissionContext _permissions;

    public WmsFeatureFlagChangesController(
        IWmsFeatureFlagChangeService service,
        ICurrentPermissionContext permissions)
    {
        _service = service;
        _permissions = permissions;
    }

    [HttpPost]
    [RequirePermission("wms-mobile", "device-manage")]
    public async Task<ActionResult<WmsFeatureFlagChangeDto>> Submit(
        CreateWmsFeatureFlagChangeRequest request,
        CancellationToken ct)
    {
        try
        {
            var actor = await _permissions.GetAsync();
            var result = await _service.SubmitAsync(
                request,
                actor.UserId,
                actor.UserName ?? User.Identity?.Name,
                ct);
            return Accepted(new
            {
                changeId = result.Id,
                approvalInstanceId = result.FlowInstanceId,
                status = result.Status,
                change = result,
            });
        }
        catch (WmsFeatureFlagChangeException ex)
        {
            return Problem(ex);
        }
        catch (DbUpdateException)
        {
            return Conflict(Error("WM-FEATURE-CHANGE-ACTIVE"));
        }
    }

    [HttpGet]
    [RequirePermission("wms-mobile", "device-manage")]
    public async Task<ActionResult<IReadOnlyList<WmsFeatureFlagChangeDto>>> Get(
        [FromQuery] WmsFeatureFlagChangeQuery query,
        CancellationToken ct)
        => Ok(await _service.GetAsync(query, ct));

    [HttpPost("{id:guid}/cancel")]
    [RequirePermission("wms-mobile", "device-manage")]
    public async Task<ActionResult> Cancel(Guid id, CancellationToken ct)
    {
        try
        {
            var actor = await _permissions.GetAsync();
            await _service.CancelAsync(
                id,
                actor.UserId,
                actor.UserName ?? User.Identity?.Name,
                ct);
            return NoContent();
        }
        catch (WmsFeatureFlagChangeException ex)
        {
            return Problem(ex);
        }
    }

    private ActionResult Problem(WmsFeatureFlagChangeException ex)
    {
        var payload = Error(ex.Code);
        return ex.Code switch
        {
            "WM-FEATURE-CHANGE-NOT-FOUND" => NotFound(payload),
            "WM-FEATURE-CHANGE-ACTIVE"
                or "WM-FEATURE-CHANGE-STALE"
                or "WM-FEATURE-CHANGE-NOT-PENDING" => Conflict(payload),
            "WM-FEATURE-CHANGE-CANCEL-FORBIDDEN" => StatusCode(
                StatusCodes.Status403Forbidden, payload),
            _ => BadRequest(payload),
        };
    }

    private static object Error(string code) => new { code, message = code };
}
