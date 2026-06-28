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
