using CP6.Core.EFDbContext;
using CP6.Core.Services.Wf;
using CP6.Entity.DomainModels.Wf;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using System.Text.Json;

namespace CP6.Tests.Wf;

/// <summary>
/// A-T5 ResumeServiceTokenAsync 幂等(P0-2)。停泊 token 恢复后沿成功边推进;
/// 幂等闸:token 非 Active 或 NodeId != nodeId → no-op(防崩溃重投二次推进);outputVars 经 helper 合并。
/// </summary>
public class ResumeServiceTokenTests
{
    private static CP6Context NewDb() => new(new DbContextOptionsBuilder<CP6Context>()
        .UseInMemoryDatabase(Guid.NewGuid().ToString())
        .ConfigureWarnings(w => w.Ignore(InMemoryEventId.TransactionIgnoredWarning)).Options);
    private static FlowEngine Engine(CP6Context db) => new(db, new ApproverResolver(db));

    // start → svc(serviceTask) → end
    private static FlowSchema Schema() => new()
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

    private static async Task<(Wf_FlowInstance inst, Wf_FlowToken tok)> ParkAsync(CP6Context db, FlowEngine eng, string vars = "{}")
    {
        var schema = Schema();
        db.Wf_FlowDefs.Add(new Wf_FlowDef { Id = Guid.NewGuid(), FlowKey = "svc", FlowName = "svc", FormKey = "f",
            SchemaJson = JsonSerializer.Serialize(schema), Version = 1, Enable = true });
        var inst = new Wf_FlowInstance { Id = Guid.NewGuid(), FlowKey = "svc", StarterId = Guid.NewGuid(),
            Status = FlowInstanceStatus.Running, CurrentNode = "svc", VarsJson = vars };
        db.Wf_FlowInstances.Add(inst);
        var tok = eng.SpawnToken(inst, schema.Nodes.First(n => n.Id == "svc"));
        await db.SaveChangesAsync();
        return (inst, tok);
    }

    [Fact]
    public async Task Resume_AdvancesParkedToken_Once()
    {
        using var db = NewDb();
        var eng = Engine(db);
        var (inst, tok) = await ParkAsync(db, eng);

        await eng.ResumeServiceTokenAsync(inst.Id, tok.Id, "svc", null);

        var t = await db.Wf_FlowTokens.SingleAsync();
        Assert.Equal("end", t.NodeId);                                  // 离开 svc 到 end
        Assert.Equal(FlowTokenStatus.Consumed, t.Status);
        Assert.Equal(FlowInstanceStatus.Approved, (await db.Wf_FlowInstances.SingleAsync()).Status);
    }

    [Fact]
    public async Task Resume_NoOp_WhenTokenNotActive()
    {
        using var db = NewDb();
        var eng = Engine(db);
        var (inst, tok) = await ParkAsync(db, eng);
        tok.Status = FlowTokenStatus.Consumed;     // 已消费(例如另一路径已恢复)
        await db.SaveChangesAsync();

        await eng.ResumeServiceTokenAsync(inst.Id, tok.Id, "svc", null);   // 不报错、不二次推进

        var t = await db.Wf_FlowTokens.SingleAsync();
        Assert.Equal("svc", t.NodeId);                                  // 未变
        Assert.Equal(FlowTokenStatus.Consumed, t.Status);
        Assert.Equal(FlowInstanceStatus.Running, (await db.Wf_FlowInstances.SingleAsync()).Status);
    }

    [Fact]
    public async Task Resume_NoOp_WhenTokenLeftNode()
    {
        using var db = NewDb();
        var eng = Engine(db);
        var (inst, tok) = await ParkAsync(db, eng);
        tok.NodeId = "end";    // token 已离开服务节点(已被前一次成功恢复推进)
        await db.SaveChangesAsync();

        await eng.ResumeServiceTokenAsync(inst.Id, tok.Id, "svc", null);   // NodeId 不匹配 → 幂等 no-op

        var t = await db.Wf_FlowTokens.SingleAsync();
        Assert.Equal("end", t.NodeId);
        Assert.Equal(FlowTokenStatus.Active, t.Status);                 // 没有被二次推进/消费
        Assert.Equal(FlowInstanceStatus.Running, (await db.Wf_FlowInstances.SingleAsync()).Status);
    }

    [Fact]
    public async Task Resume_MergesOutputVars()
    {
        using var db = NewDb();
        var eng = Engine(db);
        var (inst, tok) = await ParkAsync(db, eng, "{\"a\":1}");

        await eng.ResumeServiceTokenAsync(inst.Id, tok.Id, "svc",
            new Dictionary<string, object?> { ["b"] = 2, ["wf"] = "blocked" });

        var i = await db.Wf_FlowInstances.SingleAsync();
        Assert.Contains("\"b\":2", i.VarsJson);              // 合并新键
        Assert.Contains("\"a\":1", i.VarsJson);              // 保留既有
        Assert.DoesNotContain("blocked", i.VarsJson);        // 保留前缀 wf.* 被 helper 拦截
        Assert.True(await db.Wf_FlowHistories.AnyAsync(h => h.Action == "serviceVars"));   // §3.6 history
    }
}
