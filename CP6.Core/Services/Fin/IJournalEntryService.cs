using CP6.Entity.DomainModels.Fin;

namespace CP6.Core.Services.Fin;

/// <summary>
/// 记账凭证服务（章01 §5/§6）。两条铁律的执行者：①借贷恒等校验不可绕过 ②无 Update/Delete，
/// 只能 Post/Reverse。手工凭证强制 maker-checker；自动凭证可信直过。
/// </summary>
public interface IJournalEntryService
{
    /// <summary>取凭证（含分录行）。</summary>
    Task<JournalEntry?> GetAsync(Guid id);

    /// <summary>列出凭证（可按期间/状态过滤）。</summary>
    Task<List<JournalEntry>> ListAsync(Guid? periodId = null, JournalStatus? status = null);

    /// <summary>新建草稿（按记账日归期 + 采番；草稿允许暂不平，提交时才校验）。返回新 Id。</summary>
    Task<Guid> CreateDraftAsync(JournalEntry entry, string makerId);

    /// <summary>提交复核（Draft→PendingReview，过借贷恒等校验）。</summary>
    Task<FinResult> SubmitForReviewAsync(Guid entryId);

    /// <summary>过账（PendingReview→Posted；checker≠maker + 期间 Open + 再校恒等）。</summary>
    Task<FinResult> PostAsync(Guid entryId, string checkerId);

    /// <summary>自动凭证直过（非 Manual 来源；建+校+过一气呵成，给 Plan 2 自动凭证引擎）。</summary>
    Task<FinResult> AutoPostAsync(JournalEntry entry);

    /// <summary>驳回（PendingReview→Rejected + 原因）。</summary>
    Task<FinResult> RejectAsync(Guid entryId, string reason);

    /// <summary>
    /// 红冲（章01 §6）。只 Posted 可红冲；生成借贷对调的反向凭证，原凭证→Reversed，互指。
    /// autoPost=true（系统触发，如出货取消/付款撤销）→ 红冲直过；false（手工红冲）→ 待复核。
    /// </summary>
    Task<FinResult> ReverseAsync(Guid entryId, string makerId, string reason, bool autoPost = false);
}
