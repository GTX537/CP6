using CP6.Core.Auth;
using CP6.Core.Services.Wf;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace CP6.WebApi.Controllers.Oa;

/// <summary>流程触发器管理（spec §4，流程管理页「触发器」tab 后端）。
/// 权限点（spec §6 OA.FlowTrigger.* → 映射表②）：Edit=增改/启停/试发/重置 key；View=cron 预览。
///
/// 命名空间 Controllers.Oa（spec 原落点）：F-T2 已把 FlowTrigger.View/Edit 纳入
/// OawfPermissionAttributeTests 的 ActionVocabulary 并逐租户播种（FlowTriggerPermissionSeed），
/// E-T1 交接票要求把本管理面收编回 fail-closed 守卫扫描面（计数 16→17 / 贴点 31→37）。
/// 变更端点（Create/Update/Enable/ResetKey/ManualFire=Edit；CronPreview=View）均贴 [RequirePermission]。
/// 只读 GET（list/{id}/{id}/fires）循同菜单 oa-flow-admin 的 <see cref="FlowAdminController"/> 既有约定
/// 仅 [Authorize] 不贴细粒度键（守卫 NoReadOnlyGetAction 禁 GET 贴键）；读授权=登录态+租户隔离，
/// 与流程管理页兄弟控制器一致。路由保持 spec 原文 api/oa/flow-triggers。
/// 匿名/[Authorize]-only 的 WfTriggerEchoController(C-T2)/FlowTriggerFireController(D-T2) 仍留 Integration。</summary>
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
    public async Task<IActionResult> List(CancellationToken ct) => Ok2(await _admin.ListAsync(ct));

    [HttpGet("{id:guid}")]
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
