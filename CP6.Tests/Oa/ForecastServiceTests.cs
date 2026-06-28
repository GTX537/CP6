using CP6.Core.EFDbContext;
using CP6.Core.Services.Oa;
using CP6.Core.Services.Wf;
using CP6.Entity.DomainModels.Sys;
using CP6.Entity.DomainModels.Wf;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using System.Text.Json;

namespace CP6.Tests;

public class ForecastServiceTests
{
    private static CP6Context NewDb() => new(new DbContextOptionsBuilder<CP6Context>()
        .UseInMemoryDatabase(Guid.NewGuid().ToString())
        .ConfigureWarnings(w => w.Ignore(InMemoryEventId.TransactionIgnoredWarning)).Options);
    private static IForecastService Forecast(CP6Context db) => new ForecastService(db, new ApproverResolver(db), new ApprovalStagePlanner(new ApproverResolver(db)));

    private static async Task SeedLinearAsync(CP6Context db, Guid u1)
    {
        db.Sys_Users.Add(new Sys_User { Id = u1, UserName = "mgr", NickName = "经理张", Password = "x" });
        db.Wf_FlowDefs.Add(new Wf_FlowDef { Id = Guid.NewGuid(), FlowKey = "f", FlowName = "请假", FormKey = "f",
            SchemaJson = JsonSerializer.Serialize(new FlowSchema {
                Nodes = {
                    new FlowNode { Id = "n1", Name = "直属主管", Type = "approval", ApproverStrategy = "Specified", ApproverUserId = u1 },
                    new FlowNode { Id = "n2", Name = "HR 审核", Type = "approval", ApproverStrategy = "Role", ApproverRoleId = 999 },
                    new FlowNode { Id = "end", Name = "结束", Type = "end" },
                },
                Edges = { new FlowEdge { From = "n1", To = "n2" }, new FlowEdge { From = "n2", To = "end" } } }),
            Version = 1, Enable = true });
        await db.SaveChangesAsync();
    }

    [Fact]
    public async Task Forecast_Linear_ResolvedAndPlaceholder()
    {
        using var db = NewDb();
        var u1 = Guid.NewGuid();
        await SeedLinearAsync(db, u1);

        var res = await Forecast(db).ForecastAsync("f", "{}", Guid.NewGuid(), fromNodeId: null);

        Assert.Equal(3, res.Steps.Count);                 // n1, n2, end
        Assert.False(res.Branched);
        var s1 = res.Steps[0];
        Assert.Equal("n1", s1.NodeId);
        Assert.True(s1.Resolved);
        Assert.Contains("经理张", s1.Approvers);           // Specified 可前解析 → 显人名
        var s2 = res.Steps[1];
        Assert.Equal("n2", s2.NodeId);
        Assert.False(s2.Resolved);                        // Role 999 无人 → 占位
        Assert.Empty(s2.Approvers);
        Assert.Equal("end", res.Steps[2].NodeId);
    }

    [Fact]
    public async Task Forecast_FromCurrentNode_SkipsDone()
    {
        using var db = NewDb();
        var u1 = Guid.NewGuid();
        await SeedLinearAsync(db, u1);

        var res = await Forecast(db).ForecastAsync("f", "{}", Guid.NewGuid(), fromNodeId: "n1");
        Assert.Equal(2, res.Steps.Count);                 // n2, end（不含已到达的 n1）
        Assert.Equal("n2", res.Steps[0].NodeId);
    }

    [Fact]
    public async Task Forecast_ExpandsSerialStages()
    {
        using var db = NewDb();
        Guid s1=Guid.NewGuid(), s2=Guid.NewGuid();
        var schema = new FlowSchema { Start="ap", Nodes =
        {
            new FlowNode { Id="ap", Type="approval", Stages = new()
            {
                new ApprovalStage { Kind="fixed", ApproverStrategy="Specified", ApproverUserId=s1, Name="档1" },
                new ApprovalStage { Kind="fixed", ApproverStrategy="Specified", ApproverUserId=s2, Name="档2" },
            }},
            new FlowNode { Id="end", Type="end" },
        }, Edges = { new(){From="ap",To="end"} } };
        db.Wf_FlowDefs.Add(new() { Id=Guid.NewGuid(), FlowKey="fs", FlowName="x", FormKey="t",
            SchemaJson=JsonSerializer.Serialize(schema), Version=1, Enable=true });
        await db.SaveChangesAsync();

        var planner = new ApprovalStagePlanner(new ApproverResolver(db));
        var svc = new ForecastService(db, new ApproverResolver(db), planner);
        var res = await svc.ForecastAsync("fs", "{}", Guid.NewGuid());

        var approvalSteps = res.Steps.Where(s => s.Type == "approval").ToList();
        Assert.Equal(2, approvalSteps.Count);
        Assert.Equal(0, approvalSteps[0].StageIndex);
        Assert.Equal("档1", approvalSteps[0].StageName);
        Assert.Equal(1, approvalSteps[1].StageIndex);
    }

    [Fact]
    public async Task Forecast_ParallelSplit_MarksBranched()
    {
        using var db = NewDb();
        db.Wf_FlowDefs.Add(new Wf_FlowDef { Id = Guid.NewGuid(), FlowKey = "p", FlowName = "p", FormKey = "p",
            SchemaJson = JsonSerializer.Serialize(new FlowSchema {
                Nodes = {
                    new FlowNode { Id = "s", Type = "parallelSplit" },
                    new FlowNode { Id = "a", Name = "A 审", Type = "approval", ApproverStrategy = "Specified", ApproverUserId = Guid.NewGuid() },
                    new FlowNode { Id = "j", Type = "parallelJoin" },
                    new FlowNode { Id = "end", Type = "end" },
                },
                Edges = { new FlowEdge { From = "s", To = "a" }, new FlowEdge { From = "a", To = "j" }, new FlowEdge { From = "j", To = "end" } } }),
            Version = 1, Enable = true });
        await db.SaveChangesAsync();

        var res = await Forecast(db).ForecastAsync("p", "{}", Guid.NewGuid(), fromNodeId: null);
        Assert.True(res.Branched);                        // 含 parallelSplit
    }

    [Fact]
    public async Task Forecast_FormFieldNode_ResolvesNamedApprover_FromVars()
    {
        using var db = NewDb();
        var approver = Guid.NewGuid();
        db.Sys_Users.Add(new Sys_User { Id = approver, UserName = "boss", NickName = "Boss", Password = "x", Enable = true });
        var schema = "{\"start\":\"s\",\"nodes\":[" +
            "{\"id\":\"s\",\"type\":\"start\"}," +
            "{\"id\":\"a\",\"type\":\"approval\",\"approverStrategy\":\"FormField\",\"approverFieldName\":\"approver\"}," +
            "{\"id\":\"e\",\"type\":\"end\"}]," +
            "\"edges\":[{\"from\":\"s\",\"to\":\"a\"},{\"from\":\"a\",\"to\":\"e\"}]}";
        db.Wf_FlowDefs.Add(new Wf_FlowDef { Id = Guid.NewGuid(), FlowKey = "ff", FlowName = "ff", FormKey = "f",
            SchemaJson = schema, Enable = true });
        await db.SaveChangesAsync();

        var svc = new ForecastService(db, new ApproverResolver(db), new ApprovalStagePlanner(new ApproverResolver(db)));
        var res = await svc.ForecastAsync("ff", $"{{\"approver\":\"{approver}\"}}", Guid.NewGuid());
        var step = res.Steps.First(s => s.NodeId == "a");
        Assert.True(step.Resolved);
        Assert.Contains("Boss", step.Approvers);   // Approvers is the real property name on ForecastStep
    }
}
