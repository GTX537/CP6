using CP6.Core.Services.Oa;
using CP6.Core.Services.Sys;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace CP6.WebApi.Controllers.Oa;

[ApiController]
[Route("api/oa/approval")]
[Authorize]
public sealed class ApprovalController : ControllerBase
{
    private readonly IApprovalPanelService _panels;
    private readonly ICurrentPermissionContext _permission;
    private readonly IDelegateService _delegates;

    public ApprovalController(
        IApprovalPanelService panels, ICurrentPermissionContext permission, IDelegateService delegates)
    {
        _panels = panels;
        _permission = permission;
        _delegates = delegates;
    }

    [HttpGet("detail")]
    public async Task<IActionResult> Detail(
        [FromQuery] string bizType, [FromQuery] string bizId, CancellationToken ct)
    {
        try
        {
            var permission = await _permission.GetAsync();
            var effective = permission.UserId;
            var header = Request.Headers["X-Acting-As"].ToString();
            if (Guid.TryParse(header, out var actingAs) &&
                actingAs != Guid.Empty && actingAs != permission.UserId)
            {
                await _delegates.AssertActiveGrantAsync(permission.UserId, actingAs);
                effective = actingAs;
            }
            var dto = await _panels.GetAsync(
                bizType, bizId, permission.UserId, effective, permission, ct);
            return Ok(new { code = 0, message = "OK", data = dto });
        }
        catch (UnauthorizedAccessException ex)
        {
            return StatusCode(StatusCodes.Status403Forbidden,
                new { code = ex.Message, message = ex.Message });
        }
    }
}
