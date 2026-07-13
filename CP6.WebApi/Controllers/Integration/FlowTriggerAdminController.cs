using CP6.Core.Auth;
using CP6.Core.Services.Wf;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace CP6.WebApi.Controllers.Integration;

/// <summary>流程触发器管理（spec §4，流程管理页「触发器」tab 后端）。
/// 权限点（spec §6 OA.FlowTrigger.* → 映射表②）：View=查，Edit=增改/启停/试发/重置 key。
///
/// 命名空间放 Controllers.Integration（非 spec 落点 Controllers.Oa）：OawfPermissionAttributeTests 锁死
/// Oa∪Wf==16 控制器 / 贴点==31 / action 词表，而本控制器权限点 action="FlowTrigger.View"/"FlowTrigger.Edit"
/// 尚未纳入该词表（ActionCode/RoleAction seed 是 F-T2 的职责）。循 C-T2 WfTriggerEchoController /
/// D-T2 FlowTriggerFireController 先例移出该守卫扫描面，路由保持 spec 原文 api/oa/flow-triggers。</summary>
[ApiController]
[Route("api/oa/flow-triggers")]
[Authorize]
public class FlowTriggerAdminController : LocalizedControllerBase
{
    private readonly IFlowTriggerAdminService _admin;

    public FlowTriggerAdminController(IFlowTriggerAdminService admin) { _admin = admin; }

    private IActionResult Ok2(object? data = null) => Ok(new { code = 0, message = "OK", data });
    private IActionResult Err(InvalidOperationException e) => BadRequest(new { code = 400, message = e.Message });

    [HttpGet("list")]
    [RequirePermission("oa-flow-admin", "FlowTrigger.View")]
    public async Task<IActionResult> List(CancellationToken ct) => Ok2(await _admin.ListAsync(ct));

    [HttpGet("{id:guid}")]
    [RequirePermission("oa-flow-admin", "FlowTrigger.View")]
    public async Task<IActionResult> Get(Guid id, CancellationToken ct)
    {
        var item = await _admin.GetAsync(id, ct);
        return item is null ? NotFound(new { code = 404, message = "E-WF-022" }) : Ok2(item);
    }

    [HttpPost]
    [RequirePermission("oa-flow-admin", "FlowTrigger.Edit")]
    public async Task<IActionResult> Create([FromBody] FlowTriggerSaveReq req, CancellationToken ct)
    {
        try
        {
            var (id, apiKeyPlain) = await _admin.CreateAsync(req, ct);
            return Ok2(new { id, apiKeyPlain });   // 明文只此一次（spec §3.4）
        }
        catch (InvalidOperationException e) { return Err(e); }
    }

    [HttpPut("{id:guid}")]
    [RequirePermission("oa-flow-admin", "FlowTrigger.Edit")]
    public async Task<IActionResult> Update(Guid id, [FromBody] FlowTriggerSaveReq req, CancellationToken ct)
    {
        try { await _admin.UpdateAsync(id, req, ct); return Ok2(); }
        catch (InvalidOperationException e) { return Err(e); }
    }

    [HttpPost("{id:guid}/enable")]
    [RequirePermission("oa-flow-admin", "FlowTrigger.Edit")]
    public async Task<IActionResult> Enable(Guid id, [FromBody] EnableReq r, CancellationToken ct)
    {
        try { await _admin.SetEnabledAsync(id, r.Enabled, ct); return Ok2(); }
        catch (InvalidOperationException e) { return Err(e); }
    }

    [HttpPost("{id:guid}/reset-key")]
    [RequirePermission("oa-flow-admin", "FlowTrigger.Edit")]
    public async Task<IActionResult> ResetKey(Guid id, CancellationToken ct)
    {
        try { return Ok2(new { apiKeyPlain = await _admin.ResetKeyAsync(id, ct) }); }
        catch (InvalidOperationException e) { return Err(e); }
    }

    [HttpPost("{id:guid}/manual-fire")]
    [RequirePermission("oa-flow-admin", "FlowTrigger.Edit")]   // 手动试发归 Edit（spec §6）
    public async Task<IActionResult> ManualFire(Guid id, CancellationToken ct)
    {
        try
        {
            var r = await _admin.ManualFireAsync(id, ct);
            return r.Success
                ? Ok2(new { r.InstanceId })
                : BadRequest(new { code = 400, message = r.Error });
        }
        catch (InvalidOperationException e) { return Err(e); }
    }

    [HttpGet("{id:guid}/fires")]
    [RequirePermission("oa-flow-admin", "FlowTrigger.View")]
    public async Task<IActionResult> Fires(Guid id, [FromQuery] int take, CancellationToken ct)
        => Ok2(await _admin.ListFiresAsync(id, take <= 0 ? 20 : take, ct));

    [HttpPost("cron-preview")]
    [RequirePermission("oa-flow-admin", "FlowTrigger.View")]
    public IActionResult CronPreview([FromBody] CronPreviewReq r)
        => WfCronHelper.IsValid(r.Cron)
            ? Ok2(new { next = WfCronHelper.PreviewUtc(r.Cron, DateTime.UtcNow, 5) })
            : BadRequest(new { code = 400, message = "E-WF-022" });

    public record EnableReq(bool Enabled);
    public record CronPreviewReq(string Cron);
}
