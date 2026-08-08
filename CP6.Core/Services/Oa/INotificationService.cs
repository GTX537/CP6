namespace CP6.Core.Services.Oa;

/// <summary>
/// 站内通知中心服务接口（OA Phase D-1 §通知中心 N-T2）。
/// <list type="bullet">
///   <item>List / UnreadCount / MarkRead / MarkAllRead：读写类，自行 SaveChanges。</item>
///   <item>CreateAsync：<b>仅 Add，不 SaveChanges</b>。复合通知器与引擎共用同一工作单元，
///         由调用方（PersistentWfNotifier）随引擎 SaveChanges 一起落库——仿 Phase A 读模型钩子。</item>
/// </list>
/// </summary>
public interface INotificationService
{
    /// <summary>分页列表，按 CreateDate 倒序。page 从 1 起算。</summary>
    Task<IReadOnlyList<NotificationItem>> ListAsync(
        Guid userId, bool unreadOnly, int page, int pageSize);

    /// <summary>本人未读通知数量。</summary>
    Task<int> UnreadCountAsync(Guid userId);

    /// <summary>
    /// 标记单条已读（幂等：已读不重置 ReadAt）。
    /// 若 id 属他人或不存在则静默跳过（不抛异常）。
    /// </summary>
    Task MarkReadAsync(Guid userId, Guid id);

    /// <summary>本人全部未读通知置已读。</summary>
    Task MarkAllReadAsync(Guid userId);

    /// <summary>
    /// 向指定用户创建一条通知（<b>不 SaveChanges</b>，仅 Add 到共享 context）。
    /// 调用方须在合适时机 SaveChanges（通常与引擎同事务）。
    /// </summary>
    Task CreateAsync(Guid userId, int type, string title, string body,
        Guid? instanceId, Guid? taskId, string? flowKey);

    Task CreateOutboxAsync(Guid userId, int type, string title, string body,
        Guid? instanceId, Guid? taskId, string? flowKey,
        string eventKey, bool inAppRequested, bool emailRequested) =>
        CreateAsync(userId, type, title, body, instanceId, taskId, flowKey);
}
