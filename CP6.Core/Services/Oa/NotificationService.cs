using CP6.Core.EFDbContext;
using CP6.Entity.DomainModels.Wf;
using Microsoft.EntityFrameworkCore;

namespace CP6.Core.Services.Oa;

/// <summary>
/// 站内通知中心服务实现（OA Phase D-1 N-T2）。
/// 构造注入 <see cref="CP6Context"/>；无其他依赖。
/// </summary>
public class NotificationService : INotificationService
{
    private readonly CP6Context _db;
    public NotificationService(CP6Context db) { _db = db; }

    // ── List ──────────────────────────────────────────────────────────

    public async Task<IReadOnlyList<NotificationItem>> ListAsync(
        Guid userId, bool unreadOnly, int page, int pageSize)
    {
        var q = _db.Wf_Notifications.Where(n => n.UserId == userId);
        if (unreadOnly) q = q.Where(n => !n.IsRead);

        var rows = await q
            .OrderByDescending(n => n.CreateDate)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync();

        return rows.Select(n => new NotificationItem(
            n.Id, n.Type, n.Title, n.Body,
            n.InstanceId, n.TaskId, n.FlowKey,
            n.IsRead, n.CreateDate)).ToList();
    }

    // ── UnreadCount ───────────────────────────────────────────────────

    public Task<int> UnreadCountAsync(Guid userId) =>
        _db.Wf_Notifications.CountAsync(n => n.UserId == userId && !n.IsRead);

    // ── MarkRead ──────────────────────────────────────────────────────

    public async Task MarkReadAsync(Guid userId, Guid id)
    {
        var n = await _db.Wf_Notifications
            .FirstOrDefaultAsync(x => x.Id == id && x.UserId == userId);
        if (n is null) return;          // 他人 id 或不存在 → 静默跳过

        if (!n.IsRead)                  // 幂等：已读不重置 ReadAt
        {
            n.IsRead = true;
            n.ReadAt = DateTime.UtcNow;
        }
        await _db.SaveChangesAsync();
    }

    // ── MarkAllRead ───────────────────────────────────────────────────

    public async Task MarkAllReadAsync(Guid userId)
    {
        var unread = await _db.Wf_Notifications
            .Where(n => n.UserId == userId && !n.IsRead)
            .ToListAsync();

        if (unread.Count == 0) return;

        var now = DateTime.UtcNow;
        foreach (var n in unread)
        {
            n.IsRead = true;
            n.ReadAt = now;
        }
        await _db.SaveChangesAsync();
    }

    // ── CreateAsync（仅 Add，不 SaveChanges）─────────────────────────

    /// <summary>
    /// 只将通知实体 Add 到 ChangeTracker，<b>不调用 SaveChanges</b>。
    /// 调用方（PersistentWfNotifier）持有同一 CP6Context，随引擎事务一起落库。
    /// </summary>
    public Task CreateAsync(Guid userId, int type, string title, string body,
        Guid? instanceId, Guid? taskId, string? flowKey)
    {
        _db.Wf_Notifications.Add(new Wf_Notification
        {
            Id         = Guid.NewGuid(),
            UserId     = userId,
            Type       = type,
            Title      = title,
            Body       = body,
            InstanceId = instanceId,
            TaskId     = taskId,
            FlowKey    = flowKey,
            IsRead     = false,
        });
        return Task.CompletedTask;
    }
}
