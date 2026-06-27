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
    private static IForecastService Forecast(CP6Context db) => new ForecastService(db, new ApproverResolver(db));

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
}
