using CP6.Core.Auth;
using CP6.Core.Services.Wf;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace CP6.WebApi.Controllers.Oa;

/// <summary>审批人映射维护(②b Menu 数据驱动)。/api/oa/approver-map。</summary>
[ApiController]
[Route("api/oa/approver-map")]
[Authorize]
public class ApproverMapController : LocalizedControllerBase
{
    private readonly IApproverMapService _svc;
    public ApproverMapController(IApproverMapService svc) => _svc = svc;

    private IActionResult Ok2(object? data = null) => Ok(new { code = 0, message = "OK", data });
    private IActionResult Err(InvalidOperationException e) => BadRequest(new { code = 400, message = e.Message });

    public record CreateReq(string MapKey, string MatchValue, Guid? ApproverUserId, int? ApproverRoleId, int OrderNo);
    public record UpdateReq(string MatchValue, Guid? ApproverUserId, int? ApproverRoleId, int OrderNo, bool Enable);

    [HttpGet("list")]
    public async Task<IActionResult> List([FromQuery] string? mapKey) => Ok2(await _svc.ListAsync(mapKey));

    [HttpGet("keys")]
    public async Task<IActionResult> Keys() => Ok2(await _svc.DistinctKeysAsync());

    [HttpPost]
    [RequirePermission("oa-approver-map", "add")]
    public async Task<IActionResult> Create([FromBody] CreateReq r)
    {
        try { return Ok2(await _svc.CreateAsync(r.MapKey, r.MatchValue, r.ApproverUserId, r.ApproverRoleId, r.OrderNo)); }
        catch (InvalidOperationException e) { return Err(e); }
    }

    [HttpPut("{id}")]
    [RequirePermission("oa-approver-map", "edit")]
    public async Task<IActionResult> Update(Guid id, [FromBody] UpdateReq r)
    {
        try { await _svc.UpdateAsync(id, r.MatchValue, r.ApproverUserId, r.ApproverRoleId, r.OrderNo, r.Enable); return Ok2(); }
        catch (InvalidOperationException e) { return Err(e); }
    }

    [HttpDelete("{id}")]
    [RequirePermission("oa-approver-map", "del")]
    public async Task<IActionResult> Delete(Guid id) { await _svc.DeleteAsync(id); return Ok2(); }
}
