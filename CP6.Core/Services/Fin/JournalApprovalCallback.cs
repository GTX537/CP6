using CP6.Core.Services.Wf;
using CP6.Entity.DomainModels.Fin;

namespace CP6.Core.Services.Fin;

/// <summary>
/// 财务记账凭证审批回调（OA 章05 §4/§7 示范 ★，兑现 IApprovalService 桩）。
/// BizType="FinJournalPost"：把"手工凭证 maker-checker 过账"接到 OA 审批——
/// 凭证 Draft→提交复核(PendingReview)→起 OA 审批→通过则 OnApproved 调 PostAsync 过账、驳回则 RejectAsync。
/// <para>
/// 复核人取 OA 终态决策人（DecidedById），天然满足"过账人≠制单人"铁则（审批人=制单人时 PostAsync
/// 返 E-FIN-111，本回调抛异常触发整体回滚——OA 不会"假通过"）。回调幂等：已 Posted 直接跳过。
/// </para>
/// </summary>
public class JournalApprovalCallback : IApprovalCallback
{
    private readonly IJournalEntryService _journal;

    public JournalApprovalCallback(IJournalEntryService journal) => _journal = journal;

    public string BizType => "FinJournalPost";

    public async Task OnApprovedAsync(ApprovalCallbackContext ctx)
    {
        var entryId = ParseBizId(ctx.BizId);
        var entry = await _journal.GetAsync(entryId)
                    ?? throw new InvalidOperationException($"凭证不存在：{ctx.BizId}");

        if (entry.Status == JournalStatus.Posted) return;                    // 幂等：已过账跳过
        if (entry.Status != JournalStatus.PendingReview)
            throw new InvalidOperationException($"凭证状态非待复核，不能过账：{entry.Status}");

        // 复核人 = OA 终态决策人；缺失则兜底系统账户（仍 ≠ 任何真实制单人，不破坏 maker-checker）
        var checkerId = ctx.DecidedById?.ToString() ?? "OA-APPROVAL";
        var r = await _journal.PostAsync(entryId, checkerId);
        if (!r.Ok) throw new InvalidOperationException($"凭证过账失败：{r.Code}");   // 抛异常 → 整体回滚
    }

    public async Task OnRejectedAsync(ApprovalCallbackContext ctx)
    {
        var entryId = ParseBizId(ctx.BizId);
        var entry = await _journal.GetAsync(entryId);
        if (entry is null) return;
        if (entry.Status != JournalStatus.PendingReview) return;             // 幂等：非待复核不处理

        var r = await _journal.RejectAsync(entryId, ctx.Reason ?? "审批驳回");
        if (!r.Ok) throw new InvalidOperationException($"凭证驳回失败：{r.Code}");
    }

    private static Guid ParseBizId(string bizId) =>
        Guid.TryParse(bizId, out var id)
            ? id
            : throw new InvalidOperationException($"凭证审批 BizId 非法（应为凭证 Guid）：{bizId}");
}
