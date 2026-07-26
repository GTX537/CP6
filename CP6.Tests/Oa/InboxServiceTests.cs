using CP6.Core.EFDbContext;
using CP6.Core.Services.Oa;
using CP6.Core.Services.Wf;
using CP6.Entity.DomainModels.Sys;
using CP6.Entity.DomainModels.Wf;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using System.Text.Json;

namespace CP6.Tests;

public class InboxServiceTests
{
    private static CP6Context NewDb() => new(new DbContextOptionsBuilder<CP6Context>()
        .UseInMemoryDatabase(Guid.NewGuid().ToString())
        .ConfigureWarnings(w => w.Ignore(InMemoryEventId.TransactionIgnoredWarning)).Options);
    private static FlowEngine Engine(CP6Context db) => new(db, new ApproverResolver(db));
    private static IForecastService Forecast(CP6Context db) => new ForecastService(db, new ApproverResolver(db), new ApprovalStagePlanner(new ApproverResolver(db)));
    private static IInboxService Inbox(CP6Context db) => new InboxService(db, Engine(db), Forecast(db));

    // 流程：n1(approver 审批，CC 给 ccUser) → end。
    private static async Task SeedAndSubmitAsync(CP6Context db, Guid starter, Guid approver, Guid ccUser, string key = "leave")
    {
        db.Sys_Users.AddRange(
            new Sys_User { Id = starter, UserName = "starter", NickName = "发起人李", Password = "x" },
            new Sys_User { Id = approver, UserName = "approver", NickName = "审批王", Password = "x" },
            new Sys_User { Id = ccUser, UserName = "cc", NickName = "知会赵", Password = "x" });
        db.Wf_FlowDefs.Add(new Wf_FlowDef { Id = Guid.NewGuid(), FlowKey = key, FlowName = "请假单", FormKey = key,
            SchemaJson = JsonSerializer.Serialize(new FlowSchema {
                Nodes = {
                    new FlowNode { Id = "n1", Name = "主管审批", Type = "approval", ApproverStrategy = "Specified",
                                   ApproverUserId = approver, CcUsers = new() { ccUser } },
                    new FlowNode { Id = "end", Type = "end" } },
                Edges = { new FlowEdge { From = "n1", To = "end" } } }),
            Version = 1, Enable = true });
        await db.SaveChangesAsync();
        await Engine(db).SubmitAsync(key, starter, "{}");
    }

    [Fact]
    public async Task Pending_ReturnsMyTodos_WithStarterName_AndUnread()
    {
        using var db = NewDb();
        var starter = Guid.NewGuid(); var approver = Guid.NewGuid(); var cc = Guid.NewGuid();
        await SeedAndSubmitAsync(db, starter, approver, cc);

        var pend = await Inbox(db).PendingAsync(approver);
        var item = Assert.Single(pend);
        Assert.Equal("请假单", item.FlowName);
        Assert.Equal("发起人李", item.StarterName);
        Assert.False(item.IsRead);
        Assert.Equal(approver, (await db.Wf_FlowTasks.SingleAsync(t => t.Id == item.TaskId)).AssigneeId);
    }

    [Fact]
    public async Task PendingCc_ReturnsCcRecipientItems()
    {
        using var db = NewDb();
        var starter = Guid.NewGuid(); var approver = Guid.NewGuid(); var cc = Guid.NewGuid();
        await SeedAndSubmitAsync(db, starter, approver, cc);

        var ccItems = await Inbox(db).PendingCcAsync(cc);
        var item = Assert.Single(ccItems);
        Assert.Equal("请假单", item.FlowName);
        Assert.Equal("n1", item.AtNodeId);
        Assert.False(item.IsRead);
    }

    [Fact]
    public async Task MarkTaskRead_Idempotent_AndOwnerOnly()
    {
        using var db = NewDb();
        var starter = Guid.NewGuid(); var approver = Guid.NewGuid(); var cc = Guid.NewGuid();
        await SeedAndSubmitAsync(db, starter, approver, cc);
        var taskId = (await db.Wf_FlowTasks.SingleAsync(t => t.Status == FlowTaskStatus.Pending)).Id;

        await Inbox(db).MarkTaskReadAsync(approver, taskId);
        var t1 = await db.Wf_FlowTasks.SingleAsync(t => t.Id == taskId);
        Assert.True(t1.IsRead);
        var firstReadAt = t1.ReadAt;

        await Inbox(db).MarkTaskReadAsync(approver, taskId);            // 幂等：不改 ReadAt
        Assert.Equal(firstReadAt, (await db.Wf_FlowTasks.SingleAsync(t => t.Id == taskId)).ReadAt);

        await Inbox(db).MarkTaskReadAsync(Guid.NewGuid(), taskId);      // 非本人：no-op
        Assert.True((await db.Wf_FlowTasks.SingleAsync(t => t.Id == taskId)).IsRead);
    }

    [Fact]
    public async Task MarkCcRead_SetsIsRead()
    {
        using var db = NewDb();
        var starter = Guid.NewGuid(); var approver = Guid.NewGuid(); var cc = Guid.NewGuid();
        await SeedAndSubmitAsync(db, starter, approver, cc);
        var ccId = (await db.Wf_FlowCcs.SingleAsync(c => c.RecipientId == cc)).Id;

        await Inbox(db).MarkCcReadAsync(cc, ccId);
        Assert.True((await db.Wf_FlowCcs.SingleAsync(c => c.Id == ccId)).IsRead);
    }

    [Fact]
    public async Task Running_ReturnsMyStarted_WithCurrentHandlers()
    {
        using var db = NewDb();
        var starter = Guid.NewGuid(); var approver = Guid.NewGuid(); var cc = Guid.NewGuid();
        await SeedAndSubmitAsync(db, starter, approver, cc);

        var running = await Inbox(db).RunningAsync(starter);
        var item = Assert.Single(running);
        Assert.Equal("请假单", item.FlowName);
        Assert.Equal(FlowInstanceStatus.Running, item.Status);
        Assert.Contains("审批王", item.CurrentHandlers);     // 当前关卡应处理人 = 待签履历 ExpectedHandler
    }

    [Fact]
    public async Task Done_Mine_ReturnsHandledByMe()
    {
        using var db = NewDb();
        var starter = Guid.NewGuid(); var approver = Guid.NewGuid(); var cc = Guid.NewGuid();
        await SeedAndSubmitAsync(db, starter, approver, cc);
        var taskId = (await db.Wf_FlowTasks.SingleAsync(t => t.Status == FlowTaskStatus.Pending)).Id;
        await Engine(db).ActAsync(taskId, approver, approve: true, "OK");   // 办结 → 履历 Approved + 实例 Approved

        var done = await Inbox(db).DoneAsync(approver, null, null, "mine");
        var item = Assert.Single(done);
        Assert.Equal(FlowFormToStatus.Approved, item.FormToStatus);
        Assert.Equal(FlowInstanceStatus.Approved, item.InstanceStatus);
        Assert.Equal("发起人李", item.StarterName);
    }

    [Fact]
    public async Task Done_Cc_ReturnsCcRecipientItems()
    {
        using var db = NewDb();
        var starter = Guid.NewGuid(); var approver = Guid.NewGuid(); var cc = Guid.NewGuid();
        await SeedAndSubmitAsync(db, starter, approver, cc);

        var done = await Inbox(db).DoneAsync(cc, null, null, "cc");
        var item = Assert.Single(done);
        Assert.Equal("请假单", item.FlowName);
    }

    [Fact]
    public async Task ActBatch_PartialFailure_ReportsPerItem()
    {
        using var db = NewDb();
        var starter = Guid.NewGuid(); var approver = Guid.NewGuid(); var cc = Guid.NewGuid();
        await SeedAndSubmitAsync(db, starter, approver, cc);
        var validTaskId = (await db.Wf_FlowTasks.SingleAsync(t => t.Status == FlowTaskStatus.Pending)).Id;
        var bogus = Guid.NewGuid();

        var res = await Inbox(db).ActBatchAsync(approver, new[] { validTaskId, bogus }, approve: true, "批量同意");

        Assert.Equal(2, res.Count);
        Assert.True(res.Single(r => r.TaskId == validTaskId).Ok);
        var bad = res.Single(r => r.TaskId == bogus);
        Assert.False(bad.Ok);
        Assert.Equal("E-WF-004", bad.Error);
        Assert.Equal(FlowTaskStatus.Approved, (await db.Wf_FlowTasks.SingleAsync(t => t.Id == validTaskId)).Status);
    }

    [Fact]
    public async Task Detail_BuildsTimeline_AndForecast()
    {
        using var db = NewDb();
        var starter = Guid.NewGuid(); var approver = Guid.NewGuid(); var cc = Guid.NewGuid();
        await SeedAndSubmitAsync(db, starter, approver, cc);
        var instId = (await db.Wf_FlowInstances.SingleAsync()).Id;

        var detail = await Inbox(db).DetailAsync(approver, approver, instId);
        Assert.NotNull(detail);
        Assert.Equal("请假单", detail!.Instance.FlowName);
        var cur = Assert.Single(detail.Timeline.Where(r => r.Status == FlowFormToStatus.Pending));
        Assert.Equal("n1", cur.NodeId);
        Assert.Equal("审批王", cur.ExpectedHandlerName);          // 应处理人名解析
        Assert.NotEmpty(detail.Forecast);                         // 预计段含 end（Running 才算）
        Assert.Contains(detail.Cc, c => c.RecipientName == "知会赵");
    }

    [Fact]
    public async Task Detail_rejects_unrelated_user_without_returning_a_dto()
    {
        using var db = NewDb();
        var starter = Guid.NewGuid(); var approver = Guid.NewGuid(); var cc = Guid.NewGuid();
        await SeedAndSubmitAsync(db, starter, approver, cc);
        var instId = (await db.Wf_FlowInstances.SingleAsync()).Id;

        var unrelated = Guid.NewGuid();
        var error = await Assert.ThrowsAsync<UnauthorizedAccessException>(() =>
            Inbox(db).DetailAsync(unrelated, unrelated, instId));

        Assert.Equal("E-WF-043", error.Message);
    }

    [Fact]
    public async Task Stats_CountsPendingAndRunning()
    {
        using var db = NewDb();
        var starter = Guid.NewGuid(); var approver = Guid.NewGuid(); var cc = Guid.NewGuid();
        await SeedAndSubmitAsync(db, starter, approver, cc);

        Assert.Equal(1, (await Inbox(db).StatsAsync(approver)).PendingCount);   // 审批人有 1 待办
        Assert.Equal(1, (await Inbox(db).StatsAsync(starter)).RunningCount);    // 发起人有 1 在途
    }
}
