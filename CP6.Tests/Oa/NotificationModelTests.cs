using CP6.Core.EFDbContext;
using CP6.Entity.DomainModels.Wf;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace CP6.Tests;

public class NotificationModelTests
{
    private static CP6Context NewDb() => new(new DbContextOptionsBuilder<CP6Context>()
        .UseInMemoryDatabase(Guid.NewGuid().ToString())
        .ConfigureWarnings(w => w.Ignore(InMemoryEventId.TransactionIgnoredWarning)).Options);

    [Fact]
    public async Task Wf_Notification_CanAddAndQueryBack()
    {
        using var db = NewDb();
        var userId = Guid.NewGuid();
        var instId = Guid.NewGuid();
        var taskId = Guid.NewGuid();

        // 不显式设 TenantId — EF SaveChanges 自动盖章 DefaultTenant，查询过滤器照匹配
        var n = new Wf_Notification
        {
            Id         = Guid.NewGuid(),
            UserId     = userId,
            Type       = WfNotificationType.TodoCreated,
            Title      = "您有新的待办：请假申请",
            Body       = "张三 提交了请假申请，请审批。",
            InstanceId = instId,
            TaskId     = taskId,
            FlowKey    = "leave_request",
            IsRead     = false,
            ReadAt     = null,
        };
        db.Wf_Notifications.Add(n);
        await db.SaveChangesAsync();

        var got = await db.Wf_Notifications.SingleAsync();
        Assert.Equal(userId,              got.UserId);
        Assert.Equal(WfNotificationType.TodoCreated, got.Type);
        Assert.Equal("您有新的待办：请假申请",       got.Title);
        Assert.Equal("张三 提交了请假申请，请审批。", got.Body);
        Assert.Equal(instId,              got.InstanceId);
        Assert.Equal(taskId,              got.TaskId);
        Assert.Equal("leave_request",     got.FlowKey);
        Assert.False(got.IsRead);
        Assert.Null(got.ReadAt);
    }

    [Fact]
    public async Task Wf_Notification_IsRead_CanBeUpdated()
    {
        using var db = NewDb();
        var n = new Wf_Notification
        {
            Id     = Guid.NewGuid(),
            UserId = Guid.NewGuid(),
            Type   = WfNotificationType.FlowApproved,
            Title  = "您的申请已通过",
            Body   = "请假申请已签核完成。",
        };
        db.Wf_Notifications.Add(n);
        await db.SaveChangesAsync();

        var readAt = new DateTime(2026, 6, 28, 12, 0, 0, DateTimeKind.Utc);
        var got = await db.Wf_Notifications.SingleAsync();
        got.IsRead = true;
        got.ReadAt = readAt;
        await db.SaveChangesAsync();

        var updated = await db.Wf_Notifications.SingleAsync();
        Assert.True(updated.IsRead);
        Assert.Equal(readAt, updated.ReadAt);
    }

    [Fact]
    public async Task Wf_Notification_NullableFields_AllowNull()
    {
        using var db = NewDb();
        var n = new Wf_Notification
        {
            Id     = Guid.NewGuid(),
            UserId = Guid.NewGuid(),
            Type   = WfNotificationType.FlowRejected,
            Title  = "您的申请被驳回",
            Body   = "审批意见：材料不齐。",
            // InstanceId, TaskId, FlowKey all null
        };
        db.Wf_Notifications.Add(n);
        await db.SaveChangesAsync();

        var got = await db.Wf_Notifications.SingleAsync();
        Assert.Null(got.InstanceId);
        Assert.Null(got.TaskId);
        Assert.Null(got.FlowKey);
    }

    // ─── WfNotificationType 常量值 ──────────────────────────────────

    [Fact]
    public void WfNotificationType_Constants_HaveExpectedValues()
    {
        Assert.Equal(1, WfNotificationType.TodoCreated);
        Assert.Equal(2, WfNotificationType.FlowApproved);
        Assert.Equal(3, WfNotificationType.FlowRejected);
        Assert.Equal(4, WfNotificationType.Timeout);
    }
}
