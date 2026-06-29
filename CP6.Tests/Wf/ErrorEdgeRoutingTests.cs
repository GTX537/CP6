using CP6.Core.EFDbContext;
using CP6.Core.Services.Wf;
using CP6.Entity.DomainModels.Wf;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using System.Text.Json;

namespace CP6.Tests.Wf;

/// <summary>
/// A-T5 错误边路由(D8 不变量 + P1-4 错误变量)。
/// AdvanceToken 跳过 IsError 边(成功路径绝不误走);AdvanceAlongErrorEdge 仅走 IsError 边;
/// FailServiceTokenAsync 写 wf.serviceError 后按错误边/挂起路由。
/// </summary>
public class ErrorEdgeRoutingTests
{
    private static CP6Context NewDb() => new(new DbContextOptionsBuilder<CP6Context>()
        .UseInMemoryDatabase(Guid.NewGuid().ToString())
        .ConfigureWarnings(w => w.Ignore(InMemoryEventId.TransactionIgnoredWarning)).Options);
    private static FlowEngine Engine(CP6Context db) => new(db, new ApproverResolver(db));

    // start → svc(serviceTask) --成功--> end ; svc --IsError--> human(approval) → end
    private static FlowSchema SvcSchema(Guid approver) => new()
    {
        Start = "s",
        Nodes =
        {
            new FlowNode { Id = "s", Type = "start" },
            new FlowNode { Id = "svc", Type = "serviceTask", ServiceKind = ServiceKind.WebApi },
            new FlowNode { Id = "end", Type = "end" },
            new FlowNode { Id = "human", Type = "approval", ApproverStrategy = "Specified", ApproverUserId = approver },
        },
        Edges =
        {
            new FlowEdge { From = "s", To = "svc" },
            // ★ 错误边故意列在成功边之前:若 AdvanceToken 缺 `&& e.IsError != true` 过滤,foreach 会先命中
            //   这条错误边而误走 human → 令 D8 不变量测试真正具判别力(删过滤即红)。
            new FlowEdge { From = "svc", To = "human", IsError = true },  // 错误出边(列在前)
            new FlowEdge { From = "svc", To = "end" },                    // 成功出边(IsError == null)
            new FlowEdge { From = "human", To = "end" },
        },
    };

    // start → svc(serviceTask) --成功--> end (无错误边)
    private static FlowSchema SvcNoErrorEdgeSchema() => new()
    {
        Start = "s",
        Nodes =
        {
            new FlowNode { Id = "s", Type = "start" },
            new FlowNode { Id = "svc", Type = "serviceTask", ServiceKind = ServiceKind.WebApi },
            new FlowNode { Id = "end", Type = "end" },
        },
        Edges =
        {
            new FlowEdge { From = "s", To = "svc" },
            new FlowEdge { From = "svc", To = "end" },
        },
    };

    private static (Wf_FlowInstance inst, Wf_FlowToken tok) ParkAtSvc(CP6Context db, FlowEngine eng, FlowSchema schema, string vars = "{}")
    {
        var inst = new Wf_FlowInstance { Id = Guid.NewGuid(), FlowKey = "svc", StarterId = Guid.NewGuid(),
            Status = FlowInstanceStatus.Running, CurrentNode = "svc", VarsJson = vars };
        db.Wf_FlowInstances.Add(inst);
        var tok = eng.SpawnToken(inst, schema.Nodes.First(n => n.Id == "svc"));
        return (inst, tok);
    }

    private static void SeedDef(CP6Context db, FlowSchema schema)
        => db.Wf_FlowDefs.Add(new Wf_FlowDef { Id = Guid.NewGuid(), FlowKey = "svc", FlowName = "svc", FormKey = "f",
            SchemaJson = JsonSerializer.Serialize(schema), Version = 1, Enable = true });

    [Fact]
    public async Task AdvanceToken_SkipsErrorEdge_TakesSuccessEdge()
    {
        using var db = NewDb();
        var eng = Engine(db);
        var schema = SvcSchema(Guid.NewGuid());
        var (inst, tok) = ParkAtSvc(db, eng, schema);
        await db.SaveChangesAsync();

        await eng.AdvanceToken(inst, schema, tok);   // 成功路径
        await db.SaveChangesAsync();

        Assert.Equal("end", tok.NodeId);                            // 走成功边到 end,绝不走 human
        Assert.Equal(FlowTokenStatus.Consumed, tok.Status);         // end 节点消费
        Assert.Equal(FlowInstanceStatus.Approved, inst.Status);
        Assert.Equal(0, await db.Wf_FlowTasks.CountAsync());        // 从未触碰 human/审批
    }

    [Fact]
    public async Task AdvanceAlongErrorEdge_TakesErrorEdge()
    {
        using var db = NewDb();
        var eng = Engine(db);
        var approver = Guid.NewGuid();
        var schema = SvcSchema(approver);
        var (inst, tok) = ParkAtSvc(db, eng, schema);
        await db.SaveChangesAsync();

        await eng.AdvanceAlongErrorEdge(inst, schema, tok);   // 沿错误边
        await db.SaveChangesAsync();

        Assert.Equal("human", tok.NodeId);                          // 落在错误边目标 human
        Assert.Equal(FlowTokenStatus.Active, tok.Status);           // 停泊在审批节点
        Assert.Equal(1, await db.Wf_FlowTasks.CountAsync(t => t.AssigneeId == approver && t.Status == FlowTaskStatus.Pending));
        Assert.Equal(FlowInstanceStatus.Running, inst.Status);
    }

    [Fact]
    public async Task AdvanceAlongErrorEdge_NoErrorEdge_Suspends()
    {
        using var db = NewDb();
        var eng = Engine(db);
        var schema = SvcNoErrorEdgeSchema();
        var (inst, tok) = ParkAtSvc(db, eng, schema);
        await db.SaveChangesAsync();

        await eng.AdvanceAlongErrorEdge(inst, schema, tok);   // 无错误边
        await db.SaveChangesAsync();

        Assert.Equal("svc", tok.NodeId);                            // 未推进
        Assert.Equal(FlowTokenStatus.Active, tok.Status);
        Assert.Equal(FlowInstanceStatus.Suspended, inst.Status);    // 挂起
    }

    [Fact]
    public async Task AdvanceToken_NoErrorEdge_Unchanged()
    {
        using var db = NewDb();
        // 既有线性流 start → approval → end:行为字节等价
        var approver = Guid.NewGuid();
        var schema = new FlowSchema
        {
            Start = "s",
            Nodes =
            {
                new FlowNode { Id = "s", Type = "start" },
                new FlowNode { Id = "ap", Type = "approval", ApproverStrategy = "Specified", ApproverUserId = approver },
                new FlowNode { Id = "end", Type = "end" },
            },
            Edges =
            {
                new FlowEdge { From = "s", To = "ap" },
                new FlowEdge { From = "ap", To = "end" },
            },
        };
        db.Wf_FlowDefs.Add(new Wf_FlowDef { Id = Guid.NewGuid(), FlowKey = "lin", FlowName = "lin", FormKey = "f",
            SchemaJson = JsonSerializer.Serialize(schema), Version = 1, Enable = true });
        await db.SaveChangesAsync();

        await Engine(db).SubmitAsync("lin", Guid.NewGuid(), "{}");
        var task = await db.Wf_FlowTasks.SingleAsync(t => t.Status == FlowTaskStatus.Pending);
        Assert.Equal(approver, task.AssigneeId);
        Assert.Equal(FlowInstanceStatus.Running, (await db.Wf_FlowInstances.SingleAsync()).Status);

        await Engine(db).ActAsync(task.Id, approver, approve: true);
        Assert.Equal(FlowInstanceStatus.Approved, (await db.Wf_FlowInstances.SingleAsync()).Status);
        Assert.False(await db.Wf_FlowTokens.AnyAsync(t => t.Status == FlowTokenStatus.Active));
    }

    [Fact]
    public async Task Fail_WithErrorEdge_RoutesAndWritesServiceError()
    {
        using var db = NewDb();
        var eng = Engine(db);
        var approver = Guid.NewGuid();
        var schema = SvcSchema(approver);
        SeedDef(db, schema);
        var (inst, tok) = ParkAtSvc(db, eng, schema);
        await db.SaveChangesAsync();

        await eng.FailServiceTokenAsync(inst.Id, tok.Id, "svc", "boom");

        var i = await db.Wf_FlowInstances.SingleAsync();
        Assert.Equal("human", (await db.Wf_FlowTokens.SingleAsync()).NodeId);    // 走错误边到 human
        Assert.Equal(FlowInstanceStatus.Running, i.Status);
        Assert.Contains("serviceError", i.VarsJson);                              // wf.serviceError 直写
        Assert.Contains("boom", i.VarsJson);                                      // message=reason
        Assert.Equal(1, await db.Wf_FlowTasks.CountAsync(t => t.AssigneeId == approver));
    }

    [Fact]
    public async Task Fail_NoErrorEdge_Suspends_AndWritesServiceError()
    {
        using var db = NewDb();
        var eng = Engine(db);
        var schema = SvcNoErrorEdgeSchema();
        SeedDef(db, schema);
        var (inst, tok) = ParkAtSvc(db, eng, schema);
        await db.SaveChangesAsync();

        await eng.FailServiceTokenAsync(inst.Id, tok.Id, "svc", "kaboom");

        var i = await db.Wf_FlowInstances.SingleAsync();
        Assert.Equal(FlowInstanceStatus.Suspended, i.Status);     // 无错误边 → 挂起
        Assert.Equal("svc", (await db.Wf_FlowTokens.SingleAsync()).NodeId);
        Assert.Contains("serviceError", i.VarsJson);
        Assert.Contains("kaboom", i.VarsJson);
    }

    [Fact]
    public async Task Fail_NoOp_WhenTokenLeftNode()
    {
        using var db = NewDb();
        var eng = Engine(db);
        var schema = SvcSchema(Guid.NewGuid());
        SeedDef(db, schema);
        var (inst, tok) = ParkAtSvc(db, eng, schema);
        tok.NodeId = "end";   // token 已离开服务节点
        await db.SaveChangesAsync();

        await eng.FailServiceTokenAsync(inst.Id, tok.Id, "svc", "late");   // 幂等:不应改任何东西

        var i = await db.Wf_FlowInstances.SingleAsync();
        Assert.Equal(FlowInstanceStatus.Running, i.Status);
        Assert.DoesNotContain("serviceError", i.VarsJson);
        Assert.Equal("end", (await db.Wf_FlowTokens.SingleAsync()).NodeId);
    }
}
