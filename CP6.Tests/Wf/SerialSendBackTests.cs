using System.Text.Json;
using CP6.Core.EFDbContext;
using CP6.Core.Services.Wf;
using CP6.Entity.DomainModels.Wf;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace CP6.Tests.Wf;

public class SerialSendBackTests
{
    private static CP6Context Db() => new(new DbContextOptionsBuilder<CP6Context>()
        .UseInMemoryDatabase(Guid.NewGuid().ToString())
        .ConfigureWarnings(w => w.Ignore(InMemoryEventId.TransactionIgnoredWarning)).Options);
    private static FlowEngine Eng(CP6Context db) => new(db, new ApproverResolver(db));

    private static async Task SeedLinear3(CP6Context db, Guid a, Guid b, Guid c)
    {
        var schema = new FlowSchema { Start = "n1", Nodes =
        {
            new FlowNode { Id = "n1", Type = "approval", ApproverStrategy = "Specified", ApproverUserId = a },
            new FlowNode { Id = "n2", Type = "approval", ApproverStrategy = "Specified", ApproverUserId = b },
            new FlowNode { Id = "n3", Type = "approval", ApproverStrategy = "Specified", ApproverUserId = c },
            new FlowNode { Id = "end", Type = "end" },
        }, Edges = { new(){From="n1",To="n2"}, new(){From="n2",To="n3"}, new(){From="n3",To="end"} } };
        db.Wf_FlowDefs.Add(new() { Id = Guid.NewGuid(), FlowKey = "lin3", FlowName = "x", FormKey = "t",
            SchemaJson = JsonSerializer.Serialize(schema), Version = 1, Enable = true });
        await db.SaveChangesAsync();
    }

    [Fact]
    public async Task SendBackNode_ToUpstream_Works()
    {
        using var db = Db();
        Guid a = Guid.NewGuid(), b = Guid.NewGuid(), c = Guid.NewGuid();
        await SeedLinear3(db, a, b, c);
        var instId = await Eng(db).SubmitAsync("lin3", Guid.NewGuid(), "{}");
        var t1 = await db.Wf_FlowTasks.SingleAsync(t => t.NodeId == "n1");
        await Eng(db).ActAsync(t1.Id, a, true);
        var t2 = await db.Wf_FlowTasks.SingleAsync(t => t.NodeId == "n2" && t.Status == FlowTaskStatus.Pending);

        await Eng(db).SendBackAsync(t2.Id, b, new SendBackTarget("node", "n1"));
        Assert.Equal("n1", (await db.Wf_FlowInstances.SingleAsync(i => i.Id == instId)).CurrentNode);
    }

    [Fact]
    public async Task SendBackNode_ToDownstreamOrSelf_Throws_E_WF_012()
    {
        using var db = Db();
        Guid a = Guid.NewGuid(), b = Guid.NewGuid(), c = Guid.NewGuid();
        await SeedLinear3(db, a, b, c);
        await Eng(db).SubmitAsync("lin3", Guid.NewGuid(), "{}");
        var t1 = await db.Wf_FlowTasks.SingleAsync(t => t.NodeId == "n1");
        await Eng(db).ActAsync(t1.Id, a, true);
        var t2 = await db.Wf_FlowTasks.SingleAsync(t => t.NodeId == "n2" && t.Status == FlowTaskStatus.Pending);

        var e1 = await Assert.ThrowsAsync<InvalidOperationException>(() => Eng(db).SendBackAsync(t2.Id, b, new SendBackTarget("node", "n3")));
        Assert.Contains("E-WF-012", e1.Message);
        var e2 = await Assert.ThrowsAsync<InvalidOperationException>(() => Eng(db).SendBackAsync(t2.Id, b, new SendBackTarget("node", "n2")));
        Assert.Contains("E-WF-012", e2.Message);
    }

    private static async Task SeedSerial3(CP6Context db, Guid s1, Guid s2, Guid s3)
    {
        var schema = new FlowSchema { Start = "ap", Nodes =
        {
            new FlowNode { Id = "ap", Type = "approval", Stages = new()
            {
                new ApprovalStage { Kind="fixed", ApproverStrategy="Specified", ApproverUserId=s1, Countersign="all", Name="档1" },
                new ApprovalStage { Kind="fixed", ApproverStrategy="Specified", ApproverUserId=s2, Countersign="all", Name="档2" },
                new ApprovalStage { Kind="fixed", ApproverStrategy="Specified", ApproverUserId=s3, Countersign="all", Name="档3" },
            }},
            new FlowNode { Id = "end", Type = "end" },
        }, Edges = { new(){From="ap",To="end"} } };
        db.Wf_FlowDefs.Add(new() { Id=Guid.NewGuid(), FlowKey="ser3", FlowName="x", FormKey="t",
            SchemaJson=JsonSerializer.Serialize(schema), Version=1, Enable=true });
        await db.SaveChangesAsync();
    }

    [Fact]
    public async Task PrevStage_RepeatedRounds_TallyIsolated()
    {
        using var db = Db();
        Guid s1=Guid.NewGuid(), s2=Guid.NewGuid(), s3=Guid.NewGuid();
        await SeedSerial3(db, s1, s2, s3);
        var instId = await Eng(db).SubmitAsync("ser3", Guid.NewGuid(), "{}");

        var t0 = await db.Wf_FlowTasks.SingleAsync(t => t.StageIndex==0 && t.Status==FlowTaskStatus.Pending);
        await Eng(db).ActAsync(t0.Id, s1, true);
        var t1 = await db.Wf_FlowTasks.SingleAsync(t => t.StageIndex==1 && t.Status==FlowTaskStatus.Pending);
        await Eng(db).ActAsync(t1.Id, s2, true);
        var t2 = await db.Wf_FlowTasks.SingleAsync(t => t.StageIndex==2 && t.Status==FlowTaskStatus.Pending);

        // 档3 退回上一档(档2)→ 档2 第2轮(StageRound=1)
        await Eng(db).SendBackAsync(t2.Id, s3, new SendBackTarget("prevStage"), "再核");
        Assert.Equal(FlowTaskStatus.Cancelled, (await db.Wf_FlowTasks.SingleAsync(t => t.Id==t2.Id)).Status);
        var t1r1 = await db.Wf_FlowTasks.SingleAsync(t => t.StageIndex==1 && t.Status==FlowTaskStatus.Pending);
        Assert.Equal(1, t1r1.StageRound);
        Assert.NotEqual(t1.Id, t1r1.Id);

        await Eng(db).ActAsync(t1r1.Id, s2, true);
        var t2b = await db.Wf_FlowTasks.SingleAsync(t => t.StageIndex==2 && t.Status==FlowTaskStatus.Pending);
        Assert.Equal(FlowInstanceStatus.Running, (await db.Wf_FlowInstances.SingleAsync(i=>i.Id==instId)).Status);

        await Eng(db).SendBackAsync(t2b.Id, s3, new SendBackTarget("prevStage"));
        var t1r2 = await db.Wf_FlowTasks.SingleAsync(t => t.StageIndex==1 && t.Status==FlowTaskStatus.Pending);
        Assert.Equal(2, t1r2.StageRound);
        await Eng(db).ActAsync(t1r2.Id, s2, true);
        var t2c = await db.Wf_FlowTasks.SingleAsync(t => t.StageIndex==2 && t.Status==FlowTaskStatus.Pending);
        await Eng(db).ActAsync(t2c.Id, s3, true);
        Assert.Equal(FlowInstanceStatus.Approved, (await db.Wf_FlowInstances.SingleAsync(i=>i.Id==instId)).Status);

        // 档2 各轮 Approved 留痕(轮 0/1/2 各一)
        Assert.Equal(3, await db.Wf_FlowTasks.CountAsync(t => t.StageIndex==1 && t.Status==FlowTaskStatus.Approved));
    }

    [Fact]
    public async Task PrevStage_AtStage0_Throws_E_WF_012()
    {
        using var db = Db();
        Guid s1=Guid.NewGuid(), s2=Guid.NewGuid(), s3=Guid.NewGuid();
        await SeedSerial3(db, s1, s2, s3);
        await Eng(db).SubmitAsync("ser3", Guid.NewGuid(), "{}");
        var t0 = await db.Wf_FlowTasks.SingleAsync(t => t.StageIndex==0 && t.Status==FlowTaskStatus.Pending);
        var e = await Assert.ThrowsAsync<InvalidOperationException>(() => Eng(db).SendBackAsync(t0.Id, s1, new SendBackTarget("prevStage")));
        Assert.Contains("E-WF-012", e.Message);
    }

    [Fact]
    public async Task SendBackStarter_ReturnsToDraft_TerminatesTokens()
    {
        using var db = Db();
        Guid s1=Guid.NewGuid(), s2=Guid.NewGuid(), s3=Guid.NewGuid();
        await SeedSerial3(db, s1, s2, s3);
        var starter = Guid.NewGuid();
        var instId = await Eng(db).SubmitAsync("ser3", starter, "{}");
        var t0 = await db.Wf_FlowTasks.SingleAsync(t => t.StageIndex==0 && t.Status==FlowTaskStatus.Pending);

        await Eng(db).SendBackAsync(t0.Id, s1, new SendBackTarget("starter"), "请补充");
        var inst = await db.Wf_FlowInstances.SingleAsync(i => i.Id==instId);
        Assert.Equal(FlowInstanceStatus.Draft, inst.Status);
        Assert.Equal(0, await db.Wf_FlowTokens.CountAsync(t => t.InstanceId==instId && t.Status==FlowTokenStatus.Active));
        Assert.Equal(0, await db.Wf_FlowFormTos.CountAsync(f => f.InstanceId==instId && f.Status==FlowFormToStatus.Pending));

        await Eng(db).StartDraftAsync(instId, starter);
        Assert.Equal(FlowInstanceStatus.Running, (await db.Wf_FlowInstances.SingleAsync(i=>i.Id==instId)).Status);
        Assert.Equal(1, await db.Wf_FlowTasks.CountAsync(t => t.StageIndex==0 && t.Status==FlowTaskStatus.Pending && t.AssigneeId==s1));
    }
}
