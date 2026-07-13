// CP6.Tests/Wf/FlowTriggerAdminTests.cs —— 服务层，基座同 A-T2
using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using CP6.Core.EFDbContext;
using CP6.Core.Services.Wf;
using CP6.Entity.DomainModels.Wf;
using Microsoft.EntityFrameworkCore;
using Xunit;
using static CP6.Tests.FlowTriggerTestHarness;

namespace CP6.Tests;

public class FlowTriggerAdminTests
{
    private static FlowTriggerAdminService Admin(CP6Context db) => new(db, Service(db));

    private static FlowTriggerSaveReq Req(int type, Guid starter, string configJson,
        string flowKey = "fk-trig", bool enabled = true, string? eventKey = null)
        => new(flowKey, type, configJson, enabled, eventKey, starter);

    [Fact]
    public async Task Create_Timer_ComputesInitialNextDue()
    {
        using var conn = NewSqliteWithSchema();
        var (starter, _) = await SeedFlowAndUsersAsync(conn);
        using var db = Ctx(conn);

        var (id, plain) = await Admin(db).CreateAsync(
            Req(WfTriggerType.Timer, starter, "{\"cron\":\"0 9 * * *\"}"), CancellationToken.None);

        Assert.Null(plain);                                // timer 无 key
        var row = await db.Wf_FlowTriggers.AsNoTracking().SingleAsync(t => t.Id == id);
        Assert.NotNull(row.NextDueUtc);
        Assert.True(row.NextDueUtc > DateTime.UtcNow.AddMinutes(-1));   // 初始 NextDue 已上膛且非过去
    }

    [Fact]
    public async Task Create_Message_ReturnsPlainKeyOnce_StoresOnlyHash()
    {
        using var conn = NewSqliteWithSchema();
        var (starter, _) = await SeedFlowAndUsersAsync(conn);
        using var db = Ctx(conn);

        var (id, plain) = await Admin(db).CreateAsync(
            Req(WfTriggerType.Message, starter, "{\"varsSchema\":[\"orderNo\"]}"), CancellationToken.None);

        Assert.False(string.IsNullOrEmpty(plain));
        var row = await db.Wf_FlowTriggers.AsNoTracking().SingleAsync(t => t.Id == id);
        Assert.Equal(WfApiKeyHelper.HashOf(plain!), row.ApiKeyHash);   // 库中只有哈希
        Assert.NotEqual(plain, row.ApiKeyHash);                        // 明文不落库（spec §3.4）
        Assert.DoesNotContain(plain!, row.ConfigJson);
    }

    [Fact]
    public async Task Update_Timer_CronChanged_RecomputesNextDue()
    {
        using var conn = NewSqliteWithSchema();
        var (starter, _) = await SeedFlowAndUsersAsync(conn);
        using var db = Ctx(conn);
        var admin = Admin(db);
        var (id, _) = await admin.CreateAsync(
            Req(WfTriggerType.Timer, starter, "{\"cron\":\"0 9 * * *\"}"), CancellationToken.None);

        // 改成「每年 1 月 1 日」→ NextDue 必落在 1 月 1 日（与每日 cron 不可能撞同一到期语义）
        await admin.UpdateAsync(id,
            Req(WfTriggerType.Timer, starter, "{\"cron\":\"0 0 1 1 *\"}"), CancellationToken.None);

        var row = await db.Wf_FlowTriggers.AsNoTracking().SingleAsync(t => t.Id == id);
        var local = TimeZoneInfo.ConvertTimeFromUtc(row.NextDueUtc!.Value, TimeZoneInfo.Local);
        Assert.Equal(1, local.Month);
        Assert.Equal(1, local.Day);
    }

    [Fact]
    public async Task Update_NeverReturnsKey_KeepsHash()
    {
        using var conn = NewSqliteWithSchema();
        var (starter, _) = await SeedFlowAndUsersAsync(conn);
        using var db = Ctx(conn);
        var admin = Admin(db);
        var (id, plain) = await admin.CreateAsync(
            Req(WfTriggerType.Message, starter, "{\"varsSchema\":[\"orderNo\"]}"), CancellationToken.None);
        var hashBefore = (await db.Wf_FlowTriggers.AsNoTracking().SingleAsync(t => t.Id == id)).ApiKeyHash;

        // UpdateAsync 返回 Task（编译期即保证不回明文）；改 varsSchema 不动 key
        await admin.UpdateAsync(id,
            Req(WfTriggerType.Message, starter, "{\"varsSchema\":[\"orderNo\",\"amount\"]}"), CancellationToken.None);

        var row = await db.Wf_FlowTriggers.AsNoTracking().SingleAsync(t => t.Id == id);
        Assert.Equal(hashBefore, row.ApiKeyHash);          // hash 不变，旧明文仍有效
        Assert.True(WfApiKeyHelper.Verify(plain!, row.ApiKeyHash));
    }

    [Fact]
    public async Task ResetKey_NewPlain_OldKeyInvalid()
    {
        using var conn = NewSqliteWithSchema();
        var (starter, _) = await SeedFlowAndUsersAsync(conn);
        using var db = Ctx(conn);
        var admin = Admin(db);
        var (id, oldPlain) = await admin.CreateAsync(
            Req(WfTriggerType.Message, starter, "{\"varsSchema\":[]}"), CancellationToken.None);

        var newPlain = await admin.ResetKeyAsync(id, CancellationToken.None);

        var row = await db.Wf_FlowTriggers.AsNoTracking().SingleAsync(t => t.Id == id);
        Assert.False(WfApiKeyHelper.Verify(oldPlain!, row.ApiKeyHash));   // 旧 key 即刻失效
        Assert.True(WfApiKeyHelper.Verify(newPlain, row.ApiKeyHash));
        Assert.NotEqual(oldPlain, newPlain);
    }

    [Fact]
    public async Task ResetKey_OnNonMessage_Throws()
    {
        using var conn = NewSqliteWithSchema();
        var (starter, _) = await SeedFlowAndUsersAsync(conn);
        using var db = Ctx(conn);
        var admin = Admin(db);
        var (id, _) = await admin.CreateAsync(
            Req(WfTriggerType.Timer, starter, "{\"cron\":\"0 9 * * *\"}"), CancellationToken.None);

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(
            () => admin.ResetKeyAsync(id, CancellationToken.None));
        Assert.Contains("E-WF-022", ex.Message);
    }

    [Fact]
    public async Task ManualFire_UsesManualKey_CreatesInstance()
    {
        using var conn = NewSqliteWithSchema();
        var (starter, _) = await SeedFlowAndUsersAsync(conn);
        using var db = Ctx(conn);
        var admin = Admin(db);
        var (id, _) = await admin.CreateAsync(
            Req(WfTriggerType.Timer, starter, "{\"cron\":\"0 9 * * *\"}"), CancellationToken.None);

        var r1 = await admin.ManualFireAsync(id, CancellationToken.None);
        var r2 = await admin.ManualFireAsync(id, CancellationToken.None);   // 手动键每次新 GUID → 再发一单

        Assert.True(r1.Success);
        Assert.True(r2.Success);
        Assert.NotEqual(r1.InstanceId, r2.InstanceId);
        var fires = await db.Wf_TriggerFires.AsNoTracking().ToListAsync();
        Assert.Equal(2, fires.Count);
        Assert.All(fires, f => Assert.StartsWith("manual:", f.IdempotencyKey));   // spec §4 手动试发键
        Assert.Equal(2, await db.Wf_FlowInstances.CountAsync());
    }

    [Fact]
    public async Task ListFires_ReturnsRecent_DescByFiredUtc()
    {
        using var conn = NewSqliteWithSchema();
        var (starter, _) = await SeedFlowAndUsersAsync(conn);
        using var db = Ctx(conn);
        var admin = Admin(db);
        var (id, _) = await admin.CreateAsync(
            Req(WfTriggerType.Timer, starter, "{\"cron\":\"0 9 * * *\"}"), CancellationToken.None);
        var baseUtc = DateTime.UtcNow;
        for (var i = 0; i < 3; i++)
            db.Wf_TriggerFires.Add(new Wf_TriggerFire
            {
                TriggerId = id, IdempotencyKey = $"k{i}",
                FiredUtc = baseUtc.AddMinutes(-i), Source = WfTriggerType.Timer,
            });
        await db.SaveChangesAsync();

        var list = await admin.ListFiresAsync(id, take: 2, CancellationToken.None);

        Assert.Equal(2, list.Count);
        Assert.Equal("k0", list[0].IdempotencyKey);        // 最新在前
        Assert.Equal("k1", list[1].IdempotencyKey);
        Assert.True(list[0].FiredUtc > list[1].FiredUtc);
    }

    [Fact]
    public async Task SetEnabled_Toggles()
    {
        using var conn = NewSqliteWithSchema();
        var (starter, _) = await SeedFlowAndUsersAsync(conn);
        using var db = Ctx(conn);
        var admin = Admin(db);
        var (id, _) = await admin.CreateAsync(
            Req(WfTriggerType.Timer, starter, "{\"cron\":\"0 9 * * *\"}"), CancellationToken.None);

        await admin.SetEnabledAsync(id, false, CancellationToken.None);

        Assert.False((await db.Wf_FlowTriggers.AsNoTracking().SingleAsync(t => t.Id == id)).Enabled);
    }
}
