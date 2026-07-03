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

/// <summary>A-T6：ServiceTaskNodeHandler（第 6 个 INodeHandler）。
/// sync 内联一击（成功 advance / 失败降级入队 AttemptCount=1，P0-1）；async/timer 停泊入队（AttemptCount=0）；
/// EnqueueServiceJob 防重（P0-3，Local + DB localIds 排除式）；timer ComputeDueUtc（duration / untilDate→UTC，P0-6）。</summary>
public class ServiceTaskHandlerTests
{
    // ── 可控执行器：按 succeed 返回 Ok/Fail，记录调用次数 ──
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

    private static FlowEngine EngineWith(CP6Context db, params IServiceTaskExecutor[] execs)
        => new(db, new ApproverResolver(db), handlers: new INodeHandler[]
        {
            new StartNodeHandler(), new ApprovalNodeHandler(), new EndNodeHandler(),
            new ParallelSplitNodeHandler(), new ParallelJoinNodeHandler(),
            new ServiceTaskNodeHandler(execs),
        });

    // start → svc(serviceTask) → end
    private static FlowSchema SvcSchema(Action<FlowNode> configureSvc)
    {
        var svc = new FlowNode { Id = "svc", Type = "serviceTask" };
        configureSvc(svc);
        return new FlowSchema
        {
            Start = "s",
            Nodes =
            {
                new FlowNode { Id = "s", Type = "start" },
                svc,
                new FlowNode { Id = "end", Type = "end" },
            },
            Edges =
            {
                new FlowEdge { From = "s", To = "svc" },
                new FlowEdge { From = "svc", To = "end" },
            },
        };
    }

    private static async Task<Guid> SubmitAsync(CP6Context db, FlowEngine eng, FlowSchema schema, string vars = "{}")
    {
        db.Wf_FlowDefs.Add(new Wf_FlowDef { Id = Guid.NewGuid(), FlowKey = "svcflow", FlowName = "s", FormKey = "f",
            SchemaJson = JsonSerializer.Serialize(schema), Version = 1, Enable = true });
        await db.SaveChangesAsync();
        return await eng.SubmitAsync("svcflow", Guid.NewGuid(), vars);
    }

    [Fact]
    public async Task Sync_Success_AdvancesToken_NoJob()
    {
        using var db = NewDb();
        var eng = EngineWith(db, new FakeExec("act", succeed: true));
        var schema = SvcSchema(n => { n.ServiceKind = ServiceKind.DataWriteback; n.ServiceActionName = "act"; });

        await SubmitAsync(db, eng, schema);

        Assert.Equal(0, await db.Wf_ServiceJobs.CountAsync());                                  // 无 job
        Assert.False(await db.Wf_FlowTokens.AnyAsync(t => t.NodeId == "svc" && t.Status == FlowTokenStatus.Active)); // 离开 svc
        Assert.Equal(FlowInstanceStatus.Approved, (await db.Wf_FlowInstances.SingleAsync()).Status);
    }

    [Fact]
    public async Task Sync_Fail_EnqueuesJob_AttemptCount1_ParksToken()
    {
        using var db = NewDb();
        var eng = EngineWith(db, new FakeExec("act", succeed: false));
        var schema = SvcSchema(n => { n.ServiceKind = ServiceKind.DataWriteback; n.ServiceActionName = "act"; });

        await SubmitAsync(db, eng, schema);

        var job = await db.Wf_ServiceJobs.SingleAsync();
        Assert.Equal(ServiceJobStatus.Pending, job.Status);
        Assert.Equal(1, job.AttemptCount);            // P0-1：sync 那一击算 attempt 1
        Assert.Equal(4, job.MaxAttempts);             // 默认 retries=3 → maxAttempts=4
        Assert.Equal(ServiceKind.DataWriteback, job.Kind);
        // token 仍停泊 svc，实例仍进行中
        Assert.True(await db.Wf_FlowTokens.AnyAsync(t => t.NodeId == "svc" && t.Status == FlowTokenStatus.Active));
        Assert.Equal(FlowInstanceStatus.Running, (await db.Wf_FlowInstances.SingleAsync()).Status);
    }

    [Fact]
    public async Task Async_Enqueues_AttemptCount0_ParksToken()
    {
        using var db = NewDb();
        var eng = EngineWith(db);   // webApi async 不在 handler 内调 executor
        var schema = SvcSchema(n => { n.ServiceKind = ServiceKind.WebApi; n.ServiceConnectorName = "erpEcho"; n.ServicePath = "/x"; });

        var before = DateTime.UtcNow;
        await SubmitAsync(db, eng, schema);

        var job = await db.Wf_ServiceJobs.SingleAsync();
        Assert.Equal(ServiceJobStatus.Pending, job.Status);
        Assert.Equal(0, job.AttemptCount);            // async 首执未发生
        Assert.Equal(ServiceKind.WebApi, job.Kind);
        Assert.InRange(job.DueAtUtc, before.AddSeconds(-5), DateTime.UtcNow.AddSeconds(5));   // ≈ now
        Assert.True(await db.Wf_FlowTokens.AnyAsync(t => t.NodeId == "svc" && t.Status == FlowTokenStatus.Active));
        Assert.Equal(FlowInstanceStatus.Running, (await db.Wf_FlowInstances.SingleAsync()).Status);
    }

    [Fact]
    public async Task Timer_Enqueues_FutureDueAt()
    {
        using var db = NewDb();
        var eng = EngineWith(db);
        var schema = SvcSchema(n => { n.ServiceKind = ServiceKind.Timer; n.ServiceDelayMode = "duration"; n.ServiceDelayValue = "PT2H"; });

        var before = DateTime.UtcNow;
        await SubmitAsync(db, eng, schema);

        var job = await db.Wf_ServiceJobs.SingleAsync();
        Assert.Equal(ServiceKind.Timer, job.Kind);
        Assert.Equal(0, job.AttemptCount);
        // DueAtUtc ≈ now + 2h
        Assert.InRange(job.DueAtUtc, before.AddHours(2).AddSeconds(-5), DateTime.UtcNow.AddHours(2).AddSeconds(5));
    }

    [Fact]
    public async Task Timer_UntilDate_TenantTz_ToUtc()
    {
        using var db = NewDb();
        var eng = EngineWith(db);
        var schema = SvcSchema(n => { n.ServiceKind = ServiceKind.Timer; n.ServiceDelayMode = "untilDate"; n.ServiceDelayValue = "2026-07-01"; });

        await SubmitAsync(db, eng, schema);

        var job = await db.Wf_ServiceJobs.SingleAsync();
        // app-default tz = server local tz（TimeZoneInfo.Local）；untilDate 按该 tz 解释 → UTC
        var localMidnight = DateTime.SpecifyKind(new DateTime(2026, 7, 1, 0, 0, 0), DateTimeKind.Unspecified);
        var expectedUtc = TimeZoneInfo.ConvertTimeToUtc(localMidnight, TimeZoneInfo.Local);
        Assert.Equal(expectedUtc, job.DueAtUtc);      // 正确 UTC 时刻（已转换，非裸存本地午夜）
    }

    [Fact]
    public async Task Enqueue_Dedupe_SameTokenNode()
    {
        using var db = NewDb();
        var eng = EngineWith(db);
        var schema = SvcSchema(n => { n.ServiceKind = ServiceKind.WebApi; n.ServiceConnectorName = "erpEcho"; n.ServicePath = "/x"; });
        var node = schema.Nodes.Single(n => n.Id == "svc");

        // 手工停泊 token@svc（不经 Submit），两次 OnEnter 之间不 SaveChanges → 检验 Local 变更追踪器去重路径（P0-3）
        var inst = new Wf_FlowInstance { Id = Guid.NewGuid(), FlowKey = "svcflow",
            StarterId = Guid.NewGuid(), Status = FlowInstanceStatus.Running, CurrentNode = "svc", VarsJson = "{}" };
        db.Wf_FlowInstances.Add(inst);
        var token = eng.SpawnToken(inst, node);

        var handler = new ServiceTaskNodeHandler(Array.Empty<IServiceTaskExecutor>());
        var ctx = new NodeContext { Inst = inst, Schema = schema, Node = node, Token = token, Engine = eng };
        await handler.OnEnterAsync(ctx);   // 入队 job #1（未 SaveChanges，仅在 Local）
        await handler.OnEnterAsync(ctx);   // 去重命中 Local → 不重复入队
        await db.SaveChangesAsync();

        Assert.Equal(1, await db.Wf_ServiceJobs.CountAsync(j =>
            j.Status == ServiceJobStatus.Pending || j.Status == ServiceJobStatus.Running));
    }
}
