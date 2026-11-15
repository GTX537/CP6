using CP6.Core.EFDbContext;
using CP6.Core.Services.Wf;
using CP6.Entity.DomainModels.Fin;
using Microsoft.EntityFrameworkCore;

namespace CP6.Core.Services.Fin;

/// <summary>
/// 财务记账凭证审批回调（OA 章05 §4/§7 示范 ★，兑现 IApprovalService 桩）。
/// BizType="FinJournalPost"：把"手工凭证 maker-checker 过账"接到 OA 审批——
/// 凭证 Draft→提交复核(PendingReview)→起 OA 审批→通过则 OnApproved 调 PostAsync 过账、驳回则 RejectAsync。
/// <para>
/// PostgreSQL 按实际人工决策者执行制单与复核分离。失败抛异常，回调与 OA 办理一起回滚。
/// </para>
/// </summary>
public class JournalApprovalCallback(IJournalEntryService journal, CP6Context db) : IApprovalCallback
{
    public string BizType => "FinJournalPost";

    public async Task OnApprovedAsync(ApprovalCallbackContext ctx)
    {
        var entryId = ParseBizId(ctx.BizId);
        var entry = await journal.GetAsync(entryId)
                    ?? throw new InvalidOperationException($"凭证不存在：{ctx.BizId}");

        if (entry.Status == JournalStatus.Posted) return;                    // 幂等：已过账跳过
        if (entry.Status != JournalStatus.PendingReview)
            throw new InvalidOperationException($"凭证状态非待复核，不能过账：{entry.Status}");

        var checker = ctx.DecidedById;
        if (db.Database.IsNpgsql())
        {
            checker = ctx.CompletionOrigin switch
            {
                ApprovalCompletionOrigin.TaskApproval => ctx.DecidedById,
                ApprovalCompletionOrigin.Continuation => await ReadPriorApproverAsync(ctx.InstanceId),
                _ => null,
            };
            if (checker.GetValueOrDefault() == Guid.Empty)
                throw new InvalidOperationException("凭证过账失败：E-FIN-111");
        }
        var r = await journal.PostAsync(entryId, checker?.ToString("D") ?? "OA-APPROVAL", checker);
        if (!r.Ok) throw new InvalidOperationException($"凭证过账失败：{r.Code}");   // 抛异常 → 整体回滚
    }

    private async Task<Guid?> ReadPriorApproverAsync(Guid instanceId)
    {
        // The engine writes the actual actor to the append-only vote, including delegation.
        // An automatic continuation can use a committed approval from the same linked run.
        var history = await db.Wf_FlowHistories.AsNoTracking().Where(row => row.InstanceId == instanceId
                && (row.Action == "approve" || row.Action == "reject" || row.Action == "submit" || row.Action == "sendback"
                    || row.Action == "branchPruned" || row.Action == "timeoutErrorEdge"))
            .OrderByDescending(row => row.CreateDate).Select(row => new { row.Action, row.ActorId, row.CreateDate })
            .Take(2).ToListAsync();
        if (history.Count == 0 || history[0].Action != "approve"
            || (history.Count == 2 && history[0].CreateDate == history[1].CreateDate))
            return null;
        return history[0].ActorId;
    }

    public async Task OnRejectedAsync(ApprovalCallbackContext ctx)
    {
        var entryId = ParseBizId(ctx.BizId);
        var entry = await journal.GetAsync(entryId);
        if (entry is null) return;
        if (entry.Status != JournalStatus.PendingReview) return;             // 幂等：非待复核不处理

        var r = await journal.RejectAsync(entryId, ctx.Reason ?? "审批驳回");
        if (!r.Ok) throw new InvalidOperationException($"凭证驳回失败：{r.Code}");
    }

    private static Guid ParseBizId(string bizId) =>
        Guid.TryParse(bizId, out var id)
            ? id
            : throw new InvalidOperationException($"凭证审批 BizId 非法（应为凭证 Guid）：{bizId}");
}
