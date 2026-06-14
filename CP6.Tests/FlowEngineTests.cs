using System.Text.Json;
using CP6.Core.EFDbContext;
using CP6.Core.Services.Wf;
using CP6.Entity.DomainModels.Wf;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace CP6.Tests;

/// <summary>OA 章03 ★ 流程引擎状态机（C-3）。提交/办理/幂等/会签三规则/条件分支/缺位挂起。</summary>
public class FlowEngineTests
{
    private static CP6Context NewDb() => new(new DbContextOptionsBuilder<CP6Context>()
        .UseInMemoryDatabase(Guid.NewGuid().ToString())
        .ConfigureWarnings(w => w.Ignore(InMemoryEventId.TransactionIgnoredWarning))
        .Options);

    private static string Json(FlowSchema s) => JsonSerializer.Serialize(s);

    private static async Task SeedFlowAsync(CP6Context db, string flowKey, FlowSchema schema)
    {
        db.Wf_FlowDefs.Add(new Wf_FlowDef
        {
            Id = Guid.NewGuid(), FlowKey = flowKey, FlowName = flowKey, FormKey = "leave",
            SchemaJson = Json(schema), Version = 1, Enable = true,
        });
        await db.SaveChangesAsync();
    }

    private static FlowEngine Engine(CP6Context db) => new(db, new ApproverResolver(db));

    // ───────────────────── 会签三规则（纯函数）─────────────────────

    [Fact]
    public void EvaluateNodeCounts_AllAnyVeto()
    {
        Assert.Equal((true, false), FlowEngine.EvaluateNodeCounts(approved: 1, rejected: 1, total: 3, "all"));   // 任一驳回即否
        Assert.Equal((true, true), FlowEngine.EvaluateNodeCounts(1, 0, 3, "any"));                              // 任一同意即过
        Assert.Equal((false, false), FlowEngine.EvaluateNodeCounts(2, 0, 3, "all"));                            // 还没全同意 → 未决
        Assert.Equal((true, true), FlowEngine.EvaluateNodeCounts(3, 0, 3, "all"));                              // 全同意 → 过
        Assert.Equal((true, false), FlowEngine.EvaluateNodeCounts(0, 3, 3, "any"));                             // 全驳 → 否
        Assert.Equal((true, false), FlowEngine.EvaluateNodeCounts(2, 1, 3, "veto"));                            // veto 任一反对即死
    }

    // ───────────────────── 状态机全链路 ─────────────────────

    [Fact]
    public async Task Submit_EntersFirstNode_CreatesTask()
    {
        using var db = NewDb();
        var approver = Guid.NewGuid();
        await SeedFlowAsync(db, "leave", new FlowSchema
        {
            Nodes =
            {
                new FlowNode { Id = "n1", Type = "approval", ApproverStrategy = "Specified", ApproverUserId = approver },
                new FlowNode { Id = "end", Type = "end" },
            },
            Edges = { new FlowEdge { From = "n1", To = "end" } },
        });

        var instId = await Engine(db).SubmitAsync("leave", Guid.NewGuid(), """{"days":2}""");

        var inst = await db.Wf_FlowInstances.SingleAsync();
        Assert.Equal(instId, inst.Id);
        Assert.Equal("n1", inst.CurrentNode);
        Assert.Equal(FlowInstanceStatus.Running, inst.Status);

        var task = await db.Wf_FlowTasks.SingleAsync();
        Assert.Equal(approver, task.AssigneeId);
        Assert.Equal(FlowTaskStatus.Pending, task.Status);

        Assert.Equal(1, await db.Wf_FlowHistories.CountAsync(h => h.Action == "submit"));
    }

    [Fact]
    public async Task Act_Approve_AdvancesToEnd()
    {
        using var db = NewDb();
        var approver = Guid.NewGuid();
        await SeedFlowAsync(db, "leave", new FlowSchema
        {
            Nodes =
            {
                new FlowNode { Id = "n1", Type = "approval", ApproverStrategy = "Specified", ApproverUserId = approver },
                new FlowNode { Id = "end", Type = "end" },
            },
            Edges = { new FlowEdge { From = "n1", To = "end" } },
        });
        await Engine(db).SubmitAsync("leave", Guid.NewGuid(), """{"days":2}""");

        var task = await db.Wf_FlowTasks.SingleAsync(t => t.Status == FlowTaskStatus.Pending);
        await Engine(db).ActAsync(task.Id, approver, approve: true, "ok");

        var inst = await db.Wf_FlowInstances.SingleAsync();
        Assert.Equal(FlowInstanceStatus.Approved, inst.Status);
        Assert.Equal("end", inst.CurrentNode);
    }

    [Fact]
    public async Task Act_IsIdempotent_OnAlreadyDoneTask()
    {
        using var db = NewDb();
        var approver = Guid.NewGuid();
        await SeedFlowAsync(db, "leave", new FlowSchema
        {
            Nodes =
            {
                new FlowNode { Id = "n1", Type = "approval", ApproverStrategy = "Specified", ApproverUserId = approver },
                new FlowNode { Id = "end", Type = "end" },
            },
            Edges = { new FlowEdge { From = "n1", To = "end" } },
        });
        await Engine(db).SubmitAsync("leave", Guid.NewGuid(), "{}");
        var task = await db.Wf_FlowTasks.SingleAsync(t => t.Status == FlowTaskStatus.Pending);

        await Engine(db).ActAsync(task.Id, approver, approve: true);    // 第一次：通过
        await Engine(db).ActAsync(task.Id, approver, approve: false);   // 第二次：已办 → 无效

        var inst = await db.Wf_FlowInstances.SingleAsync();
        Assert.Equal(FlowInstanceStatus.Approved, inst.Status);          // 没被第二次驳回翻转
        Assert.Equal(1, await db.Wf_FlowHistories.CountAsync(h => h.Action == "approve" || h.Action == "reject"));
    }

    [Fact]
    public async Task NextNode_PicksBranchByCondition()
    {
        FlowSchema Schema() => new()
        {
            Nodes =
            {
                new FlowNode { Id = "n1", Type = "approval", ApproverStrategy = "Specified", ApproverUserId = Guid.NewGuid() },
                new FlowNode { Id = "n2", Type = "approval", ApproverStrategy = "Specified", ApproverUserId = Guid.NewGuid() },
                new FlowNode { Id = "end", Type = "end" },
            },
            Edges =
            {
                new FlowEdge { From = "n1", To = "end", Condition = "days <= 3" },
                new FlowEdge { From = "n1", To = "n2", Condition = "days > 3" },
                new FlowEdge { From = "n2", To = "end" },
            },
        };

        // days=2 → n1 同意后直接走 end
        using (var db = NewDb())
        {
            await SeedFlowAsync(db, "leave", Schema());
            await Engine(db).SubmitAsync("leave", Guid.NewGuid(), """{"days":2}""");
            var t = await db.Wf_FlowTasks.SingleAsync(x => x.NodeId == "n1");
            await Engine(db).ActAsync(t.Id, t.AssigneeId, true);
            var inst = await db.Wf_FlowInstances.SingleAsync();
            Assert.Equal(FlowInstanceStatus.Approved, inst.Status);
            Assert.Equal("end", inst.CurrentNode);
        }

        // days=5 → n1 同意后走 n2（新建待办）
        using (var db = NewDb())
        {
            await SeedFlowAsync(db, "leave", Schema());
            await Engine(db).SubmitAsync("leave", Guid.NewGuid(), """{"days":5}""");
            var t = await db.Wf_FlowTasks.SingleAsync(x => x.NodeId == "n1");
            await Engine(db).ActAsync(t.Id, t.AssigneeId, true);
            var inst = await db.Wf_FlowInstances.SingleAsync();
            Assert.Equal(FlowInstanceStatus.Running, inst.Status);
            Assert.Equal("n2", inst.CurrentNode);
            Assert.Equal(1, await db.Wf_FlowTasks.CountAsync(x => x.NodeId == "n2" && x.Status == FlowTaskStatus.Pending));
        }
    }

    [Fact]
    public async Task Countersign_All_OneReject_RejectsInstance_CancelsOthers()
    {
        using var db = NewDb();
        // 角色 7 下两名启用用户 → all 会签
        db.Sys_Users.AddRange(
            new Sys_User { Id = Guid.NewGuid(), UserName = "a", Password = "x", RoleId = 7, Enable = true },
            new Sys_User { Id = Guid.NewGuid(), UserName = "b", Password = "x", RoleId = 7, Enable = true });
        await db.SaveChangesAsync();
        await SeedFlowAsync(db, "leave", new FlowSchema
        {
            Nodes =
            {
                new FlowNode { Id = "n1", Type = "approval", ApproverStrategy = "Role", ApproverRoleId = 7, Countersign = "all" },
                new FlowNode { Id = "end", Type = "end" },
            },
            Edges = { new FlowEdge { From = "n1", To = "end" } },
        });
        await Engine(db).SubmitAsync("leave", Guid.NewGuid(), "{}");

        var tasks = await db.Wf_FlowTasks.Where(t => t.Status == FlowTaskStatus.Pending).ToListAsync();
        Assert.Equal(2, tasks.Count);
        await Engine(db).ActAsync(tasks[0].Id, tasks[0].AssigneeId, approve: false, "驳回");   // all 下任一驳回即否

        var inst = await db.Wf_FlowInstances.SingleAsync();
        Assert.Equal(FlowInstanceStatus.Rejected, inst.Status);
        var other = await db.Wf_FlowTasks.SingleAsync(t => t.Id == tasks[1].Id);
        Assert.Equal(FlowTaskStatus.Cancelled, other.Status);   // 其余在途作废
    }

    [Fact]
    public async Task Countersign_Any_OneApprove_Passes_CancelsOthers()
    {
        using var db = NewDb();
        db.Sys_Users.AddRange(
            new Sys_User { Id = Guid.NewGuid(), UserName = "a", Password = "x", RoleId = 8, Enable = true },
            new Sys_User { Id = Guid.NewGuid(), UserName = "b", Password = "x", RoleId = 8, Enable = true });
        await db.SaveChangesAsync();
        await SeedFlowAsync(db, "leave", new FlowSchema
        {
            Nodes =
            {
                new FlowNode { Id = "n1", Type = "approval", ApproverStrategy = "Role", ApproverRoleId = 8, Countersign = "any" },
                new FlowNode { Id = "end", Type = "end" },
            },
            Edges = { new FlowEdge { From = "n1", To = "end" } },
        });
        await Engine(db).SubmitAsync("leave", Guid.NewGuid(), "{}");

        var tasks = await db.Wf_FlowTasks.Where(t => t.Status == FlowTaskStatus.Pending).ToListAsync();
        await Engine(db).ActAsync(tasks[0].Id, tasks[0].AssigneeId, approve: true);   // any 下任一同意即过

        var inst = await db.Wf_FlowInstances.SingleAsync();
        Assert.Equal(FlowInstanceStatus.Approved, inst.Status);
        var other = await db.Wf_FlowTasks.SingleAsync(t => t.Id == tasks[1].Id);
        Assert.Equal(FlowTaskStatus.Cancelled, other.Status);
    }

    [Fact]
    public async Task EnterNode_UnresolvedApprover_SuspendsNotCrash()
    {
        using var db = NewDb();
        var starter = Guid.NewGuid();
        db.Sys_Users.Add(new Sys_User { Id = starter, UserName = "u", Password = "x", Enable = true });  // 无 ManagerId
        await db.SaveChangesAsync();
        await SeedFlowAsync(db, "leave", new FlowSchema
        {
            Nodes =
            {
                new FlowNode { Id = "n1", Type = "approval", ApproverStrategy = "DirectManager", ApproverLevels = 1 },
                new FlowNode { Id = "end", Type = "end" },
            },
            Edges = { new FlowEdge { From = "n1", To = "end" } },
        });

        await Engine(db).SubmitAsync("leave", starter, "{}");   // 算不出审批人

        var inst = await db.Wf_FlowInstances.SingleAsync();
        Assert.Equal(FlowInstanceStatus.Suspended, inst.Status);
        Assert.Equal(0, await db.Wf_FlowTasks.CountAsync());
        Assert.Equal(1, await db.Wf_FlowHistories.CountAsync(h => h.Action == "suspend"));
    }
}
