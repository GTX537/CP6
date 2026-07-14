namespace CP6.Core.Services.Oa;

/// <summary>电子表单信箱读模型服务（umbrella §4.3）。读 L1，写动作经 L0 引擎。T5~T8 增量。</summary>
public interface IInboxService
{
    // ── 未處理（T5 + wfs-inbox-ux §5 rowMode）──
    // rowMode: "merged"(默认，同实例多任务合并一行显最新态) | "expanded"(逐任务平铺)。
    // page/pageSize 可选（null=全量，现状不变）；merged 下分组先于分页（跨页正确性）。
    Task<IReadOnlyList<InboxPendingItem>> PendingAsync(Guid userId, string rowMode = "merged", int? page = null, int? pageSize = null);
    Task<IReadOnlyList<InboxCcItem>> PendingCcAsync(Guid userId);        // CC：抄送我
    Task MarkTaskReadAsync(Guid userId, Guid taskId);                    // 幂等、仅本人
    Task MarkCcReadAsync(Guid userId, Guid ccId);                        // 幂等、仅本人
    // ── 在途（T6）──
    Task<IReadOnlyList<InboxRunningItem>> RunningAsync(Guid userId);
    // ── 已處理（T6）──：tab = mine | cc | all；year/month 可空（null=不限月）
    Task<IReadOnlyList<InboxDoneItem>> DoneAsync(Guid userId, int? year, int? month, string tab = "mine");
    // ── 批量办理（T7）──
    Task<IReadOnlyList<BatchActResultItem>> ActBatchAsync(Guid userId, IReadOnlyList<Guid> taskIds, bool approve, string? comment = null);
    // ── 批量办理（act-as，Phase C T8）── actorId=实际执行人；onBehalfOf=被代理人（null=本人操作）
    Task<IReadOnlyList<BatchActResultItem>> ActBatchAsAsync(Guid actorId, Guid? onBehalfOf, IReadOnlyList<Guid> taskIds, bool approve, string? comment = null);
    // ── 详情 + 仪表盘（T8）──
    Task<InboxDetail?> DetailAsync(Guid instanceId);   // 不存在 → null（控制器转 404）
    Task<InboxStats> StatsAsync(Guid userId);
    // ── 表單查詢（Phase C）──
    Task<IReadOnlyList<FormQueryItem>> QueryAsync(FormQueryFilter filter);
    // ── 在途批量转单（wfs-inbox-ux §3）── actorId=操作者（管理员本人）；逐条独立事务（引擎 TransferAsync 内部 SaveChanges）
    Task<BatchTransferReport> BatchTransferAsync(Guid actorId, Guid fromUserId, Guid toUserId, string? comment, BatchTransferFilter? filter = null);
    Task<BatchTransferPreview> BatchTransferPreviewAsync(Guid fromUserId, BatchTransferFilter? filter = null);
}
