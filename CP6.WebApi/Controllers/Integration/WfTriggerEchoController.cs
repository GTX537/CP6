using System.Data;
using System.Data.Common;
using CP6.Core.EFDbContext;
using CP6.Core.Services.Integration;
using CP6.Core.Services.Sys;
using CP6.WebApi.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace CP6.WebApi.Controllers.Integration;

/// <summary>Echo 样例事件源（QA harness 用，对齐 ServiceTask EchoConnector 先例，spec §3.3）。
/// 业务模块接入真实事件 = 与本控制器同款「一行调用」IWfTriggerBridgeHook.OnEventAsync。
/// 落 Controllers.Integration（与 BridgeHealthController 同族）：本控制器是 Integration 桥接 hook
/// 的 QA 触点，非业务 OA 写端点，不进 OA/WF 权限键守卫扫描面（OawfPermissionAttributeTests 计数锁 17）。</summary>
[ApiController]
[Route("api/oa/wf-trigger-echo")]
[Authorize]
public class WfTriggerEchoController : ControllerBase
{
    private readonly IWfTriggerBridgeHook _hook;
    private readonly CP6Context _db;

    public WfTriggerEchoController(IWfTriggerBridgeHook hook, CP6Context db) { _hook = hook; _db = db; }

    [HttpPost("fire")]
    public async Task<IActionResult> Fire([FromBody] EchoEventReq r, CancellationToken ct)
    {
        var eventKey = string.IsNullOrWhiteSpace(r.EventKey) ? "QA|OnEchoAsync" : r.EventKey;
        var eventId = string.IsNullOrWhiteSpace(r.EventId) ? Guid.NewGuid().ToString("N") : r.EventId;
        if (!_db.Database.IsNpgsql()) return Result(await FireAsync());

        var commitAttempted = false;
        try
        {
            await using var tx = await _db.Database.BeginTransactionAsync(IsolationLevel.ReadCommitted, ct);
            await AuthenticationCompletionGateV1.AcquireAsync(_db, ct);
            var actor = await CurrentActorAsync();
            var result = await FireAsync();
            if (await CurrentActorAsync() != actor)
                throw new IamAdministrationExceptionV1(401, "REAUTH_REQUIRED");
            ct.ThrowIfCancellationRequested();
            commitAttempted = true;
            await tx.CommitAsync(ct);
            return Result(result);
        }
        catch (IamAdministrationExceptionV1 error)
        {
            return StatusCode(error.Status, new { code = error.Status, message = error.Message });
        }
        catch (Exception error) when (commitAttempted && error is (DbException or TimeoutException or OperationCanceledException))
        {
            return StatusCode(503, new { code = 503, message = "AUTHORITY_COMMIT_UNKNOWN" });
        }
        catch (Exception error) when (error is DbException or DbUpdateException or CurrentPermissionUnavailableException)
        {
            return StatusCode(503, new { code = 503, message = "AUTHORITY_FENCE_UNAVAILABLE" });
        }

        Task<Guid> CurrentActorAsync() => IamAdministrationHttpV1.CurrentActor(HttpContext,
            HttpContext.RequestServices, ct, allowImpersonation: true);
        Task<WfTriggerBridgeResult> FireAsync() => _hook.OnEventAsync(eventKey, eventId, r.PayloadJson ?? "{}", User.Identity?.Name, ct);
        IActionResult Result(WfTriggerBridgeResult result) =>
            Ok(new { code = 0, message = "OK", data = new { result.Success, result.MatchedCount, result.FiredCount, result.Message } });
    }

    public record EchoEventReq(string? EventKey, string? EventId, string? PayloadJson);
}
