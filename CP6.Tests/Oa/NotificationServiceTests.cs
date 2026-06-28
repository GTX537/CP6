using CP6.Core.EFDbContext;
using CP6.Core.Services.Oa;
using CP6.Entity.DomainModels.Wf;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace CP6.Tests;

/// <summary>
/// NotificationService（N-T2）单元测试。
/// 不显式设 TenantId — EF SaveChanges 自动盖章 DefaultTenant，查询过滤器照匹配。
/// CreateAsync 只 Add 不 Save；测试中手动 SaveChangesAsync 模拟引擎提交。
/// </summary>
public class NotificationServiceTests
{
    private static CP6Context NewDb() => new(new DbContextOptionsBuilder<CP6Context>()
        .UseInMemoryDatabase(Guid.NewGuid().ToString())
        .ConfigureWarnings(w => w.Ignore(InMemoryEventId.TransactionIgnoredWarning)).Options);

    private static INotificationService Svc(CP6Context db) => new NotificationService(db);

    // ── CreateAsync 不 save 验证 ────────────────────────────────────────

    [Fact]
    public async Task CreateAsync_OnlyAdds_DoesNotSaveByItself()
    {
        using var db = NewDb();
        var userId = Guid.NewGuid();

        // 调用 CreateAsync 但不手动 save
        await Svc(db).CreateAsync(userId, WfNotificationType.TodoCreated,
            "待办标题", "待办正文", null, null, null);

        // 引擎 save 前：持久化层为 0
        Assert.Equal(0, await db.Wf_Notifications.CountAsync());

        // 模拟引擎 SaveChanges 落库
        await db.SaveChangesAsync();
        Assert.Equal(1, await db.Wf_Notifications.CountAsync());
    }

    [Fact]
    public async Task CreateAsync_WithoutSave_SimulatesRollback()
    {
        using var db = NewDb();
        var userId = Guid.NewGuid();

        await Svc(db).CreateAsync(userId, WfNotificationType.FlowApproved,
            "已通过", "申请已签核完成。", Guid.NewGuid(), null, "leave");
        // 不 save — 模拟 rollback，持久化层应始终为 0
        Assert.Equal(0, await db.Wf_Notifications.CountAsync());
    }

    // ── List 倒序 + CreateAsync+Save 后可查回 ─────────────────────────

    [Fact]
    public async Task List_AfterCreateAndSave_ReturnsMostRecentFirst()
    {
        using var db = NewDb();
        var userId = Guid.NewGuid();

        // 直接添加两条实体，设定明确 CreateDate 以确保顺序稳定
        db.Wf_Notifications.AddRange(
            new Wf_Notification
            {
                Id = Guid.NewGuid(), UserId = userId, Type = WfNotificationType.TodoCreated,
                Title = "早", Body = "早一点",
                CreateDate = new DateTime(2026, 6, 28, 9, 0, 0, DateTimeKind.Utc)
            },
            new Wf_Notification
            {
                Id = Guid.NewGuid(), UserId = userId, Type = WfNotificationType.FlowApproved,
                Title = "晚", Body = "晚一点",
                CreateDate = new DateTime(2026, 6, 28, 10, 0, 0, DateTimeKind.Utc)
            });
        await db.SaveChangesAsync();

        var list = await Svc(db).ListAsync(userId, unreadOnly: false, page: 1, pageSize: 10);

        Assert.Equal(2, list.Count);
        Assert.Equal("晚", list[0].Title);   // 较新在前
        Assert.Equal("早", list[1].Title);
    }

    [Fact]
    public async Task List_CreateAsyncThenSave_CorrectDto()
    {
        using var db = NewDb();
        var userId = Guid.NewGuid();
        var instId = Guid.NewGuid();
        var taskId = Guid.NewGuid();

        await Svc(db).CreateAsync(userId, WfNotificationType.TodoCreated,
            "新待办：请假申请", "张三提交了请假申请，请审批。", instId, taskId, "leave");
        await db.SaveChangesAsync(); // 模拟引擎 save

        var list = await Svc(db).ListAsync(userId, unreadOnly: false, page: 1, pageSize: 10);
        var item = Assert.Single(list);

        Assert.Equal(userId, item.Id == Guid.Empty ? userId : userId); // Id 非 Guid.Empty
        Assert.NotEqual(Guid.Empty, item.Id);
        Assert.Equal(WfNotificationType.TodoCreated, item.Type);
        Assert.Equal("新待办：请假申请", item.Title);
        Assert.Equal("张三提交了请假申请，请审批。", item.Body);
        Assert.Equal(instId, item.InstanceId);
        Assert.Equal(taskId, item.TaskId);
        Assert.Equal("leave", item.FlowKey);
        Assert.False(item.IsRead);
    }

    [Fact]
    public async Task List_OnlyReturnsCurrentUsersNotifications()
    {
        using var db = NewDb();
        var me = Guid.NewGuid();
        var other = Guid.NewGuid();

        db.Wf_Notifications.AddRange(
            new Wf_Notification { Id = Guid.NewGuid(), UserId = me,    Type = 1, Title = "我的", Body = "b" },
            new Wf_Notification { Id = Guid.NewGuid(), UserId = other, Type = 1, Title = "他的", Body = "b" });
        await db.SaveChangesAsync();

        var list = await Svc(db).ListAsync(me, unreadOnly: false, page: 1, pageSize: 10);
        Assert.Single(list);
        Assert.Equal("我的", list[0].Title);
    }

    // ── unreadOnly 过滤 ─────────────────────────────────────────────────

    [Fact]
    public async Task List_UnreadOnly_ExcludesReadItems()
    {
        using var db = NewDb();
        var userId = Guid.NewGuid();

        db.Wf_Notifications.AddRange(
            new Wf_Notification
            {
                Id = Guid.NewGuid(), UserId = userId, Type = 1,
                Title = "未读", Body = "b", IsRead = false
            },
            new Wf_Notification
            {
                Id = Guid.NewGuid(), UserId = userId, Type = 2,
                Title = "已读", Body = "b", IsRead = true, ReadAt = DateTime.UtcNow
            });
        await db.SaveChangesAsync();

        var allList    = await Svc(db).ListAsync(userId, unreadOnly: false, page: 1, pageSize: 10);
        var unreadList = await Svc(db).ListAsync(userId, unreadOnly: true,  page: 1, pageSize: 10);

        Assert.Equal(2, allList.Count);
        Assert.Single(unreadList);
        Assert.Equal("未读", unreadList[0].Title);
    }

    // ── UnreadCount ─────────────────────────────────────────────────────

    [Fact]
    public async Task UnreadCount_CorrectCount()
    {
        using var db = NewDb();
        var userId = Guid.NewGuid();

        db.Wf_Notifications.AddRange(
            new Wf_Notification { Id = Guid.NewGuid(), UserId = userId, Type = 1, Title = "u1", Body = "b", IsRead = false },
            new Wf_Notification { Id = Guid.NewGuid(), UserId = userId, Type = 1, Title = "u2", Body = "b", IsRead = false },
            new Wf_Notification { Id = Guid.NewGuid(), UserId = userId, Type = 2, Title = "r1", Body = "b", IsRead = true });
        await db.SaveChangesAsync();

        var count = await Svc(db).UnreadCountAsync(userId);
        Assert.Equal(2, count);
    }

    [Fact]
    public async Task UnreadCount_ZeroForNoNotifications()
    {
        using var db = NewDb();
        var count = await Svc(db).UnreadCountAsync(Guid.NewGuid());
        Assert.Equal(0, count);
    }

    [Fact]
    public async Task UnreadCount_OnlyCountsCurrentUser()
    {
        using var db = NewDb();
        var me    = Guid.NewGuid();
        var other = Guid.NewGuid();

        db.Wf_Notifications.AddRange(
            new Wf_Notification { Id = Guid.NewGuid(), UserId = me,    Type = 1, Title = "a", Body = "b", IsRead = false },
            new Wf_Notification { Id = Guid.NewGuid(), UserId = other, Type = 1, Title = "b", Body = "b", IsRead = false });
        await db.SaveChangesAsync();

        Assert.Equal(1, await Svc(db).UnreadCountAsync(me));
        Assert.Equal(1, await Svc(db).UnreadCountAsync(other));
    }

    // ── MarkRead：本人成功 + ReadAt 设置 ────────────────────────────────

    [Fact]
    public async Task MarkRead_MarksAsRead_SetsReadAt()
    {
        using var db = NewDb();
        var userId = Guid.NewGuid();
        var id     = Guid.NewGuid();
        db.Wf_Notifications.Add(new Wf_Notification
        {
            Id = id, UserId = userId, Type = 1, Title = "T", Body = "b", IsRead = false
        });
        await db.SaveChangesAsync();

        await Svc(db).MarkReadAsync(userId, id);

        var n = await db.Wf_Notifications.SingleAsync();
        Assert.True(n.IsRead);
        Assert.NotNull(n.ReadAt);
    }

    // ── MarkRead 幂等：已读不重置 ReadAt ───────────────────────────────

    [Fact]
    public async Task MarkRead_Idempotent_DoesNotResetReadAt()
    {
        using var db = NewDb();
        var userId      = Guid.NewGuid();
        var id          = Guid.NewGuid();
        var firstReadAt = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        db.Wf_Notifications.Add(new Wf_Notification
        {
            Id = id, UserId = userId, Type = 1, Title = "T", Body = "b",
            IsRead = true, ReadAt = firstReadAt
        });
        await db.SaveChangesAsync();

        // 再次标记已读的通知
        await Svc(db).MarkReadAsync(userId, id);

        var n = await db.Wf_Notifications.SingleAsync();
        Assert.True(n.IsRead);
        Assert.Equal(firstReadAt, n.ReadAt);   // ReadAt 不被重置
    }

    // ── MarkRead：他人 id → 静默忽略 ───────────────────────────────────

    [Fact]
    public async Task MarkRead_OtherUserNotification_SilentlySkips()
    {
        using var db = NewDb();
        var me    = Guid.NewGuid();
        var other = Guid.NewGuid();
        var id    = Guid.NewGuid();

        db.Wf_Notifications.Add(new Wf_Notification
        {
            Id = id, UserId = other, Type = 1, Title = "T", Body = "b", IsRead = false
        });
        await db.SaveChangesAsync();

        // me 试图标记 other 的通知 → 静默跳过，不抛异常
        var ex = await Record.ExceptionAsync(() => Svc(db).MarkReadAsync(me, id));
        Assert.Null(ex);

        var n = await db.Wf_Notifications.SingleAsync();
        Assert.False(n.IsRead);   // 未受影响
    }

    [Fact]
    public async Task MarkRead_NonExistentId_SilentlySkips()
    {
        using var db = NewDb();
        var ex = await Record.ExceptionAsync(() =>
            Svc(db).MarkReadAsync(Guid.NewGuid(), Guid.NewGuid()));
        Assert.Null(ex);
    }

    // ── MarkAllRead 清零 ────────────────────────────────────────────────

    [Fact]
    public async Task MarkAllRead_SetsAllUnreadToRead()
    {
        using var db = NewDb();
        var userId = Guid.NewGuid();
        db.Wf_Notifications.AddRange(
            new Wf_Notification { Id = Guid.NewGuid(), UserId = userId, Type = 1, Title = "u1", Body = "b", IsRead = false },
            new Wf_Notification { Id = Guid.NewGuid(), UserId = userId, Type = 1, Title = "u2", Body = "b", IsRead = false },
            new Wf_Notification { Id = Guid.NewGuid(), UserId = userId, Type = 2, Title = "r1", Body = "b", IsRead = true });
        await db.SaveChangesAsync();

        await Svc(db).MarkAllReadAsync(userId);

        var all = await db.Wf_Notifications.ToListAsync();
        Assert.All(all, n => Assert.True(n.IsRead));
        Assert.Equal(0, await Svc(db).UnreadCountAsync(userId));
    }

    [Fact]
    public async Task MarkAllRead_OnlyAffectsCurrentUser()
    {
        using var db = NewDb();
        var me    = Guid.NewGuid();
        var other = Guid.NewGuid();

        db.Wf_Notifications.AddRange(
            new Wf_Notification { Id = Guid.NewGuid(), UserId = me,    Type = 1, Title = "mine",   Body = "b", IsRead = false },
            new Wf_Notification { Id = Guid.NewGuid(), UserId = other, Type = 1, Title = "theirs", Body = "b", IsRead = false });
        await db.SaveChangesAsync();

        await Svc(db).MarkAllReadAsync(me);

        var mine   = await db.Wf_Notifications.FirstAsync(n => n.UserId == me);
        var theirs = await db.Wf_Notifications.FirstAsync(n => n.UserId == other);
        Assert.True(mine.IsRead);
        Assert.False(theirs.IsRead);   // 不受影响
    }

    [Fact]
    public async Task MarkAllRead_NoUnread_IsNoOp()
    {
        using var db = NewDb();
        // 空 DB — 不抛异常
        var ex = await Record.ExceptionAsync(() =>
            Svc(db).MarkAllReadAsync(Guid.NewGuid()));
        Assert.Null(ex);
    }

    // ── 分页 ────────────────────────────────────────────────────────────

    [Fact]
    public async Task List_Pagination_ReturnsCorrectPages()
    {
        using var db = NewDb();
        var userId = Guid.NewGuid();

        // 5 条，CreateDate 依次递增（N0 最旧，N4 最新）
        for (var i = 0; i < 5; i++)
        {
            db.Wf_Notifications.Add(new Wf_Notification
            {
                Id         = Guid.NewGuid(),
                UserId     = userId,
                Type       = 1,
                Title      = $"N{i}",
                Body       = "b",
                CreateDate = new DateTime(2026, 6, 28, 8 + i, 0, 0, DateTimeKind.Utc)
            });
        }
        await db.SaveChangesAsync();

        var page1 = await Svc(db).ListAsync(userId, unreadOnly: false, page: 1, pageSize: 3);
        var page2 = await Svc(db).ListAsync(userId, unreadOnly: false, page: 2, pageSize: 3);

        Assert.Equal(3, page1.Count);
        Assert.Equal(2, page2.Count);

        // 倒序：N4 → N3 → N2（page1），N1 → N0（page2）
        Assert.Equal("N4", page1[0].Title);
        Assert.Equal("N3", page1[1].Title);
        Assert.Equal("N2", page1[2].Title);
        Assert.Equal("N1", page2[0].Title);
        Assert.Equal("N0", page2[1].Title);
    }

    [Fact]
    public async Task List_PageBeyondData_ReturnsEmpty()
    {
        using var db = NewDb();
        var userId = Guid.NewGuid();
        db.Wf_Notifications.Add(new Wf_Notification
        {
            Id = Guid.NewGuid(), UserId = userId, Type = 1, Title = "only", Body = "b"
        });
        await db.SaveChangesAsync();

        var page2 = await Svc(db).ListAsync(userId, unreadOnly: false, page: 2, pageSize: 10);
        Assert.Empty(page2);
    }
}
