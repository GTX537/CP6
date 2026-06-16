using System.Text.Json;
using CP6.Core.EFDbContext;
using CP6.Core.Services.Wf;
using CP6.Entity.DomainModels.Wf;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace CP6.Tests.Wf;

/// <summary>
/// OA 章07 §4 超时扫描（C-4）。注入 now 测扫描逻辑：remind 软动作可重复 / approve·reject·escalate
/// 硬动作一次性 + TimeoutHandled 幂等（处理过不再处理）。
/// </summary>
public class TimeoutScanTests
{
    private static CP6Context NewDb() => new(new DbContextOptionsBuilder<CP6Context>()
        .UseInMemoryDatabase(Guid.NewGuid().ToString())
        .ConfigureWarnings(w => w.Ignore(InMemoryEventId.TransactionIgnoredWarning))
        .Options);

    private const string FlowKey = "timeout-flow";

    /// <summary>单审批人 n1(A)→end，n1 配超时 1 小时 + 指定动作（escalate 时升级给 escalateTo）。</summary>
    private static async Task SeedAsync(CP6Context db, Guid a, string action, Guid? escalateTo = null)
    {
        var schema = new FlowSchema
        {
            Start = "n1",
            Nodes =
            {
                new FlowNode
                {
                    Id = "n1", Type = "approval", ApproverStrategy = "Specified", ApproverUserId = a,
                    TimeoutHours = 1, TimeoutAction = action, EscalateTo = escalateTo,
                },
                new FlowNode { Id = "end", Type = "end" },
            },
            Edges = { new FlowEdge { From = "n1", To = "end" } },
        };
        db.Wf_FlowDefs.Add(new Wf_FlowDef
        {
            Id = Guid.NewGuid(), FlowKey = FlowKey, FlowName = "超时流程", FormKey = "test",
            SchemaJson = JsonSerializer.Serialize(schema), Version = 1, Enable = true,
        });
        await db.SaveChangesAsync();
    }

    private static (FlowEngine engine, WfTimeoutService svc) Build(CP6Context db)
    {
        var engine = new FlowEngine(db, new ApproverResolver(db));
        return (engine, new WfTimeoutService(db, engine));
    }

    private static DateTime Future => DateTime.Now.AddHours(2);   // > DueAt(now+1h)

    [Fact]
    public async Task Submit_SetsDueAt_WhenNodeHasTimeout()
    {
        using var db = NewDb();
        var a = Guid.NewGuid();
        await SeedAsync(db, a, "remind");
        var (engine, _) = Build(db);
        await engine.SubmitAsync(FlowKey, Guid.NewGuid(), "{}");

        var task = await db.Wf_FlowTasks.SingleAsync();
        Assert.NotNull(task.DueAt);   // 配了超时 → 建待办即设 DueAt
    }

    [Fact]
    public async Task Timeout_Approve_AutoPostsAndAdvances()
    {
        using var db = NewDb();
        var a = Guid.NewGuid();
        await SeedAsync(db, a, "approve");
        var (engine, svc) = Build(db);
        var instId = await engine.SubmitAsync(FlowKey, Guid.NewGuid(), "{}");

        var n = await svc.ScanOnceAsync(Future);
        Assert.Equal(1, n);
        var inst = await db.Wf_FlowInstances.SingleAsync(i => i.Id == instId);
        Assert.Equal(FlowInstanceStatus.Approved, inst.Status);   // 超时自动同意 → 流转 end
        Assert.Equal(1, await db.Wf_FlowHistories.CountAsync(h => h.Comment == "超时自动同意"));
    }

    [Fact]
    public async Task Timeout_Reject_AutoRejects()
    {
        using var db = NewDb();
        var a = Guid.NewGuid();
        await SeedAsync(db, a, "reject");
        var (engine, svc) = Build(db);
        var instId = await engine.SubmitAsync(FlowKey, Guid.NewGuid(), "{}");

        await svc.ScanOnceAsync(Future);
        var inst = await db.Wf_FlowInstances.SingleAsync(i => i.Id == instId);
        Assert.Equal(FlowInstanceStatus.Rejected, inst.Status);
    }

    [Fact]
    public async Task Timeout_Escalate_ReassignsAndTraces()
    {
        using var db = NewDb();
        var a = Guid.NewGuid();
        var boss = Guid.NewGuid();
        await SeedAsync(db, a, "escalate", escalateTo: boss);
        var (engine, svc) = Build(db);
        await engine.SubmitAsync(FlowKey, Guid.NewGuid(), "{}");

        await svc.ScanOnceAsync(Future);

        var task = await db.Wf_FlowTasks.SingleAsync();
        Assert.Equal(boss, task.AssigneeId);          // 升级给上级
        Assert.True(task.TimeoutHandled);
        Assert.Equal(FlowTaskStatus.Pending, task.Status);   // 仍待办（换了人）
        Assert.Equal(1, await db.Wf_FlowHistories.CountAsync(h => h.Action == "escalate"));
    }

    [Fact]
    public async Task Timeout_Remind_PushesDueForward_Repeatable()
    {
        using var db = NewDb();
        var a = Guid.NewGuid();
        await SeedAsync(db, a, "remind");
        var (engine, svc) = Build(db);
        await engine.SubmitAsync(FlowKey, Guid.NewGuid(), "{}");

        var now = Future;
        await svc.ScanOnceAsync(now);
        var task = await db.Wf_FlowTasks.SingleAsync();
        Assert.False(task.TimeoutHandled);            // 软动作不置 Handled（可重复催办）
        Assert.True(task.DueAt > now);                // DueAt 顺延
        Assert.Equal(FlowTaskStatus.Pending, task.Status);
    }

    [Fact]
    public async Task Timeout_HardAction_Idempotent_SecondScanNoop()
    {
        using var db = NewDb();
        var a = Guid.NewGuid();
        var boss = Guid.NewGuid();
        await SeedAsync(db, a, "escalate", escalateTo: boss);
        var (engine, svc) = Build(db);
        await engine.SubmitAsync(FlowKey, Guid.NewGuid(), "{}");

        Assert.Equal(1, await svc.ScanOnceAsync(Future));
        Assert.Equal(0, await svc.ScanOnceAsync(Future.AddHours(1)));   // 已 Handled → 不再处理
        Assert.Equal(1, await db.Wf_FlowHistories.CountAsync(h => h.Action == "escalate"));
    }

    [Fact]
    public async Task Scan_BeforeDue_NothingHappens()
    {
        using var db = NewDb();
        var a = Guid.NewGuid();
        await SeedAsync(db, a, "approve");
        var (engine, svc) = Build(db);
        await engine.SubmitAsync(FlowKey, Guid.NewGuid(), "{}");

        var n = await svc.ScanOnceAsync(DateTime.Now);   // 未到 DueAt(now+1h)
        Assert.Equal(0, n);
    }
}
