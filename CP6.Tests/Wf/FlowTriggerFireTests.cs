// CP6.Tests/Wf/FlowTriggerFireTests.cs
using System;
using System.Threading;
using System.Threading.Tasks;
using CP6.Core.Services.Wf;
using CP6.Entity.DomainModels.Wf;
using Microsoft.EntityFrameworkCore;
using Xunit;
using static CP6.Tests.FlowTriggerTestHarness;

namespace CP6.Tests;

public class FlowTriggerFireTests
{
    [Fact]
    public async Task Fire_Success_CreatesInstance_WritesFire_UpdatesLastFired()
    {
        using var conn = NewSqliteWithSchema();
        var (starter, _) = await SeedFlowAndUsersAsync(conn);
        using var db = Ctx(conn);
        var trig = NewTrigger("fk-trig", WfTriggerType.Message, starter);
        db.Wf_FlowTriggers.Add(trig);
        await db.SaveChangesAsync();

        var r = await Service(db).FireAsync(trig, "{}", WfTriggerType.Message, "k1", CancellationToken.None);

        Assert.True(r.Success);
        Assert.False(r.Replayed);
        Assert.NotNull(r.InstanceId);
        Assert.Equal(1, await db.Wf_FlowInstances.CountAsync());
        var fire = await db.Wf_TriggerFires.AsNoTracking().SingleAsync();
        Assert.Equal(r.InstanceId, fire.InstanceId);
        Assert.Null(fire.Error);
        Assert.Equal(WfTriggerType.Message, fire.Source);
        Assert.Equal("k1", fire.IdempotencyKey);
        Assert.NotNull((await db.Wf_FlowTriggers.AsNoTracking().SingleAsync()).LastFiredUtc);
    }

    [Fact]
    public async Task Fire_SameKey_Replays_ExistingInstance_NoSecondInstance()
    {
        using var conn = NewSqliteWithSchema();
        var (starter, _) = await SeedFlowAndUsersAsync(conn);
        using var db = Ctx(conn);
        var trig = NewTrigger("fk-trig", WfTriggerType.Message, starter);
        db.Wf_FlowTriggers.Add(trig);
        await db.SaveChangesAsync();
        var svc = Service(db);

        var r1 = await svc.FireAsync(trig, "{}", WfTriggerType.Message, "k1", CancellationToken.None);
        var r2 = await svc.FireAsync(trig, "{}", WfTriggerType.Message, "k1", CancellationToken.None);

        Assert.True(r2.Success);
        Assert.True(r2.Replayed);                        // 幂等成功不是错误（spec §3.1/§8）
        Assert.Equal(r1.InstanceId, r2.InstanceId);
        Assert.Equal(1, await db.Wf_FlowInstances.CountAsync());
        Assert.Equal(1, await db.Wf_TriggerFires.CountAsync());
    }

    [Fact]
    public async Task Fire_Disabled_Rejected_NoFireRow()
    {
        using var conn = NewSqliteWithSchema();
        var (starter, _) = await SeedFlowAndUsersAsync(conn);
        using var db = Ctx(conn);
        var trig = NewTrigger("fk-trig", WfTriggerType.Message, starter, enabled: false);
        db.Wf_FlowTriggers.Add(trig);
        await db.SaveChangesAsync();

        var r = await Service(db).FireAsync(trig, "{}", WfTriggerType.Message, "k1", CancellationToken.None);

        Assert.False(r.Success);
        Assert.Equal(0, await db.Wf_TriggerFires.CountAsync());   // Enabled 检查先于幂等闸（spec §3.1 顺序）
        Assert.Equal(0, await db.Wf_FlowInstances.CountAsync());
    }

    [Fact]
    public async Task Fire_StarterDisabled_EWF022_ErrorBackfilled()
    {
        using var conn = NewSqliteWithSchema();
        var (starter, _) = await SeedFlowAndUsersAsync(conn, starterEnabled: false);
        using var db = Ctx(conn);
        var trig = NewTrigger("fk-trig", WfTriggerType.Message, starter);
        db.Wf_FlowTriggers.Add(trig);
        await db.SaveChangesAsync();

        var r = await Service(db).FireAsync(trig, "{}", WfTriggerType.Message, "k1", CancellationToken.None);

        Assert.False(r.Success);
        Assert.Contains("E-WF-022", r.Error);
        var fire = await db.Wf_TriggerFires.AsNoTracking().SingleAsync();
        Assert.Contains("E-WF-022", fire.Error);          // 流水行保留供排障
        Assert.Null(fire.InstanceId);
        Assert.Equal(0, await db.Wf_FlowInstances.CountAsync());
    }

    [Fact]
    public async Task Fire_FlowDisabled_EWF023_ErrorBackfilled()
    {
        using var conn = NewSqliteWithSchema();
        var (starter, _) = await SeedFlowAndUsersAsync(conn, flowEnabled: false);
        using var db = Ctx(conn);
        var trig = NewTrigger("fk-trig", WfTriggerType.Message, starter);
        db.Wf_FlowTriggers.Add(trig);
        await db.SaveChangesAsync();

        var r = await Service(db).FireAsync(trig, "{}", WfTriggerType.Message, "k1", CancellationToken.None);

        Assert.False(r.Success);
        Assert.Contains("E-WF-023", r.Error);
        Assert.Contains("E-WF-023", (await db.Wf_TriggerFires.AsNoTracking().SingleAsync()).Error);
    }

    [Fact]
    public async Task Fire_SubmitThrows_EWF024_ErrorBackfilled_RowKept()
    {
        using var conn = NewSqliteWithSchema();
        var (starter, _) = await SeedFlowAndUsersAsync(conn);   // 先 seed 合法用户
        using (var seed = Ctx(conn))
        {
            // 空 schema（无节点）的 enabled 流程 → SubmitAsync 抛"无节点" → E-WF-024 包装
            seed.Wf_FlowDefs.Add(new Wf_FlowDef
            {
                Id = Guid.NewGuid(), FlowKey = "fk-bad", FlowName = "fk-bad", FormKey = "f",
                SchemaJson = "{\"Start\":null,\"Nodes\":[],\"Edges\":[]}", Version = 1, Enable = true,
            });
            await seed.SaveChangesAsync();
        }
        using var db = Ctx(conn);
        var trig = NewTrigger("fk-bad", WfTriggerType.Message, starter);
        db.Wf_FlowTriggers.Add(trig);
        await db.SaveChangesAsync();

        var r = await Service(db).FireAsync(trig, "{}", WfTriggerType.Message, "k1", CancellationToken.None);

        Assert.False(r.Success);
        Assert.Contains("E-WF-024", r.Error);
        var fire = await db.Wf_TriggerFires.AsNoTracking().SingleAsync();
        Assert.Contains("E-WF-024", fire.Error);          // 流水行保留 Error 回填（spec §3.1）
        Assert.Null(fire.InstanceId);
        Assert.Equal(0, await db.Wf_FlowInstances.CountAsync());
    }

    [Fact]
    public async Task Fire_ResumesUnfinishedSlot_BackfillsInstance()
    {
        using var conn = NewSqliteWithSchema();
        var (starter, _) = await SeedFlowAndUsersAsync(conn);
        using var db = Ctx(conn);
        var trig = NewTrigger("fk-trig", WfTriggerType.Timer, starter);
        db.Wf_FlowTriggers.Add(trig);
        await db.SaveChangesAsync();
        // 预插占坑行（模拟第一段已提交、第二段未跑）
        db.Wf_TriggerFires.Add(new Wf_TriggerFire
        {
            TriggerId = trig.Id, IdempotencyKey = "slot-1",
            FiredUtc = DateTime.UtcNow.AddMinutes(-5), Source = WfTriggerType.Timer,
        });
        await db.SaveChangesAsync();

        var r = await Service(db).FireAsync(trig, "{}", WfTriggerType.Timer, "slot-1", CancellationToken.None);

        Assert.True(r.Success);
        var fire = await db.Wf_TriggerFires.AsNoTracking().SingleAsync();   // 复用该行，不新增
        Assert.Equal(r.InstanceId, fire.InstanceId);
        Assert.Equal(1, await db.Wf_FlowInstances.CountAsync());
    }

    [Fact]
    public async Task Fire_RetriesFailedSlot_ClearsError()
    {
        using var conn = NewSqliteWithSchema();
        var (starter, _) = await SeedFlowAndUsersAsync(conn, flowEnabled: false);
        using var db = Ctx(conn);
        var trig = NewTrigger("fk-trig", WfTriggerType.Event, starter);
        db.Wf_FlowTriggers.Add(trig);
        await db.SaveChangesAsync();
        var svc = Service(db);

        // 第一发：流程停用 → E-WF-023 失败流水
        var r1 = await svc.FireAsync(trig, "{}", WfTriggerType.Event, "ev-1:k", CancellationToken.None);
        Assert.False(r1.Success);

        // 启用流程 → 同 key 重发（event outbox 重放 / message 客户端重试语义，映射表⑦）
        using (var fix = Ctx(conn))
        {
            (await fix.Wf_FlowDefs.SingleAsync(d => d.FlowKey == "fk-trig")).Enable = true;
            await fix.SaveChangesAsync();
        }
        var r2 = await svc.FireAsync(trig, "{}", WfTriggerType.Event, "ev-1:k", CancellationToken.None);

        Assert.True(r2.Success);
        var fire = await db.Wf_TriggerFires.AsNoTracking().SingleAsync();   // 同一行：Error 清空、InstanceId 回填
        Assert.Null(fire.Error);
        Assert.Equal(r2.InstanceId, fire.InstanceId);
        Assert.Equal(1, await db.Wf_FlowInstances.CountAsync());
    }

    [Fact]
    public async Task Fire_PayloadHash_SetForNonTimer()
    {
        using var conn = NewSqliteWithSchema();
        var (starter, _) = await SeedFlowAndUsersAsync(conn);
        using var db = Ctx(conn);
        var msgTrig = NewTrigger("fk-trig", WfTriggerType.Message, starter);
        var timerTrig = NewTrigger("fk-trig", WfTriggerType.Timer, starter);
        db.Wf_FlowTriggers.AddRange(msgTrig, timerTrig);
        await db.SaveChangesAsync();
        var svc = Service(db);

        await svc.FireAsync(msgTrig, "{\"a\":1}", WfTriggerType.Message, "km", CancellationToken.None);
        await svc.FireAsync(timerTrig, "{}", WfTriggerType.Timer, "kt", CancellationToken.None);

        var msgFire = await db.Wf_TriggerFires.AsNoTracking().SingleAsync(f => f.TriggerId == msgTrig.Id);
        var timerFire = await db.Wf_TriggerFires.AsNoTracking().SingleAsync(f => f.TriggerId == timerTrig.Id);
        Assert.NotNull(msgFire.PayloadHash);
        Assert.Equal(64, msgFire.PayloadHash!.Length);     // SHA-256 hex
        Assert.Null(timerFire.PayloadHash);                // timer 无负载哈希（spec §2.2）
    }
}
