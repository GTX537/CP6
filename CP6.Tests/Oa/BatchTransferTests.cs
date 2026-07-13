using CP6.Core.EFDbContext;
using CP6.Core.Services.Oa;
using CP6.Core.Services.Wf;
using CP6.Entity.DomainModels.Sys;
using CP6.Entity.DomainModels.Wf;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using System.Text.Json;
using Xunit;

namespace CP6.Tests.Oa;

public class BatchTransferTests
{
    // 脚手架照 InboxServiceTests：InMemory + 真引擎 + 真 ForecastService
    private static CP6Context NewDb() => new(new DbContextOptionsBuilder<CP6Context>()
        .UseInMemoryDatabase(Guid.NewGuid().ToString())
        .ConfigureWarnings(w => w.Ignore(InMemoryEventId.TransactionIgnoredWarning)).Options);
    private static FlowEngine Engine(CP6Context db) => new(db, new ApproverResolver(db));
    private static IInboxService Inbox(CP6Context db) => new InboxService(db, Engine(db),
        new ForecastService(db, new ApproverResolver(db), new ApprovalStagePlanner(new ApproverResolver(db))));

    /// <summary>种 n 个实例（同一 flowKey）全部待办压给 from。返回 taskId 列表（按提交序）。</summary>
    private static async Task<List<Guid>> SeedPendingAsync(CP6Context db, Guid starter, Guid from, int n, string flowKey = "leave")
    {
        if (!await db.Sys_Users.AnyAsync(u => u.Id == starter))
            db.Sys_Users.Add(new Sys_User { Id = starter, UserName = "starter", NickName = "发起人", Password = "x" });
        if (!await db.Sys_Users.AnyAsync(u => u.Id == from))
            db.Sys_Users.Add(new Sys_User { Id = from, UserName = "from", NickName = "转出人", Password = "x" });
        if (!await db.Wf_FlowDefs.AnyAsync(d => d.FlowKey == flowKey))
            db.Wf_FlowDefs.Add(new Wf_FlowDef { Id = Guid.NewGuid(), FlowKey = flowKey, FlowName = flowKey, FormKey = flowKey,
                SchemaJson = JsonSerializer.Serialize(new FlowSchema {
                    Nodes = { new FlowNode { Id = "n1", Type = "approval", ApproverStrategy = "Specified", ApproverUserId = from },
                              new FlowNode { Id = "end", Type = "end" } },
                    Edges = { new FlowEdge { From = "n1", To = "end" } } }),
                Version = 1, Enable = true });
        await db.SaveChangesAsync();
        var ids = new List<Guid>();
        for (var i = 0; i < n; i++)
        {
            var instId = await Engine(db).SubmitAsync(flowKey, starter, "{}");
            ids.Add((await db.Wf_FlowTasks.SingleAsync(t => t.InstanceId == instId)).Id);
        }
        return ids;
    }

    private static Sys_User NewEnabledUser(Guid id, string name) =>
        new() { Id = id, UserName = name, NickName = name, Password = "x", Enable = true };

    // ── 部分成功 + 汇总（spec §7：逐条事务部分成功、失败明细）──
    [Fact]
    public async Task Batch_PartialSuccess_DirtyRowDoesNotBlockOthers()
    {
        using var db = NewDb();
        var starter = Guid.NewGuid(); var from = Guid.NewGuid();
        var to = Guid.NewGuid(); var admin = Guid.NewGuid();
        db.Sys_Users.Add(NewEnabledUser(to, "to"));
        await db.SaveChangesAsync();
        var taskIds = await SeedPendingAsync(db, starter, from, 3);

        // 弄脏中间一条：先办结（Status != Pending → TransferAsync 抛 E-WF-002）
        await Engine(db).ActAsync(taskIds[1], from, approve: true, comment: null);

        var report = await Inbox(db).BatchTransferAsync(admin, from, to, "离职移交");

        Assert.Equal(2, report.Total);          // 已办结那条不再是 Pending → 不入候选
        Assert.Equal(2, report.Succeeded);
        Assert.Empty(report.Failed);
        Assert.Equal(2, await db.Wf_FlowTasks.CountAsync(t => t.AssigneeId == to && t.Status == FlowTaskStatus.Pending));
    }

    [Fact]
    public async Task Batch_ExplicitTaskIds_DirtyRow_FailsWithDetail_ContinuesRest()
    {
        using var db = NewDb();
        var starter = Guid.NewGuid(); var from = Guid.NewGuid();
        var to = Guid.NewGuid(); var admin = Guid.NewGuid();
        db.Sys_Users.Add(NewEnabledUser(to, "to"));
        await db.SaveChangesAsync();
        var taskIds = await SeedPendingAsync(db, starter, from, 3);

        // 显式点名 3 条（重试口径：TaskIds 命中时不预筛状态，让引擎裁决）；第 2 条已办结
        await Engine(db).ActAsync(taskIds[1], from, approve: true, comment: null);
        var report = await Inbox(db).BatchTransferAsync(admin, from, to, null,
            new BatchTransferFilter(TaskIds: taskIds));

        Assert.Equal(3, report.Total);                                   // 点名 3 条全入候选
        Assert.Equal(2, report.Succeeded);                               // 循环中段失败不中断后续（D3）
        var f = Assert.Single(report.Failed);                            // 失败以明细行呈现（spec §3.2 重试同口径）
        Assert.Equal(taskIds[1], f.TaskId);
        Assert.Equal("E-WF-002", f.Error);                               // 引擎语义原样透出
        Assert.Equal("leave", f.FlowKey);
    }

    // ── 上限 500（spec §3.1 防御）──
    [Fact]
    public async Task Batch_Over500_Rejected_WithHintKey()
    {
        using var db = NewDb();
        var starter = Guid.NewGuid(); var from = Guid.NewGuid();
        var to = Guid.NewGuid();
        db.Sys_Users.Add(NewEnabledUser(to, "to"));
        await db.SaveChangesAsync();
        await SeedPendingAsync(db, starter, from, 501);

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(
            () => Inbox(db).BatchTransferAsync(Guid.NewGuid(), from, to, null));
        Assert.Equal("oa.bt.errTooMany", ex.Message);
        Assert.Equal(501, await db.Wf_FlowTasks.CountAsync(t => t.AssigneeId == from));   // 一条都没转（前置校验）
    }

    // ── from==to / to 停用 / to 不存在（跨租户同路径）──
    [Fact]
    public async Task Batch_FromEqualsTo_Rejected()
    {
        using var db = NewDb();
        var from = Guid.NewGuid();
        var ex = await Assert.ThrowsAsync<InvalidOperationException>(
            () => Inbox(db).BatchTransferAsync(Guid.NewGuid(), from, from, null));
        Assert.Equal("oa.bt.errSameUser", ex.Message);
    }

    [Fact]
    public async Task Batch_TargetDisabled_Rejected()
    {
        using var db = NewDb();
        var to = Guid.NewGuid();
        db.Sys_Users.Add(new Sys_User { Id = to, UserName = "to", NickName = "停用者", Password = "x", Enable = false });
        await db.SaveChangesAsync();
        var ex = await Assert.ThrowsAsync<InvalidOperationException>(
            () => Inbox(db).BatchTransferAsync(Guid.NewGuid(), Guid.NewGuid(), to, null));
        Assert.Equal("oa.bt.errTargetInvalid", ex.Message);
    }

    [Fact]
    public async Task Batch_TargetCrossTenant_Rejected_SamePathAsMissing()
    {
        using var db = NewDb();
        var to = Guid.NewGuid();
        // 显式设他租户（StampTenant 只盖 TenantId==Guid.Empty 的新增行，CP6Context.cs:2211-2213）
        db.Sys_Users.Add(new Sys_User { Id = to, UserName = "alien", NickName = "他租户", Password = "x",
            Enable = true, TenantId = Guid.NewGuid() });
        await db.SaveChangesAsync();
        // 全局查询过滤器（TenantId==CurrentTenantId）查不到 → 与不存在同路径拒绝
        var ex = await Assert.ThrowsAsync<InvalidOperationException>(
            () => Inbox(db).BatchTransferAsync(Guid.NewGuid(), Guid.NewGuid(), to, null));
        Assert.Equal("oa.bt.errTargetInvalid", ex.Message);
    }

    // ── 审计与引擎语义（spec §7：审计行齐全、TransferAsync 语义不变回归）──
    [Fact]
    public async Task Batch_WritesEngineAudit_HistoryAndFormToPair_PerTask()
    {
        using var db = NewDb();
        var starter = Guid.NewGuid(); var from = Guid.NewGuid();
        var to = Guid.NewGuid(); var admin = Guid.NewGuid();
        db.Sys_Users.Add(NewEnabledUser(to, "to"));
        await db.SaveChangesAsync();
        await SeedPendingAsync(db, starter, from, 2);

        await Inbox(db).BatchTransferAsync(admin, from, to, "移交");

        // Wf_FlowHistory：每条一行 action=transfer，ActorId=操作者（admin）
        Assert.Equal(2, await db.Wf_FlowHistories.CountAsync(h => h.Action == "transfer" && h.ActorId == admin));
        // Wf_FlowFormTo 双行：原行 Transferred(ActualHandlerId=from) + 新 Pending 行(ExpectedHandlerId=to)
        Assert.Equal(2, await db.Wf_FlowFormTos.CountAsync(f => f.Status == FlowFormToStatus.Transferred && f.ActualHandlerId == from));
        Assert.Equal(2, await db.Wf_FlowFormTos.CountAsync(f => f.Status == FlowFormToStatus.Pending && f.ExpectedHandlerId == to));
    }

    // ── filter 收窄 + preview ──
    [Fact]
    public async Task Batch_FilterByFlowKey_NarrowsCandidates()
    {
        using var db = NewDb();
        var starter = Guid.NewGuid(); var from = Guid.NewGuid();
        var to = Guid.NewGuid();
        db.Sys_Users.Add(NewEnabledUser(to, "to"));
        await db.SaveChangesAsync();
        await SeedPendingAsync(db, starter, from, 2, flowKey: "leave");
        await SeedPendingAsync(db, starter, from, 1, flowKey: "expense");

        var report = await Inbox(db).BatchTransferAsync(Guid.NewGuid(), from, to, null,
            new BatchTransferFilter(FlowKey: "leave"));

        Assert.Equal(2, report.Total);
        Assert.Equal(1, await db.Wf_FlowTasks.CountAsync(t => t.AssigneeId == from && t.Status == FlowTaskStatus.Pending)); // expense 留下
    }

    [Fact]
    public async Task Preview_ReturnsTotalAndSample_WithoutTransferring()
    {
        using var db = NewDb();
        var starter = Guid.NewGuid(); var from = Guid.NewGuid();
        await SeedPendingAsync(db, starter, from, 12);

        var preview = await Inbox(db).BatchTransferPreviewAsync(from);

        Assert.Equal(12, preview.Total);
        Assert.Equal(10, preview.Sample.Count);                                            // 抽样前 10
        Assert.Equal(12, await db.Wf_FlowTasks.CountAsync(t => t.AssigneeId == from));     // 只读
    }
}
