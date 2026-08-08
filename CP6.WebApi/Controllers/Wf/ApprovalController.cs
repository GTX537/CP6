using System.Text.Json;
using CP6.Core.Auth;
using CP6.Core.Services.Sys;
using CP6.Core.Services.Wf;
using CP6.Core.Services.Oa;
using CP6.Core.EFDbContext;
using Microsoft.EntityFrameworkCore;
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
    private readonly CP6Context _db;
    private readonly IOaInstanceAccessService _access;
    private readonly IDelegateService _delegates;

    public ApprovalController(IApprovalService approval, ICurrentPermissionContext ctx,
        CP6Context db, IOaInstanceAccessService access, IDelegateService delegates)
    {
        _approval = approval;
        _ctx = ctx;
        _db = db;
        _access = access;
        _delegates = delegates;
    }

    private IActionResult Ok2(object? data = null) => Ok(new { code = 0, message = "OK", data });
    private IActionResult Err(InvalidOperationException e) => BadRequest(new { code = 400, message = e.Message });

    /// <summary>起业务审批（按 bizType 绑定的流程）。返回流程实例 Id。</summary>
    [HttpPost("submit")]
    [RequirePermission("oa-form-catalog", "submit")]
    public async Task<IActionResult> Submit([FromBody] ApprovalSubmitReq r)
    {
        await Task.CompletedTask;
        return StatusCode(StatusCodes.Status410Gone,
            new { code = 410, message = "Business modules must submit through their trusted backend endpoint." });
    }

    /// <summary>查业务单据的审批状态。</summary>
    [HttpGet("status")]
    public async Task<IActionResult> Status([FromQuery] string bizType, [FromQuery] string bizId)
    {
        var instanceId = await _db.Wf_FlowInstances.AsNoTracking()
            .Where(x => x.BizType == bizType && x.BizId == bizId)
            .OrderByDescending(x => x.CreateDate).Select(x => (Guid?)x.Id).FirstOrDefaultAsync();
        if (instanceId == null) return NotFound(new { code = 404, message = "E-WF-007" });
        var actual = (await _ctx.GetAsync()).UserId;
        var effective = actual;
        var header = Request.Headers["X-Acting-As"].ToString();
        if (Guid.TryParse(header, out var actingAs) && actingAs != Guid.Empty && actingAs != actual)
        {
            await _delegates.AssertActiveGrantAsync(actual, actingAs);
            effective = actingAs;
        }
        try { await _access.GetAsync(actual, effective, instanceId.Value); }
        catch (UnauthorizedAccessException)
        {
            return StatusCode(StatusCodes.Status403Forbidden, new { code = "E-WF-043", message = "E-WF-043" });
        }
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
