using CP6.Core.Services.Integration;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace CP6.WebApi.Controllers.Integration;

/// <summary>Echo 样例事件源（QA harness 用，对齐 ServiceTask EchoConnector 先例，spec §3.3）。
/// 业务模块接入真实事件 = 与本控制器同款「一行调用」IWfTriggerBridgeHook.OnEventAsync。
/// 落 Controllers.Integration（与 BridgeHealthController 同族）：本控制器是 Integration 桥接 hook
/// 的 QA 触点，非业务 OA 写端点，不进 OA/WF 权限键守卫扫描面（OawfPermissionAttributeTests 计数锁 16）。</summary>
[ApiController]
[Route("api/oa/wf-trigger-echo")]
[Authorize]
public class WfTriggerEchoController : ControllerBase
{
    private readonly IWfTriggerBridgeHook _hook;

    public WfTriggerEchoController(IWfTriggerBridgeHook hook) { _hook = hook; }

    [HttpPost("fire")]
    public async Task<IActionResult> Fire([FromBody] EchoEventReq r)
    {
        var result = await _hook.OnEventAsync(
            string.IsNullOrWhiteSpace(r.EventKey) ? "QA|OnEchoAsync" : r.EventKey,
            string.IsNullOrWhiteSpace(r.EventId) ? Guid.NewGuid().ToString("N") : r.EventId,
            r.PayloadJson ?? "{}",
            User.Identity?.Name);
        return Ok(new { code = 0, message = "OK", data = new { result.Success, result.MatchedCount, result.FiredCount, result.Message } });
    }

    public record EchoEventReq(string? EventKey, string? EventId, string? PayloadJson);
}
