using System.Text;
using System.Text.Json;
using CP6.Core.Auth;
using CP6.Core.Services.Wf;
using CP6.Entity.DomainModels.Wf;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace CP6.WebApi.Controllers.Integration;

/// <summary>message 触发器外呼端点（spec §3.4）。
/// 命名空间放 Controllers.Integration（非 spec 落点 Controllers.Oa）：OawfPermissionAttributeTests 锁死
/// Oa∪Wf==16 控制器且要求变更端点贴 [RequirePermission]，本端点 [AllowAnonymous]+key 闸不贴权限键，
/// 循 C-T2 WfTriggerEchoController 先例移出该守卫扫描面；路由保持 spec 原文 api/oa/flow-triggers。
/// 响应：201 新发起 {instanceId} / 200 幂等重放 {instanceId} / 400 缺幂等头·负载超限·非对象 /
/// 401 key 无效 / 404 不存在或未启用（不区分）/ 500 运行时发起失败（E-WF-022/023/024 detail）。</summary>
[ApiController]
[Route("api/oa/flow-triggers")]
public class FlowTriggerFireController : ControllerBase
{
    public const int MaxPayloadBytes = 64 * 1024;   // 64KB 上限防滥用（spec §6）

    private readonly IFlowTriggerService _triggers;

    public FlowTriggerFireController(IFlowTriggerService triggers) { _triggers = triggers; }

    [HttpPost("{id:guid}/fire")]
    [AllowAnonymous]
    [WfTriggerApiKey]
    public async Task<IActionResult> Fire(Guid id, CancellationToken ct)
    {
        var trigger = (Wf_FlowTrigger)HttpContext.Items[WfTriggerApiKeyAttribute.ItemKey]!;
        var idemKey = Request.Headers["Idempotency-Key"].First()!;

        // 64KB：Content-Length 先验 + 实读字节数兜底（chunked 无 Content-Length 时）
        if (Request.ContentLength is > MaxPayloadBytes)
            return BadRequest(new { code = 400, message = "payload too large (64KB max)" });
        string body;
        using (var reader = new StreamReader(Request.Body, Encoding.UTF8))
            body = await reader.ReadToEndAsync(ct);
        if (Encoding.UTF8.GetByteCount(body) > MaxPayloadBytes)
            return BadRequest(new { code = 400, message = "payload too large (64KB max)" });

        // varsSchema 白名单过滤（防变量注入，spec §2.3/§6）
        string varsJson;
        try
        {
            var cfg = WfTriggerConfig.ParseMessage(trigger.ConfigJson);
            varsJson = WfTriggerVarsMapper.FilterBySchema(body, cfg.VarsSchema);
        }
        catch (JsonException)
        {
            return BadRequest(new { code = 400, message = "body must be a JSON object" });
        }

        var r = await _triggers.FireAsync(trigger, varsJson, WfTriggerType.Message, idemKey, ct);
        if (!r.Success)
            return StatusCode(500, new { code = 500, message = r.Error });
        return r.Replayed
            ? Ok(new { instanceId = r.InstanceId })                          // 200 幂等重放
            : StatusCode(201, new { instanceId = r.InstanceId });            // 201 新发起
    }
}
