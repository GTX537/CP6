using CP6.Core.EFDbContext;
using Microsoft.EntityFrameworkCore;
using WfApproval = CP6.Core.Services.Wf.IApprovalService;

namespace CP6.Core.Services.Pur.Contracts;

/// <summary>
/// 采购审批适配器（P-D1 真实实现）。把采购送审端口 <see cref="IApprovalService"/> 委托 OA 审批引擎
/// <see cref="WfApproval"/>（章05 §2）起真实流程，替换 <see cref="StubApprovalService"/>。
/// <para>
/// 判定：BizType 有启用的 <c>Wf_ApprovalBinding</c> → 起流程（AutoApproved=false，单据进 PendingApproval/Submitted，
/// 待 OA 终态经 <c>PurApprovalCallback</c> 回调激活）；无绑定 → 自动放行（向后兼容，未配审批流程的 BizType 直通）。
/// </para>
/// </summary>
public class ApprovalServiceAdapter : IApprovalService
{
    private readonly WfApproval _wfApproval;
    private readonly CP6Context _db;
    public ApprovalServiceAdapter(WfApproval wfApproval, CP6Context db)
    {
        _wfApproval = wfApproval;
        _db = db;
    }

    /// <inheritdoc />
    public async Task<ApprovalSubmitResult> SubmitAsync(ApprovalSubmitRequest request)
    {
        var actorId = request.ActorId;
        if (actorId == Guid.Empty && !string.IsNullOrWhiteSpace(request.Submitter))
            actorId = await _db.Sys_Users.Where(x => x.UserName == request.Submitter && x.Enable)
                .Select(x => x.Id).FirstOrDefaultAsync();
        if (actorId == Guid.Empty) throw new InvalidOperationException("E-PUR-057");
        var instanceId = await _wfApproval.SubmitAsync(
            request.BizType, request.BizKey, actorId, request.Snapshot, request.InstanceId);

        return new ApprovalSubmitResult { AutoApproved = false, ApprovalRef = instanceId.ToString() };
    }
}
