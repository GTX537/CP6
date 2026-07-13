// CP6.Tests/Wf/WfTriggerBridgeHookTests.cs —— 基座同 A-T2；hook 用真 FlowTriggerService 构造
using System;
using System.Threading.Tasks;
using CP6.Core.EFDbContext;
using CP6.Core.Services.Wf;
using CP6.Entity.DomainModels.Integration;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;
using static CP6.Tests.FlowTriggerTestHarness;

namespace CP6.Tests;

public class WfTriggerBridgeHookTests
{
    private const string EventKey = "WMS|OnShipmentConfirmedAsync";

    private static WfTriggerBridgeHook Hook(CP6Context db)
        => new(db, Service(db), NullLogger<WfTriggerBridgeHook>.Instance);

    [Fact]
    public async Task OnEvent_MatchesMany_FiresEach_WithPerTriggerKey()
    {
        using var conn = NewSqliteWithSchema();
        var (starter, _) = await SeedFlowAndUsersAsync(conn);
        using var db = Ctx(conn);
        for (var i = 0; i < 3; i++)
            db.Wf_FlowTriggers.Add(NewTrigger("fk-trig", WfTriggerType.Event, starter, eventKey: EventKey));
        await db.SaveChangesAsync();

        var r = await Hook(db).OnEventAsync(EventKey, "EV-1", "{}", "u");

        Assert.True(r.Success);
        Assert.Equal(3, r.MatchedCount);
        Assert.Equal(3, r.FiredCount);
        Assert.Equal(3, await db.Wf_FlowInstances.CountAsync());
        var fires = await db.Wf_TriggerFires.AsNoTracking().ToListAsync();
        Assert.Equal(3, fires.Count);
        foreach (var f in fires)
            Assert.Equal($"EV-1:{f.TriggerId}", f.IdempotencyKey);   // 触发器粒度幂等键（spec §2.2）
        var evt = await db.IntegrationEvents.AsNoTracking().SingleAsync();   // outbox 台账恰 1 行
        Assert.Equal(IntegrationEventStatus.Success, evt.Status);
        Assert.Equal("WF", evt.TargetModule);
        Assert.Equal("WMS", evt.SourceModule);
    }

    [Fact]
    public async Task OnEvent_NoMatch_ZeroAction_SkippedRow()
    {
        using var conn = NewSqliteWithSchema();
        await SeedFlowAndUsersAsync(conn);
        using var db = Ctx(conn);

        var r = await Hook(db).OnEventAsync("MES|OnNobodyListensAsync", "EV-2", "{}", null);

        Assert.True(r.Success);                            // 未匹配零动作不是错误（spec §8）
        Assert.Equal(0, r.MatchedCount);
        Assert.Equal(0, r.FiredCount);
        Assert.Equal(0, await db.Wf_FlowInstances.CountAsync());
        var evt = await db.IntegrationEvents.AsNoTracking().SingleAsync();   // 审计 Skipped 行
        Assert.Equal(IntegrationEventStatus.Skipped, evt.Status);
    }

    [Fact]
    public async Task OnEvent_MissingEventId_Failed_NoOutbox()
    {
        using var conn = NewSqliteWithSchema();
        var (starter, _) = await SeedFlowAndUsersAsync(conn);
        using var db = Ctx(conn);
        db.Wf_FlowTriggers.Add(NewTrigger("fk-trig", WfTriggerType.Event, starter, eventKey: EventKey));
        await db.SaveChangesAsync();

        var r = await Hook(db).OnEventAsync(EventKey, "", "{}", null);

        Assert.False(r.Success);                           // eventId 必填（幂等键素材，spec §3.3）
        Assert.Equal(0, await db.Wf_FlowInstances.CountAsync());
        Assert.Equal(0, await db.IntegrationEvents.CountAsync());   // 重试同样缺 → 不进 outbox
    }

    [Fact]
    public async Task OnEvent_VarsMap_Applied()
    {
        using var conn = NewSqliteWithSchema();
        var (starter, _) = await SeedFlowAndUsersAsync(conn);
        using var db = Ctx(conn);
        db.Wf_FlowTriggers.Add(NewTrigger("fk-trig", WfTriggerType.Event, starter,
            configJson: "{\"varsMap\":{\"orderNo\":\"$.OutboundNo\"}}", eventKey: EventKey));
        await db.SaveChangesAsync();

        var r = await Hook(db).OnEventAsync(EventKey, "EV-3", "{\"OutboundNo\":\"OB-9\"}", "u");

        Assert.True(r.Success);
        var inst = await db.Wf_FlowInstances.AsNoTracking().SingleAsync();
        Assert.Contains("\"orderNo\":\"OB-9\"", inst.VarsJson);   // varsMap 点路径映射进流程变量
    }

    [Fact]
    public async Task OnEvent_PartialFail_OutboxFailed_ReplayTopsUpOnlyMissing()
    {
        // spec §8 关键测试：3 触发器发 2 成 1 败 → 重放仅补 1，已发的撞键幂等跳过
        using var conn = NewSqliteWithSchema();
        var (starter, _) = await SeedFlowAndUsersAsync(conn);                       // fk-trig enabled
        await SeedFlowAndUsersAsync(conn, flowKey: "fk-off", flowEnabled: false);   // fk-off 停用
        using var db = Ctx(conn);
        db.Wf_FlowTriggers.Add(NewTrigger("fk-trig", WfTriggerType.Event, starter, eventKey: EventKey));
        db.Wf_FlowTriggers.Add(NewTrigger("fk-trig", WfTriggerType.Event, starter, eventKey: EventKey));
        db.Wf_FlowTriggers.Add(NewTrigger("fk-off", WfTriggerType.Event, starter, eventKey: EventKey));
        await db.SaveChangesAsync();
        var hook = Hook(db);

        var r1 = await hook.OnEventAsync(EventKey, "EV-4", "{}", "u");

        Assert.False(r1.Success);                          // 部分失败
        Assert.Equal(2, await db.Wf_FlowInstances.CountAsync());
        var failedEvt = await db.IntegrationEvents.AsNoTracking()
            .SingleAsync(e => e.Status == IntegrationEventStatus.Failed);
        Assert.Contains("EV-4", failedEvt.PayloadJson);    // eventId 随负载持久化供重放复用（spec §2.2）
        var outboxBefore = await db.IntegrationEvents.CountAsync();

        // 修复：启用 fk-off → dispatcher 重放路径（ReplayEventAsync，同 eventKey/eventId/payload）
        using (var fix = Ctx(conn))
        {
            (await fix.Wf_FlowDefs.SingleAsync(d => d.FlowKey == "fk-off")).Enable = true;
            await fix.SaveChangesAsync();
        }
        var r2 = await hook.ReplayEventAsync(EventKey, "EV-4", "{}", "u");

        Assert.True(r2.Success);
        Assert.Equal(3, await db.Wf_FlowInstances.CountAsync());   // 只补第 3 个，前 2 个幂等跳过
        Assert.Equal(3, await db.Wf_TriggerFires.CountAsync());
        Assert.Equal(outboxBefore, await db.IntegrationEvents.CountAsync());   // 重放不再新写 outbox 行
    }

    [Fact]
    public async Task Replay_DoesNotWriteNewOutboxRow()
    {
        using var conn = NewSqliteWithSchema();
        var (starter, _) = await SeedFlowAndUsersAsync(conn);
        using var db = Ctx(conn);
        db.Wf_FlowTriggers.Add(NewTrigger("fk-trig", WfTriggerType.Event, starter, eventKey: EventKey));
        await db.SaveChangesAsync();

        var r = await Hook(db).ReplayEventAsync(EventKey, "EV-5", "{}", null);

        Assert.True(r.Success);
        Assert.Equal(1, await db.Wf_FlowInstances.CountAsync());
        Assert.Equal(0, await db.IntegrationEvents.CountAsync());   // 重放入口零 outbox 写入（映射表⑦）
    }
}
