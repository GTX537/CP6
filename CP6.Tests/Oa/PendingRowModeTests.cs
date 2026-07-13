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

public class PendingRowModeTests
{
    private static CP6Context NewDb() => new(new DbContextOptionsBuilder<CP6Context>()
        .UseInMemoryDatabase(Guid.NewGuid().ToString())
        .ConfigureWarnings(w => w.Ignore(InMemoryEventId.TransactionIgnoredWarning)).Options);
    private static FlowEngine Engine(CP6Context db) => new(db, new ApproverResolver(db));
    private static IInboxService Inbox(CP6Context db) => new InboxService(db, Engine(db),
        new ForecastService(db, new ApproverResolver(db), new ApprovalStagePlanner(new ApproverResolver(db))));

    /// <summary>并行三分支同审批人 → 同实例 3 个 Pending 任务（多状态多行素材）。返回 instanceId。</summary>
    private static async Task<Guid> SeedParallelSameApproverAsync(CP6Context db, Guid starter, Guid approver, string flowKey)
    {
        if (!await db.Sys_Users.AnyAsync(u => u.Id == starter))
            db.Sys_Users.Add(new Sys_User { Id = starter, UserName = "s", NickName = "发起人", Password = "x" });
        if (!await db.Sys_Users.AnyAsync(u => u.Id == approver))
            db.Sys_Users.Add(new Sys_User { Id = approver, UserName = "a", NickName = "审批人", Password = "x" });
        var schema = new FlowSchema
        {
            Nodes =
            {
                new FlowNode { Id = "split", Type = "parallelSplit" },
                new FlowNode { Id = "n1", Type = "approval", ApproverStrategy = "Specified", ApproverUserId = approver },
                new FlowNode { Id = "n2", Type = "approval", ApproverStrategy = "Specified", ApproverUserId = approver },
                new FlowNode { Id = "n3", Type = "approval", ApproverStrategy = "Specified", ApproverUserId = approver },
                new FlowNode { Id = "join", Type = "parallelJoin" },
                new FlowNode { Id = "end", Type = "end" },
            },
            Edges =
            {
                new FlowEdge { From = "split", To = "n1" },
                new FlowEdge { From = "split", To = "n2" },
                new FlowEdge { From = "split", To = "n3" },
                new FlowEdge { From = "n1", To = "join" },
                new FlowEdge { From = "n2", To = "join" },
                new FlowEdge { From = "n3", To = "join" },
                new FlowEdge { From = "join", To = "end" },
            },
        };
        db.Wf_FlowDefs.Add(new Wf_FlowDef { Id = Guid.NewGuid(), FlowKey = flowKey, FlowName = flowKey, FormKey = flowKey,
            SchemaJson = JsonSerializer.Serialize(schema), Version = 1, Enable = true });
        await db.SaveChangesAsync();
        return await Engine(db).SubmitAsync(flowKey, starter, "{}");
    }

    /// <summary>把用户全部 Pending 任务的 CreateDate 摆成确定性阶梯（排序/分页稳定）。</summary>
    private static async Task StaircaseAsync(CP6Context db, Guid approver)
    {
        // 按创建时序（非随机 Guid.Id）铺阶梯：同一 SubmitAsync 的并行任务共块，
        // 后提交实例的任务严格更晚（.NET8/Windows 高精度 DateTime.Now），保证「先提交实例 < 后提交实例」确定性。
        var tasks = await db.Wf_FlowTasks.Where(t => t.AssigneeId == approver)
            .OrderBy(t => t.CreateDate).ThenBy(t => t.Id).ToListAsync();
        var baseline = new DateTime(2026, 7, 1, 8, 0, 0);
        for (var i = 0; i < tasks.Count; i++) tasks[i].CreateDate = baseline.AddMinutes(i);
        await db.SaveChangesAsync();
    }

    [Fact]
    public async Task Merged_SameInstanceThreeTasks_CollapsesToOneRow_LatestState()
    {
        using var db = NewDb();
        var starter = Guid.NewGuid(); var approver = Guid.NewGuid();
        var instId = await SeedParallelSameApproverAsync(db, starter, approver, "par");
        await StaircaseAsync(db, approver);

        var rows = await Inbox(db).PendingAsync(approver, rowMode: "merged");

        var row = Assert.Single(rows);
        Assert.Equal(instId, row.InstanceId);
        // 显最新态：合并行 = CreateDate 最大的那个任务
        var latest = await db.Wf_FlowTasks.Where(t => t.AssigneeId == approver)
            .OrderByDescending(t => t.CreateDate).FirstAsync();
        Assert.Equal(latest.Id, row.TaskId);
    }

    [Fact]
    public async Task Expanded_SameInstanceThreeTasks_ThreeRows()
    {
        using var db = NewDb();
        var starter = Guid.NewGuid(); var approver = Guid.NewGuid();
        await SeedParallelSameApproverAsync(db, starter, approver, "par");

        var rows = await Inbox(db).PendingAsync(approver, rowMode: "expanded");
        Assert.Equal(3, rows.Count);
        Assert.Equal(3, rows.Select(r => r.TaskId).Distinct().Count());
    }

    [Fact]
    public async Task Default_IsMerged()
    {
        using var db = NewDb();
        var starter = Guid.NewGuid(); var approver = Guid.NewGuid();
        await SeedParallelSameApproverAsync(db, starter, approver, "par");

        Assert.Single(await Inbox(db).PendingAsync(approver));   // 缺省参数 = merged（spec D5）
    }

    // ── 分页正确性：同实例 3 任务跨页界（spec §7）──
    [Fact]
    public async Task Merged_Paging_GroupsBeforeSkipTake_NoInstanceStraddlesPages()
    {
        using var db = NewDb();
        var starter = Guid.NewGuid(); var approver = Guid.NewGuid();
        var instA = await SeedParallelSameApproverAsync(db, starter, approver, "parA");   // 3 任务
        var instB = await SeedParallelSameApproverAsync(db, starter, approver, "parB");   // 3 任务
        await StaircaseAsync(db, approver);                                                // A(0-2分) < B(3-5分)

        var page1 = await Inbox(db).PendingAsync(approver, "merged", page: 1, pageSize: 1);
        var page2 = await Inbox(db).PendingAsync(approver, "merged", page: 2, pageSize: 1);
        var page3 = await Inbox(db).PendingAsync(approver, "merged", page: 3, pageSize: 1);

        Assert.Equal(instB, Assert.Single(page1).InstanceId);   // 分组后按最新 CreateDate 倒序
        Assert.Equal(instA, Assert.Single(page2).InstanceId);
        Assert.Empty(page3);                                     // 若分组晚于分页会错误地出现第 3 页
    }

    [Fact]
    public async Task Expanded_Paging_SkipTakeOverTaskRows()
    {
        using var db = NewDb();
        var starter = Guid.NewGuid(); var approver = Guid.NewGuid();
        await SeedParallelSameApproverAsync(db, starter, approver, "par");
        await StaircaseAsync(db, approver);

        var page1 = await Inbox(db).PendingAsync(approver, "expanded", page: 1, pageSize: 2);
        var page2 = await Inbox(db).PendingAsync(approver, "expanded", page: 2, pageSize: 2);

        Assert.Equal(2, page1.Count);
        Assert.Single(page2);
        Assert.Equal(3, page1.Concat(page2).Select(r => r.TaskId).Distinct().Count());   // 无重复无遗漏
    }

    [Fact]
    public async Task NoPaging_ReturnsAll_BehaviourUnchanged()
    {
        using var db = NewDb();
        var starter = Guid.NewGuid(); var approver = Guid.NewGuid();
        await SeedParallelSameApproverAsync(db, starter, approver, "par");
        Assert.Equal(3, (await Inbox(db).PendingAsync(approver, "expanded")).Count);
    }
}
