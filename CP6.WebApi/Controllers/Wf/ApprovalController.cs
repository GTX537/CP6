using System.Text.Json;
using CP6.Core.Services.Sys;
using CP6.Core.Services.Wf;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace CP6.WebApi.Controllers.Wf;

/// <summary>
/// 审批集成 REST（OA 章05 阶段2）。/api/wf/approval —— 业务模块接入 OA 的入口：
/// 起业务审批（按绑定选流程）+ 查业务单据审批状态。发起人取登录用户（不接受客户端传 id）。
/// </summary>
[ApiController]
[Route("api/wf/approval")]
[Authorize]
public class ApprovalController : LocalizedControllerBase
{
    private readonly IApprovalService _approval;
    private readonly ICurrentPermissionContext _ctx;

    public ApprovalController(IApprovalService approval, ICurrentPermissionContext ctx)
    {
        _approval = approval;
        _ctx = ctx;
    }

    private IActionResult Ok2(object? data = null) => Ok(new { code = 0, message = "OK", data });
    private IActionResult Err(InvalidOperationException e) => BadRequest(new { code = 400, message = e.Message });

    /// <summary>起业务审批（按 bizType 绑定的流程）。返回流程实例 Id。</summary>
    [HttpPost("submit")]
    public async Task<IActionResult> Submit([FromBody] ApprovalSubmitReq r)
    {
        try
        {
            var starter = (await _ctx.GetAsync()).UserId;
            // formSnapshot 透传为对象（已是 JsonElement / 由业务侧序列化）
            object? snapshot = r.FormSnapshot.ValueKind == JsonValueKind.Undefined ? null : r.FormSnapshot;
            var id = await _approval.SubmitAsync(r.BizType, r.BizId, starter, snapshot);
            return Ok2(new { instanceId = id });
        }
        catch (InvalidOperationException e) { return Err(e); }
    }

    /// <summary>查业务单据的审批状态。</summary>
    [HttpGet("status")]
    public async Task<IActionResult> Status([FromQuery] string bizType, [FromQuery] string bizId)
    {
        var status = await _approval.GetStatusAsync(bizType, bizId);
        return Ok2(new { bizType, bizId, status = (int)status, statusName = status.ToString() });
    }
}

/// <summary>起审请求。FormSnapshot 为业务表单字段快照（OA 落 VarsJson，不回查业务表）。</summary>
public class ApprovalSubmitReq
{
    public string BizType { get; set; } = string.Empty;
    public string BizId { get; set; } = string.Empty;
    public JsonElement FormSnapshot { get; set; }
}
