namespace CP6.Core.Services.Oa;

/// <summary>电子表单信箱读模型服务（umbrella §4.3）。读 L1，写动作经 L0 引擎。T5~T8 增量。</summary>
public interface IInboxService
{
    // ── 未處理（T5）──
    Task<IReadOnlyList<InboxPendingItem>> PendingAsync(Guid userId);     // 待審核：我的待办
    Task<IReadOnlyList<InboxCcItem>> PendingCcAsync(Guid userId);        // CC：抄送我
    Task MarkTaskReadAsync(Guid userId, Guid taskId);                    // 幂等、仅本人
    Task MarkCcReadAsync(Guid userId, Guid ccId);                        // 幂等、仅本人
}
