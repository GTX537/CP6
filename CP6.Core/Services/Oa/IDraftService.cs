namespace CP6.Core.Services.Oa;

/// <summary>草稿（暫存）服务（umbrella §4.3 / R2）。草稿 = Wf_FlowInstance.Status=Draft。</summary>
public interface IDraftService
{
    Task<Guid> SaveDraftAsync(Guid starterId, string flowKey, string varsJson);   // 新建草稿，返回实例 Id
    Task UpdateDraftAsync(Guid starterId, Guid instanceId, string varsJson);       // 改草稿字段
    Task<IReadOnlyList<InboxRunningItem>> ListDraftsAsync(Guid starterId);         // 我的草稿（复用 Running DTO 形状）
    Task DeleteDraftAsync(Guid starterId, Guid instanceId);                        // 删草稿
    Task SubmitDraftAsync(Guid starterId, Guid instanceId);                        // 提交 → 引擎 StartDraftAsync
}
