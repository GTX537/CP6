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
        // 无启用绑定 → 自动放行（向后兼容：未配审批流程的 BizType 直通）
        var hasBinding = await _db.Wf_ApprovalBindings.AnyAsync(b => b.BizType == request.BizType && b.Enable);
        if (!hasBinding)
            return new ApprovalSubmitResult { AutoApproved = true, ApprovalRef = $"AUTO-{request.BizKey}" };

        // 提交人 userName → Sys_User.Id（OA 起流程需 starterId Guid；缺则 Empty，审批人由流程定义指定）
        var starterId = await _db.Sys_Users
            .Where(u => u.UserName == request.Submitter)
            .Select(u => (Guid?)u.Id).FirstOrDefaultAsync() ?? Guid.Empty;

        var instanceId = await _wfApproval.SubmitAsync(request.BizType, request.BizKey, starterId,
            new { amount = request.Amount, submitter = request.Submitter });

        return new ApprovalSubmitResult { AutoApproved = false, ApprovalRef = instanceId.ToString() };
    }
}
