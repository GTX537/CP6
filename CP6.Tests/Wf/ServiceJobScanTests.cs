using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using CP6.Core.EFDbContext;
using CP6.Core.Services.Wf;
using CP6.Entity.DomainModels.Wf;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Xunit;

namespace CP6.Tests.Wf;

/// <summary>B-T1：<see cref="WfServiceJobService.ScanOnceAsync"/> 异步引擎核心。
/// 覆盖：① reaper（P0-4，只回收过期租约）② lease 抢占 ③ 执行前状态闸（P0-5，撤回/token 离开 → Cancelled 不执行）
/// ④ 成功 → ResumeServiceTokenAsync 推进 + Succeeded ⑤ 失败 → 指数退避重试（AttemptCount&lt;MaxAttempts），
/// 耗尽 → FailServiceTokenAsync 路由（有错误边走错误边 + 写 wf.serviceError；无则 Suspend）⑥ timer 纯等待（actionKind=none）不调 executor 直接推进。
/// nowUtc 注入实现确定性（退避/lease/due 全可测）。RowVersion 并发-skip 为 provider-dependent（InMemory 不触发 DbUpdateConcurrencyException）→ 本套行为级测试不依赖它，留 SQL Server 生产兜。</summary>
public class ServiceJobScanTests
{
    // ── 可控执行器：成功/失败可配，记录调用次数（验状态闸"不执行"）──
    private sealed class FakeExec : IServiceTaskExecutor
    {
        private readonly bool _succeed;
        private readonly Dictionary<string, object?>? _out;
        public FakeExec(string key, bool succeed, Dictionary<string, object?>? outputVars = null)
        { Key = key; _succeed = succeed; _out = outputVars; }
        public string Key { get; }
        public string Kind => "dataWriteback";
        public bool VisibleInDesigner => true;
        public string DisplayName => "Fake";
        public int CallCount { get; private set; }
        public Task<ServiceTaskResult> ExecuteAsync(ServiceTaskContext ctx)
        {
            CallCount++;
            return Task.FromResult(_succeed ? ServiceTaskResult.Ok(_out) : ServiceTaskResult.Fail("boom"));
        }
    }

    private static CP6Context NewDb() => new(new DbContextOptionsBuilder<CP6Context>()
        .UseInMemoryDatabase(Guid.NewGuid().ToString())
        .ConfigureWarnings(w => w.Ignore(InMemoryEventId.TransactionIgnoredWarning)).Options);

    private static FlowEngine Engine(CP6Context db) => new(db, new ApproverResolver(db));

    private static WfServiceJobService Svc(CP6Context db, FlowEngine eng, params IServiceTaskExecutor[] execs)
        => new(db, eng, execs);

    // svc 节点：dataWriteback 动作 "act"（ActionRef → actionKind=dataWriteback，ResolveExecutorKey → "act"）
    private static FlowNode SvcNode() => new()
    {
        Id = "svc", Type = "serviceTask",
        ServiceKind = ServiceKind.DataWriteback, ServiceActionName = "act",
    };

    // start → svc --IsError--> human(approval) ; svc --成功--> end ; human → end
    private static FlowSchema SchemaWithErrorEdge(Guid approver) => new()
    {
        Start = "s",
        Nodes =
        {
            new FlowNode { Id = "s", Type = "start" },
            SvcNode(),
            new FlowNode { Id = "end", Type = "end" },
            new FlowNode { Id = "human", Type = "approval", ApproverStrategy = "Specified", ApproverUserId = approver },
        },
        Edges =
        {
            new FlowEdge { From = "s", To = "svc" },
            new FlowEdge { From = "svc", To = "human", IsError = true },
            new FlowEdge { From = "svc", To = "end" },
            new FlowEdge { From = "human", To = "end" },
        },
    };

    // start → svc → end（无错误边）
    private static FlowSchema SchemaNoErrorEdge() => new()
    {
        Start = "s",
        Nodes =
        {
            new FlowNode { Id = "s", Type = "start" },
            SvcNode(),
            new FlowNode { Id = "end", Type = "end" },
        },
        Edges =
        {
            new FlowEdge { From = "s", To = "svc" },
            new FlowEdge { From = "svc", To = "end" },
        },
    };

    private static void SeedDef(CP6Context db, FlowSchema schema)
        => db.Wf_FlowDefs.Add(new Wf_FlowDef { Id = Guid.NewGuid(), FlowKey = "svc", FlowName = "svc", FormKey = "f",
            SchemaJson = JsonSerializer.Serialize(schema), Version = 1, Enable = true });

    // 停泊 token@svc + 建一个 Pending job（按需配 due/attempt/maxAttempts）。返回 (inst, tok, job)。
    private static (Wf_FlowInstance inst, Wf_FlowToken tok, Wf_ServiceJob job) Park(
        CP6Context db, FlowEngine eng, FlowSchema schema, DateTime nextAttemptAtUtc,
        int attemptCount = 0, int maxAttempts = 4, int instStatus = FlowInstanceStatus.Running,
        string? tokNodeId = null, string vars = "{}")
    {
        SeedDef(db, schema);
        var svc = schema.Nodes.First(n => n.Id == "svc");
        var inst = new Wf_FlowInstance { Id = Guid.NewGuid(), FlowKey = "svc", StarterId = Guid.NewGuid(),
            Status = instStatus, CurrentNode = "svc", VarsJson = vars };
        db.Wf_FlowInstances.Add(inst);
        var tok = eng.SpawnToken(inst, svc);
        if (tokNodeId is not null) tok.NodeId = tokNodeId;   // 模拟 token 已离开服务节点
        var job = new Wf_ServiceJob
        {
            Id = Guid.NewGuid(), InstanceId = inst.Id, TokenId = tok.Id, NodeId = "svc",
            Kind = svc.ServiceKind ?? "", ActionRefJson = ServiceTaskActionRef.Snapshot(svc),
            DueAtUtc = nextAttemptAtUtc, Status = ServiceJobStatus.Pending,
            AttemptCount = attemptCount, MaxAttempts = maxAttempts, NextAttemptAtUtc = nextAttemptAtUtc,
            CreateDate = DateTime.UtcNow,
        };
        db.Wf_ServiceJobs.Add(job);
        return (inst, tok, job);
    }

    private static readonly DateTime T0 = new(2026, 6, 29, 12, 0, 0, DateTimeKind.Utc);

    // ─────────────────────────────────────────────────────────────

    [Fact]
    public async Task DueJob_Success_ResumesToken_MarksSucceeded()
    {
        using var db = NewDb();
        var eng = Engine(db);
        var exec = new FakeExec("act", succeed: true);
        var (inst, _, job) = Park(db, eng, SchemaNoErrorEdge(), nextAttemptAtUtc: T0);
        await db.SaveChangesAsync();

        var processed = await Svc(db, eng, exec).ScanOnceAsync(T0, "w1");

        Assert.Equal(1, processed);
        Assert.Equal(1, exec.CallCount);
        var j = await db.Wf_ServiceJobs.SingleAsync();
        Assert.Equal(ServiceJobStatus.Succeeded, j.Status);
        Assert.NotNull(j.CompletedAtUtc);
        // token 离开 svc → 实例走到 end
        Assert.False(await db.Wf_FlowTokens.AnyAsync(t => t.NodeId == "svc" && t.Status == FlowTokenStatus.Active));
        Assert.Equal(FlowInstanceStatus.Approved, (await db.Wf_FlowInstances.SingleAsync()).Status);
    }

    [Fact]
    public async Task NotDue_Skipped()
    {
        using var db = NewDb();
        var eng = Engine(db);
        var exec = new FakeExec("act", succeed: true);
        var (_, _, job) = Park(db, eng, SchemaNoErrorEdge(), nextAttemptAtUtc: T0.AddHours(1));   // 未到期
        await db.SaveChangesAsync();

        var processed = await Svc(db, eng, exec).ScanOnceAsync(T0, "w1");

        Assert.Equal(0, processed);
        Assert.Equal(0, exec.CallCount);
        var j = await db.Wf_ServiceJobs.SingleAsync();
        Assert.Equal(ServiceJobStatus.Pending, j.Status);
        Assert.Equal(0, j.AttemptCount);
        Assert.Null(j.LockedBy);
    }

    [Fact]
    public async Task Fail_Retries_WithBackoff_UntilMaxAttempts()
    {
        using var db = NewDb();
        var eng = Engine(db);
        var exec = new FakeExec("act", succeed: false);
        var (_, _, job) = Park(db, eng, SchemaNoErrorEdge(), nextAttemptAtUtc: T0, attemptCount: 0, maxAttempts: 2);
        await db.SaveChangesAsync();

        var svc = Svc(db, eng, exec);

        // 第 1 次：执行失败 → AttemptCount=1 < MaxAttempts=2 → Pending + 退避
        var p1 = await svc.ScanOnceAsync(T0, "w1");
        Assert.Equal(1, p1);
        var j1 = await db.Wf_ServiceJobs.SingleAsync();
        Assert.Equal(ServiceJobStatus.Pending, j1.Status);
        Assert.Equal(1, j1.AttemptCount);
        Assert.True(j1.NextAttemptAtUtc > T0);                          // 指数退避后推迟
        Assert.Equal(T0.AddSeconds(30), j1.NextAttemptAtUtc);          // 30 * 2^(1-1) = 30s
        Assert.Null(j1.LockedBy);                                       // 退避时清租约

        // 第 2 次：推进 nowUtc 越过退避 → 执行再失败 → AttemptCount=2 == MaxAttempts → Failed + 路由
        var t1 = T0.AddSeconds(31);
        var p2 = await svc.ScanOnceAsync(t1, "w1");
        Assert.Equal(1, p2);
        Assert.Equal(2, exec.CallCount);
        var j2 = await db.Wf_ServiceJobs.SingleAsync();
        Assert.Equal(ServiceJobStatus.Failed, j2.Status);
        Assert.Equal(2, j2.AttemptCount);
        Assert.NotNull(j2.CompletedAtUtc);
    }

    [Fact]
    public async Task Exhausted_NoErrorEdge_Suspends()
    {
        using var db = NewDb();
        var eng = Engine(db);
        var exec = new FakeExec("act", succeed: false);
        var (inst, _, _) = Park(db, eng, SchemaNoErrorEdge(), nextAttemptAtUtc: T0, attemptCount: 0, maxAttempts: 1);
        await db.SaveChangesAsync();

        await Svc(db, eng, exec).ScanOnceAsync(T0, "w1");

        var j = await db.Wf_ServiceJobs.SingleAsync();
        Assert.Equal(ServiceJobStatus.Failed, j.Status);
        Assert.Equal(FlowInstanceStatus.Suspended, (await db.Wf_FlowInstances.SingleAsync()).Status);   // 无错误边 → 挂起
    }

    [Fact]
    public async Task Exhausted_WithErrorEdge_RoutesAndWritesServiceError()
    {
        using var db = NewDb();
        var eng = Engine(db);
        var approver = Guid.NewGuid();
        var exec = new FakeExec("act", succeed: false);
        var (inst, _, _) = Park(db, eng, SchemaWithErrorEdge(approver), nextAttemptAtUtc: T0, attemptCount: 0, maxAttempts: 1);
        await db.SaveChangesAsync();

        await Svc(db, eng, exec).ScanOnceAsync(T0, "w1");

        var j = await db.Wf_ServiceJobs.SingleAsync();
        Assert.Equal(ServiceJobStatus.Failed, j.Status);
        var i = await db.Wf_FlowInstances.SingleAsync();
        Assert.Equal(FlowInstanceStatus.Running, i.Status);                            // 走错误边，实例仍在途
        Assert.Equal("human", (await db.Wf_FlowTokens.SingleAsync()).NodeId);          // token 落到 human
        Assert.Contains("serviceError", i.VarsJson);                                   // P1-4：wf.serviceError 写入
        Assert.Equal(1, await db.Wf_FlowTasks.CountAsync(t => t.AssigneeId == approver && t.Status == FlowTaskStatus.Pending));
    }

    [Fact]
    public async Task Reaper_ResetsExpiredLease_Only()
    {
        using var db = NewDb();
        var eng = Engine(db);
        // A：Running 且租约已过期 → 应重置 Pending + AttemptCount++（NextAttemptAtUtc 设未来，避免本轮被再次取用，隔离 reaper 行为）
        var a = new Wf_ServiceJob { Id = Guid.NewGuid(), InstanceId = Guid.NewGuid(), TokenId = Guid.NewGuid(),
            NodeId = "svc", Kind = ServiceKind.WebApi, Status = ServiceJobStatus.Running,
            AttemptCount = 1, MaxAttempts = 4, NextAttemptAtUtc = T0.AddHours(1),
            LockedBy = "deadWorker", LockedAtUtc = T0.AddMinutes(-10), LockExpiresAtUtc = T0.AddMinutes(-1),
            CreateDate = DateTime.UtcNow };
        // B：Running 且租约未过期 → 不动
        var b = new Wf_ServiceJob { Id = Guid.NewGuid(), InstanceId = Guid.NewGuid(), TokenId = Guid.NewGuid(),
            NodeId = "svc", Kind = ServiceKind.WebApi, Status = ServiceJobStatus.Running,
            AttemptCount = 0, MaxAttempts = 4, NextAttemptAtUtc = T0,
            LockedBy = "liveWorker", LockedAtUtc = T0.AddMinutes(-1), LockExpiresAtUtc = T0.AddMinutes(5),
            CreateDate = DateTime.UtcNow };
        db.Wf_ServiceJobs.AddRange(a, b);
        await db.SaveChangesAsync();

        await Svc(db, eng).ScanOnceAsync(T0, "w1");

        var ja = await db.Wf_ServiceJobs.SingleAsync(j => j.Id == a.Id);
        Assert.Equal(ServiceJobStatus.Pending, ja.Status);
        Assert.Equal(2, ja.AttemptCount);          // ++（计入崩溃的那次）
        Assert.Null(ja.LockedBy);
        Assert.Null(ja.LockedAtUtc);
        Assert.Null(ja.LockExpiresAtUtc);

        var jb = await db.Wf_ServiceJobs.SingleAsync(j => j.Id == b.Id);
        Assert.Equal(ServiceJobStatus.Running, jb.Status);     // 未过期租约不回收
        Assert.Equal(0, jb.AttemptCount);
        Assert.Equal("liveWorker", jb.LockedBy);
        Assert.Equal(T0.AddMinutes(5), jb.LockExpiresAtUtc);
    }

    [Fact]
    public async Task StateGate_InstanceWithdrawn_CancelsJob_NoExecute()
    {
        using var db = NewDb();
        var eng = Engine(db);
        var exec = new FakeExec("act", succeed: true);
        var (inst, _, _) = Park(db, eng, SchemaNoErrorEdge(), nextAttemptAtUtc: T0,
            instStatus: FlowInstanceStatus.Withdrawn);   // 实例已撤回
        await db.SaveChangesAsync();

        await Svc(db, eng, exec).ScanOnceAsync(T0, "w1");

        Assert.Equal(0, exec.CallCount);                       // P0-5：撤回 → 不执行 executor
        var j = await db.Wf_ServiceJobs.SingleAsync();
        Assert.Equal(ServiceJobStatus.Cancelled, j.Status);
        Assert.NotNull(j.CompletedAtUtc);
    }

    [Fact]
    public async Task StateGate_TokenLeftNode_CancelsJob()
    {
        using var db = NewDb();
        var eng = Engine(db);
        var exec = new FakeExec("act", succeed: true);
        var (inst, _, _) = Park(db, eng, SchemaNoErrorEdge(), nextAttemptAtUtc: T0,
            tokNodeId: "end");   // token 已离开 svc
        await db.SaveChangesAsync();

        await Svc(db, eng, exec).ScanOnceAsync(T0, "w1");

        Assert.Equal(0, exec.CallCount);
        var j = await db.Wf_ServiceJobs.SingleAsync();
        Assert.Equal(ServiceJobStatus.Cancelled, j.Status);
    }

    [Fact]
    public async Task Timer_NoAction_JustAdvances()
    {
        using var db = NewDb();
        var eng = Engine(db);
        var exec = new FakeExec("act", succeed: true);   // 不应被调用（timer 纯等待无 executor）
        // 纯等待 timer 节点：actionKind=none，ResolveExecutorKey=null
        var svcNode = new FlowNode { Id = "svc", Type = "serviceTask",
            ServiceKind = ServiceKind.Timer, ServiceDelayMode = "duration", ServiceDelayValue = "PT1H" };
        var schema = new FlowSchema
        {
            Start = "s",
            Nodes = { new FlowNode { Id = "s", Type = "start" }, svcNode, new FlowNode { Id = "end", Type = "end" } },
            Edges = { new FlowEdge { From = "s", To = "svc" }, new FlowEdge { From = "svc", To = "end" } },
        };
        SeedDef(db, schema);
        var inst = new Wf_FlowInstance { Id = Guid.NewGuid(), FlowKey = "svc", StarterId = Guid.NewGuid(),
            Status = FlowInstanceStatus.Running, CurrentNode = "svc", VarsJson = "{}" };
        db.Wf_FlowInstances.Add(inst);
        var tok = eng.SpawnToken(inst, svcNode);
        db.Wf_ServiceJobs.Add(new Wf_ServiceJob { Id = Guid.NewGuid(), InstanceId = inst.Id, TokenId = tok.Id,
            NodeId = "svc", Kind = ServiceKind.Timer, ActionRefJson = ServiceTaskActionRef.Snapshot(svcNode),
            DueAtUtc = T0, Status = ServiceJobStatus.Pending, AttemptCount = 0, MaxAttempts = 4,
            NextAttemptAtUtc = T0, CreateDate = DateTime.UtcNow });
        await db.SaveChangesAsync();

        await Svc(db, eng, exec).ScanOnceAsync(T0, "w1");

        Assert.Equal(0, exec.CallCount);                       // 纯等待：无 executor 调用
        var j = await db.Wf_ServiceJobs.SingleAsync();
        Assert.Equal(ServiceJobStatus.Succeeded, j.Status);
        Assert.False(await db.Wf_FlowTokens.AnyAsync(t => t.NodeId == "svc" && t.Status == FlowTokenStatus.Active));   // token 推进
        Assert.Equal(FlowInstanceStatus.Approved, (await db.Wf_FlowInstances.SingleAsync()).Status);
    }
}
