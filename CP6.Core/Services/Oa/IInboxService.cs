namespace CP6.Core.Services.Oa;

/// <summary>电子表单信箱读模型服务（umbrella §4.3）。读 L1，写动作经 L0 引擎。T5~T8 增量。</summary>
public interface IInboxService
{
    // ── 未處理（T5）──
    Task<IReadOnlyList<InboxPendingItem>> PendingAsync(Guid userId);     // 待審核：我的待办
    Task<IReadOnlyList<InboxCcItem>> PendingCcAsync(Guid userId);        // CC：抄送我
    Task MarkTaskReadAsync(Guid userId, Guid taskId);                    // 幂等、仅本人
    Task MarkCcReadAsync(Guid userId, Guid ccId);                        // 幂等、仅本人
    // ── 在途（T6）──
    Task<IReadOnlyList<InboxRunningItem>> RunningAsync(Guid userId);
    // ── 已處理（T6）──：tab = mine | cc | all；year/month 可空（null=不限月）
    Task<IReadOnlyList<InboxDoneItem>> DoneAsync(Guid userId, int? year, int? month, string tab = "mine");
    // ── 批量办理（T7）──
    Task<IReadOnlyList<BatchActResultItem>> ActBatchAsync(Guid userId, IReadOnlyList<Guid> taskIds, bool approve, string? comment = null);
    // ── 详情 + 仪表盘（T8）──
    Task<InboxDetail?> DetailAsync(Guid instanceId);   // 不存在 → null（控制器转 404）
    Task<InboxStats> StatsAsync(Guid userId);
    // ── 表單查詢（Phase C）──
    Task<IReadOnlyList<FormQueryItem>> QueryAsync(FormQueryFilter filter);
}
