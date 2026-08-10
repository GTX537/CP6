// CP6.Tests/Oa/PersistentWfNotifierTests.cs
using CP6.Core.EFDbContext;
using CP6.Core.Services.Oa;
using CP6.Entity.DomainModels.Sys;
using CP6.Entity.DomainModels.Wf;
using CP6.WebApi.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Xunit;

namespace CP6.Tests.Oa;

public class PersistentWfNotifierTests
{
    // ── 手写 fakes ──────────────────────────────────────────────────────────
    private sealed class RecordingNotif : INotificationService
    {
        public readonly List<(Guid UserId, int Type, string EventKey, bool InApp, bool Email)> Created = new();
        public Task CreateAsync(Guid userId, int type, string title, string body, Guid? instanceId, Guid? taskId, string? flowKey)
        { Created.Add((userId, type, Guid.NewGuid().ToString("N"), true, false)); return Task.CompletedTask; }
        public Task CreateOutboxAsync(Guid userId, int type, string title, string body,
            Guid? instanceId, Guid? taskId, string? flowKey, string eventKey,
            bool inAppRequested, bool emailRequested)
        {
            Created.Add((userId, type, eventKey, inAppRequested, emailRequested));
            return Task.CompletedTask;
        }
        public Task<IReadOnlyList<NotificationItem>> ListAsync(Guid userId, bool unreadOnly, int page, int pageSize)
            => Task.FromResult<IReadOnlyList<NotificationItem>>(Array.Empty<NotificationItem>());
        public Task<int> UnreadCountAsync(Guid userId) => Task.FromResult(0);
        public Task MarkReadAsync(Guid userId, Guid id) => Task.CompletedTask;
        public Task MarkAllReadAsync(Guid userId) => Task.CompletedTask;
    }

    // ── 脚手架 ──────────────────────────────────────────────────────────────
    private static CP6Context NewDb() => new(new DbContextOptionsBuilder<CP6Context>()
        .UseInMemoryDatabase(Guid.NewGuid().ToString())
        .ConfigureWarnings(w => w.Ignore(InMemoryEventId.TransactionIgnoredWarning)).Options);

    private sealed record Rig(PersistentWfNotifier Notifier, RecordingNotif Notif);

    private static async Task<Rig> BuildAsync(Guid user, string? prefsJson)
    {
        var db = NewDb();
        db.Sys_Users.Add(new Sys_User { Id = user, UserName = "u1", NickName = "用户一", Password = "x", Email = "u1@cp6.local" });
        if (prefsJson is not null)
            db.Wf_InboxPrefs.Add(new Wf_InboxPref { Id = Guid.NewGuid(), UserId = user, PrefsJson = prefsJson });
        await db.SaveChangesAsync();
        var notif = new RecordingNotif();
        var notifier = new PersistentWfNotifier(notif, new PrefService(db));
        return new Rig(notifier, notif);
    }

    // ── 跳过矩阵（spec §7）──────────────────────────────────────────────────
    [Fact]
    public async Task Default_NoPrefRow_EnqueuesBothRequestedChannels()
    {
        var user = Guid.NewGuid();
        var r = await BuildAsync(user, prefsJson: null);
        await r.Notifier.TodoCreatedAsync(user, Guid.NewGuid(), Guid.NewGuid(), "leave");
        Assert.Single(r.Notif.Created);
        Assert.Equal(WfNotificationType.TodoCreated, r.Notif.Created[0].Type);
        Assert.True(r.Notif.Created[0].InApp);
        Assert.True(r.Notif.Created[0].Email);
    }

    [Fact]
    public async Task InAppOff_EnqueuesEmailOnly()
    {
        var user = Guid.NewGuid();
        var r = await BuildAsync(user, """{"notify":{"todoCreated":{"inApp":false,"email":true}}}""");
        await r.Notifier.TodoCreatedAsync(user, Guid.NewGuid(), Guid.NewGuid(), "leave");
        Assert.Single(r.Notif.Created);
        Assert.False(r.Notif.Created[0].InApp);
        Assert.True(r.Notif.Created[0].Email);
    }

    [Fact]
    public async Task EmailOff_EnqueuesInAppOnly()
    {
        var user = Guid.NewGuid();
        var r = await BuildAsync(user, """{"notify":{"flowApproved":{"email":false}}}""");
        await r.Notifier.FlowApprovedAsync(user, Guid.NewGuid(), "leave");
        Assert.Single(r.Notif.Created);
        Assert.Equal(WfNotificationType.FlowApproved, r.Notif.Created[0].Type);
        Assert.True(r.Notif.Created[0].InApp);
        Assert.False(r.Notif.Created[0].Email);
    }

    [Fact]
    public async Task BothOff_SkipsEverything()
    {
        var user = Guid.NewGuid();
        var r = await BuildAsync(user, """{"notify":{"flowRejected":{"inApp":false,"email":false}}}""");
        await r.Notifier.FlowRejectedAsync(user, Guid.NewGuid(), "leave", "缺附件");
        Assert.Empty(r.Notif.Created);
    }

    [Fact]
    public async Task TypesIndependent_RejectedOff_TodoStillFull()
    {
        var user = Guid.NewGuid();
        var r = await BuildAsync(user, """{"notify":{"flowRejected":{"inApp":false,"email":false}}}""");
        await r.Notifier.TodoCreatedAsync(user, Guid.NewGuid(), Guid.NewGuid(), "leave");
        Assert.Single(r.Notif.Created);
    }

    // ── 遗留扁平数据回归（C2：旧用户已存开关不失效）──
    [Fact]
    public async Task LegacyFlat_TodoOff_SkipsAllChannels()
    {
        var user = Guid.NewGuid();
        var r = await BuildAsync(user, """{"notify":{"todo":false,"email":true}}""");
        await r.Notifier.TodoCreatedAsync(user, Guid.NewGuid(), Guid.NewGuid(), "leave");
        Assert.Empty(r.Notif.Created);
    }

    [Fact]
    public async Task LegacyFlat_GlobalEmailOff_SkipsOnlyEmail()
    {
        var user = Guid.NewGuid();
        var r = await BuildAsync(user, """{"notify":{"email":false}}""");
        await r.Notifier.FlowApprovedAsync(user, Guid.NewGuid(), "leave");
        Assert.Single(r.Notif.Created);
        Assert.False(r.Notif.Created[0].Email);
    }

    // ── 第 4 方法 BranchPrunedAsync 矩阵门控（hardening 波已合入；typeKey="branchPruned"）──
    [Fact]
    public async Task BranchPruned_Default_EnqueuesBothRequestedChannels()
    {
        var user = Guid.NewGuid();
        var r = await BuildAsync(user, prefsJson: null);
        await r.Notifier.BranchPrunedAsync(user, Guid.NewGuid(), "leave", "node2", "分支驳回");
        Assert.Single(r.Notif.Created);
        Assert.Equal(WfNotificationType.BranchPruned, r.Notif.Created[0].Type);
        Assert.True(r.Notif.Created[0].InApp);
        Assert.True(r.Notif.Created[0].Email);
    }

    [Fact]
    public async Task BranchPruned_InAppOff_EnqueuesEmailOnly()
    {
        var user = Guid.NewGuid();
        var r = await BuildAsync(user, """{"notify":{"branchPruned":{"inApp":false,"email":true}}}""");
        await r.Notifier.BranchPrunedAsync(user, Guid.NewGuid(), "leave", "node2", "分支驳回");
        Assert.Single(r.Notif.Created);
        Assert.False(r.Notif.Created[0].InApp);
        Assert.True(r.Notif.Created[0].Email);
    }

    [Fact]
    public async Task BranchPruned_EmailOff_EnqueuesInAppOnly()
    {
        var user = Guid.NewGuid();
        var r = await BuildAsync(user, """{"notify":{"branchPruned":{"email":false}}}""");
        await r.Notifier.BranchPrunedAsync(user, Guid.NewGuid(), "leave", "node2", "分支驳回");
        Assert.Single(r.Notif.Created);
        Assert.Equal(WfNotificationType.BranchPruned, r.Notif.Created[0].Type);
        Assert.True(r.Notif.Created[0].InApp);
        Assert.False(r.Notif.Created[0].Email);
    }

    [Fact]
    public async Task BranchPruned_BothOff_SkipsEverything()
    {
        var user = Guid.NewGuid();
        var r = await BuildAsync(user, """{"notify":{"branchPruned":{"inApp":false,"email":false}}}""");
        await r.Notifier.BranchPrunedAsync(user, Guid.NewGuid(), "leave", "node2", "分支驳回");
        Assert.Empty(r.Notif.Created);
    }
}
