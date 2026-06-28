using System.Text.Json;
using CP6.Core.EFDbContext;
using CP6.Core.Services.Wf;
using CP6.Entity.DomainModels.Wf;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace CP6.Tests.Wf;

public class SerialSignTests
{
    [Fact]
    public void NewColumns_DefaultToZeroOrNull()
    {
        var task = new Wf_FlowTask();
        Assert.Equal(0, task.StageIndex);
        Assert.Equal(0, task.StageRound);
        var token = new Wf_FlowToken();
        Assert.Null(token.StagePlanJson);
        var formto = new Wf_FlowFormTo();
        Assert.Null(formto.StageIndex);
        Assert.Null(formto.StageRound);
    }

    [Fact]
    public void Constants_AndSentBackStatus_Exist()
    {
        Assert.Equal("fixed", ApprovalStageKinds.Fixed);
        Assert.Equal("managerChain", ApprovalStageKinds.ManagerChain);
        Assert.Equal("all", CountersignModes.All);
        Assert.Equal(7, FlowFormToStatus.SentBack);
    }

    [Fact]
    public void FlowNode_Stages_DefaultsNull()
    {
        Assert.Null(new FlowNode().Stages);
        var stage = new ApprovalStage { Kind = "fixed", ApproverStrategy = "Specified", Countersign = "all" };
        Assert.Equal("fixed", stage.Kind);
    }

    private static CP6Context Db() => new(new DbContextOptionsBuilder<CP6Context>()
        .UseInMemoryDatabase(Guid.NewGuid().ToString())
        .ConfigureWarnings(w => w.Ignore(InMemoryEventId.TransactionIgnoredWarning)).Options);
    private static FlowEngine Eng(CP6Context db) => new(db, new ApproverResolver(db));

    private static async Task SeedSerialFixed3Async(CP6Context db, Guid s1, Guid s2, Guid s3)
    {
        var schema = new FlowSchema { Start = "ap", Nodes =
        {
            new FlowNode { Id = "ap", Type = "approval", Stages = new()
            {
                new ApprovalStage { Kind = "fixed", ApproverStrategy = "Specified", ApproverUserId = s1, Countersign = "all", Name = "档1" },
                new ApprovalStage { Kind = "fixed", ApproverStrategy = "Specified", ApproverUserId = s2, Countersign = "all", Name = "档2" },
                new ApprovalStage { Kind = "fixed", ApproverStrategy = "Specified", ApproverUserId = s3, Countersign = "all", Name = "档3" },
            }},
            new FlowNode { Id = "end", Type = "end" },
        }, Edges = { new FlowEdge { From = "ap", To = "end" } } };
        db.Wf_FlowDefs.Add(new Wf_FlowDef { Id = Guid.NewGuid(), FlowKey = "serial3", FlowName = "三档串簽",
            FormKey = "t", SchemaJson = JsonSerializer.Serialize(schema), Version = 1, Enable = true });
        await db.SaveChangesAsync();
    }

    [Fact]
    public async Task SerialEnter_BuildsOnlyStage0Task_FreezesPlan()
    {
        using var db = Db();
        Guid s1 = Guid.NewGuid(), s2 = Guid.NewGuid(), s3 = Guid.NewGuid();
        await SeedSerialFixed3Async(db, s1, s2, s3);
        var instId = await Eng(db).SubmitAsync("serial3", Guid.NewGuid(), "{}");

        var pending = await db.Wf_FlowTasks.Where(t => t.Status == FlowTaskStatus.Pending).ToListAsync();
        Assert.Single(pending);
        Assert.Equal(s1, pending[0].AssigneeId);
        Assert.Equal(0, pending[0].StageIndex);
        Assert.Equal(0, pending[0].StageRound);

        var tok = await db.Wf_FlowTokens.SingleAsync(t => t.Status == FlowTokenStatus.Active);
        Assert.False(string.IsNullOrEmpty(tok.StagePlanJson));
    }

    [Fact]
    public async Task SerialFixed3_AdvancesStageByStage_ThenEnds()
    {
        using var db = Db();
        Guid s1 = Guid.NewGuid(), s2 = Guid.NewGuid(), s3 = Guid.NewGuid();
        await SeedSerialFixed3Async(db, s1, s2, s3);
        var instId = await Eng(db).SubmitAsync("serial3", Guid.NewGuid(), "{}");

        var t0 = await db.Wf_FlowTasks.SingleAsync(t => t.StageIndex == 0 && t.Status == FlowTaskStatus.Pending);
        await Eng(db).ActAsync(t0.Id, s1, true);
        var t1 = await db.Wf_FlowTasks.SingleAsync(t => t.StageIndex == 1 && t.Status == FlowTaskStatus.Pending);
        Assert.Equal(s2, t1.AssigneeId);
        Assert.Equal(FlowInstanceStatus.Running, (await db.Wf_FlowInstances.SingleAsync(i => i.Id == instId)).Status);

        await Eng(db).ActAsync(t1.Id, s2, true);
        var t2 = await db.Wf_FlowTasks.SingleAsync(t => t.StageIndex == 2 && t.Status == FlowTaskStatus.Pending);
        Assert.Equal(s3, t2.AssigneeId);

        await Eng(db).ActAsync(t2.Id, s3, true);
        var inst = await db.Wf_FlowInstances.SingleAsync(i => i.Id == instId);
        Assert.Equal(FlowInstanceStatus.Approved, inst.Status);
        Assert.Equal(0, await db.Wf_FlowTokens.CountAsync(t => t.InstanceId == instId && t.Status == FlowTokenStatus.Active));
    }

    [Fact]
    public async Task SerialStage_WithParallelCountersign_Any_PassesOnFirstApprove()
    {
        using var db = Db();
        Guid x = Guid.NewGuid(), y = Guid.NewGuid();
        var schema = new FlowSchema { Start = "ap", Nodes =
        {
            new FlowNode { Id = "ap", Type = "approval", Stages = new()
            {
                new ApprovalStage { Kind = "fixed", ApproverStrategy = "Role", ApproverRoleId = 5, Countersign = "any" },
            }},
            new FlowNode { Id = "end", Type = "end" },
        }, Edges = { new FlowEdge { From = "ap", To = "end" } } };
        db.Sys_Users.Add(new() { Id = x, UserName = "x", Enable = true, RoleId = 5, Password = "x" });
        db.Sys_Users.Add(new() { Id = y, UserName = "y", Enable = true, RoleId = 5, Password = "x" });
        db.Wf_FlowDefs.Add(new() { Id = Guid.NewGuid(), FlowKey = "csany", FlowName = "x", FormKey = "t",
            SchemaJson = JsonSerializer.Serialize(schema), Version = 1, Enable = true });
        await db.SaveChangesAsync();

        var instId = await Eng(db).SubmitAsync("csany", Guid.NewGuid(), "{}");
        Assert.Equal(2, await db.Wf_FlowTasks.CountAsync(t => t.StageIndex == 0 && t.Status == FlowTaskStatus.Pending));
        var one = await db.Wf_FlowTasks.FirstAsync(t => t.Status == FlowTaskStatus.Pending);
        await Eng(db).ActAsync(one.Id, one.AssigneeId, true);
        Assert.Equal(FlowInstanceStatus.Approved, (await db.Wf_FlowInstances.SingleAsync(i => i.Id == instId)).Status);
    }

    [Fact]
    public async Task ManagerChainPlan_FrozenAtEntry_OrgChangeDoesNotAddStage()
    {
        using var db = Db();
        Guid u0 = Guid.NewGuid(), u1 = Guid.NewGuid();
        db.Sys_Users.Add(new() { Id = u1, UserName = "u1", Enable = true, ManagerId = null, Password = "x" });
        db.Sys_Users.Add(new() { Id = u0, UserName = "u0", Enable = true, ManagerId = u1, Password = "x" });
        var schema = new FlowSchema { Start = "ap", Nodes =
        {
            new FlowNode { Id = "ap", Type = "approval", Stages = new()
            {
                new ApprovalStage { Kind = "managerChain", MaxLevels = 5, Countersign = "all" },
            }},
            new FlowNode { Id = "end", Type = "end" },
        }, Edges = { new FlowEdge { From = "ap", To = "end" } } };
        db.Wf_FlowDefs.Add(new() { Id = Guid.NewGuid(), FlowKey = "mc", FlowName = "x", FormKey = "t",
            SchemaJson = JsonSerializer.Serialize(schema), Version = 1, Enable = true });
        await db.SaveChangesAsync();

        var instId = await Eng(db).SubmitAsync("mc", u0, "{}");   // 进节点冻结:链 1 级 → plan 1 档
        var t0 = await db.Wf_FlowTasks.SingleAsync(t => t.StageIndex == 0 && t.Status == FlowTaskStatus.Pending);

        // 冻结后改组织:给 u1 加上级 u2(链变 2 级)
        var u2 = Guid.NewGuid();
        db.Sys_Users.Add(new() { Id = u2, UserName = "u2", Enable = true, ManagerId = null, Password = "x" });
        var u1row = await db.Sys_Users.SingleAsync(u => u.Id == u1);
        u1row.ManagerId = u2;
        await db.SaveChangesAsync();

        // 档0(u1)过 → 冻结 plan 仅 1 档 → 不因链增长出档1 → AdvanceToken → end → Approved
        await Eng(db).ActAsync(t0.Id, u1, true);
        var inst = await db.Wf_FlowInstances.SingleAsync(i => i.Id == instId);
        Assert.Equal(FlowInstanceStatus.Approved, inst.Status);
        Assert.Equal(0, await db.Wf_FlowTasks.CountAsync(t => t.StageIndex == 1));   // 无第 1 档(防漂移)
    }

    [Fact]
    public async Task SerialStage_NoApprover_Suspends_NotSkip()
    {
        using var db = Db();
        var schema = new FlowSchema { Start = "ap", Nodes =
        {
            new FlowNode { Id = "ap", Type = "approval", Stages = new()
            {
                new ApprovalStage { Kind = "fixed", ApproverStrategy = "Role", ApproverRoleId = 999, Countersign = "all" },
            }},
            new FlowNode { Id = "end", Type = "end" },
        }, Edges = { new FlowEdge { From = "ap", To = "end" } } };
        db.Wf_FlowDefs.Add(new Wf_FlowDef { Id = Guid.NewGuid(), FlowKey = "noappr", FlowName = "x", FormKey = "t",
            SchemaJson = JsonSerializer.Serialize(schema), Version = 1, Enable = true });
        await db.SaveChangesAsync();

        var instId = await Eng(db).SubmitAsync("noappr", Guid.NewGuid(), "{}");
        var inst = await db.Wf_FlowInstances.SingleAsync(i => i.Id == instId);
        Assert.Equal(FlowInstanceStatus.Suspended, inst.Status);
        Assert.Equal(0, await db.Wf_FlowTasks.CountAsync(t => t.Status == FlowTaskStatus.Pending));
    }
}
