using System.Text.Json;
using CP6.Core.EFDbContext;
using CP6.Core.Services.Wf;
using CP6.Entity.DomainModels.Wf;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace CP6.Tests.Wf;

/// <summary>终审 Critical#1 定点回归：split 存在「直连 join」出边（合法 schema，过 FlowSchemaValidator）时，
/// 单相 spawn+Enter 会让首枚子 token 在兄弟 token 生出之前同步抵达 join → 动态计票（A-T2）看不到阻挡者
/// → 提前放行（审批绕过）+ 兄弟支后到再次放行（双重放行）+ 孤儿 Active token 永泊（实例永 Running）。
/// 两 split handler 改两阶段（先全 spawn 后逐个 Enter）后，首枚到场 token 能看到全部同批兄弟 → 正确停泊。</summary>
public class SplitDirectEdgeToJoinTests
{
    private static CP6Context NewDb() => new(new DbContextOptionsBuilder<CP6Context>()
        .UseInMemoryDatabase(Guid.NewGuid().ToString())
        .ConfigureWarnings(w => w.Ignore(InMemoryEventId.TransactionIgnoredWarning)).Options);
    private static FlowEngine Engine(CP6Context db) => new(db, new ApproverResolver(db));

    // s → psplit → ( 直连 join [首条边] , a → join ) → join → post → end
    private static FlowSchema ParallelDirect(Guid ua, Guid upost) => new()
    {
        Start = "s",
        Nodes =
        {
            new FlowNode { Id = "s", Type = "start" },
            new FlowNode { Id = "psplit", Type = "parallelSplit" },
            new FlowNode { Id = "a", Type = "approval", ApproverStrategy = "Specified", ApproverUserId = ua },
            new FlowNode { Id = "join", Type = "parallelJoin" },
            new FlowNode { Id = "post", Type = "approval", ApproverStrategy = "Specified", ApproverUserId = upost },
            new FlowNode { Id = "end", Type = "end" },
        },
        Edges =
        {
            new FlowEdge { From = "s", To = "psplit" },
            new FlowEdge { From = "psplit", To = "join" },   // ★ 直连边在前 → 先处理
            new FlowEdge { From = "psplit", To = "a" },
            new FlowEdge { From = "a", To = "join" },
            new FlowEdge { From = "join", To = "post" },
            new FlowEdge { From = "post", To = "end" },
        },
    };

    [Fact]
    public async Task Parallel_DirectEdgeToJoin_NoPrematureRelease_SingleRelease_NoOrphan()
    {
        using var db = NewDb();
        Guid ua = Guid.NewGuid(), upost = Guid.NewGuid();
        db.Wf_FlowDefs.Add(new Wf_FlowDef { Id = Guid.NewGuid(), FlowKey = "pdj", FlowName = "pdj", FormKey = "f",
            SchemaJson = JsonSerializer.Serialize(ParallelDirect(ua, upost)), Version = 1, Enable = true });
        await db.SaveChangesAsync();
        await Engine(db).SubmitAsync("pdj", Guid.NewGuid(), "{}");

        // ★① 提交后 join 绝不能已放行（a 支还没审）→ post 零任务（否则=审批绕过）
        Assert.False(await db.Wf_FlowTasks.AnyAsync(t => t.AssigneeId == upost));
        Assert.Equal(0, await db.Wf_FlowHistories.CountAsync(h => h.Action == "parallelJoin"));

        // a 支审过 → join 齐批放行，且恰好一次
        var ta = await db.Wf_FlowTasks.SingleAsync(t => t.AssigneeId == ua && t.Status == FlowTaskStatus.Pending);
        await Engine(db).ActAsync(ta.Id, ua, approve: true);
        Assert.Equal(1, await db.Wf_FlowHistories.CountAsync(h => h.Action == "parallelJoin"));   // ★② 恰一次放行

        // ★③ post 审过 → Approved 终态，零孤儿 Active token
        var tp = await db.Wf_FlowTasks.SingleAsync(t => t.AssigneeId == upost && t.Status == FlowTaskStatus.Pending);
        await Engine(db).ActAsync(tp.Id, upost, approve: true);
        Assert.Equal(FlowInstanceStatus.Approved, (await db.Wf_FlowInstances.SingleAsync()).Status);
        Assert.False(await db.Wf_FlowTokens.AnyAsync(t => t.Status == FlowTokenStatus.Active));
    }

    // s → isplit → ( 直连 ijoin ["go>0" 首条边] , a["go>0"] → ijoin , d[default 兜底，不走] → ijoin ) → ijoin → post → end
    private static FlowSchema InclusiveDirect(Guid ua, Guid ud, Guid upost) => new()
    {
        Start = "s",
        Nodes =
        {
            new FlowNode { Id = "s", Type = "start" },
            new FlowNode { Id = "isplit", Type = "inclusiveSplit" },
            new FlowNode { Id = "a", Type = "approval", ApproverStrategy = "Specified", ApproverUserId = ua },
            new FlowNode { Id = "d", Type = "approval", ApproverStrategy = "Specified", ApproverUserId = ud },
            new FlowNode { Id = "ijoin", Type = "inclusiveJoin" },
            new FlowNode { Id = "post", Type = "approval", ApproverStrategy = "Specified", ApproverUserId = upost },
            new FlowNode { Id = "end", Type = "end" },
        },
        Edges =
        {
            new FlowEdge { From = "s", To = "isplit" },
            new FlowEdge { From = "isplit", To = "ijoin", Condition = "go > 0" },   // ★ 真条件直连边在前
            new FlowEdge { From = "isplit", To = "a", Condition = "go > 0" },
            new FlowEdge { From = "isplit", To = "d" },                             // default 兜底（有真边不走）
            new FlowEdge { From = "a", To = "ijoin" }, new FlowEdge { From = "d", To = "ijoin" },
            new FlowEdge { From = "ijoin", To = "post" },
            new FlowEdge { From = "post", To = "end" },
        },
    };

    [Fact]
    public async Task Inclusive_DirectEdgeToJoin_NoPrematureRelease_SingleRelease_NoOrphan()
    {
        using var db = NewDb();
        Guid ua = Guid.NewGuid(), ud = Guid.NewGuid(), upost = Guid.NewGuid();
        db.Wf_FlowDefs.Add(new Wf_FlowDef { Id = Guid.NewGuid(), FlowKey = "idj", FlowName = "idj", FormKey = "f",
            SchemaJson = JsonSerializer.Serialize(InclusiveDirect(ua, ud, upost)), Version = 1, Enable = true });
        await db.SaveChangesAsync();
        await Engine(db).SubmitAsync("idj", Guid.NewGuid(), "{\"go\":1}");

        // ★① 提交后 ijoin 绝不能已放行 → post 零任务；default 支 d 不走
        Assert.False(await db.Wf_FlowTasks.AnyAsync(t => t.AssigneeId == upost));
        Assert.False(await db.Wf_FlowTasks.AnyAsync(t => t.AssigneeId == ud));
        Assert.Equal(0, await db.Wf_FlowHistories.CountAsync(h => h.Action == "inclusiveJoin"));

        // a 支审过 → ijoin 齐批放行，且恰好一次
        var ta = await db.Wf_FlowTasks.SingleAsync(t => t.AssigneeId == ua && t.Status == FlowTaskStatus.Pending);
        await Engine(db).ActAsync(ta.Id, ua, approve: true);
        Assert.Equal(1, await db.Wf_FlowHistories.CountAsync(h => h.Action == "inclusiveJoin"));   // ★② 恰一次放行

        // ★③ post 审过 → Approved 终态，零孤儿 Active token
        var tp = await db.Wf_FlowTasks.SingleAsync(t => t.AssigneeId == upost && t.Status == FlowTaskStatus.Pending);
        await Engine(db).ActAsync(tp.Id, upost, approve: true);
        Assert.Equal(FlowInstanceStatus.Approved, (await db.Wf_FlowInstances.SingleAsync()).Status);
        Assert.False(await db.Wf_FlowTokens.AnyAsync(t => t.Status == FlowTokenStatus.Active));
    }
}
