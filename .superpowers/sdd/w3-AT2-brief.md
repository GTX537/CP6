### Task A-T2: WfTriggerConfig 分型解析 + `IFlowTriggerService.FireAsync`（幂等闸+占坑复用+运行时双检+SubmitAsync 接缝）+ DI

> **D2 落点，本波最关键的正确性任务。** FireAsync 是三入口唯一出口；撞键语义见「共享契约」末条。

**Files:**
- Create: `CP6.Core/Services/Wf/WfTriggerConfig.cs`
- Create: `CP6.Core/Services/Wf/FlowTriggerService.cs`（`IFlowTriggerService` + `TriggerFireResult` + 实现；ScanTimersOnceAsync 本任务先抛 `NotImplementedException`——B-T2 实现，接口先立全）
- Modify: `CP6.WebApi/Program.cs`（DI：`AddScoped<IFlowTriggerService, FlowTriggerService>()`，放 `:107-108` FlowEngine 注册同块）
- Test: `CP6.Tests/Wf/FlowTriggerTestHarness.cs`（共享基座，本波所有 SQLite 测试复用）、`CP6.Tests/Wf/FlowTriggerConfigTests.cs`、`CP6.Tests/Wf/FlowTriggerFireTests.cs`

- [ ] **Step 1: 写失败测试（config 解析，纯逻辑）**

```csharp
// CP6.Tests/Wf/FlowTriggerConfigTests.cs
using CP6.Core.Services.Wf;
using Xunit;

public class FlowTriggerConfigTests
{
    [Fact]
    public void ParseTimer_ReadsCronAndVars()
    {
        var c = WfTriggerConfig.ParseTimer("{\"cron\":\"0 0 25 * *\",\"varsJson\":\"{\\\"a\\\":1}\"}");
        Assert.Equal("0 0 25 * *", c.Cron);
        Assert.Equal("{\"a\":1}", c.VarsJson);
    }

    [Fact]
    public void ParseEvent_ReadsVarsMap()
    {
        var c = WfTriggerConfig.ParseEvent("{\"varsMap\":{\"orderNo\":\"$.OutboundNo\"}}");
        Assert.Equal("$.OutboundNo", c.VarsMap!["orderNo"]);
    }

    [Fact]
    public void ParseMessage_ReadsVarsSchema()
    {
        var c = WfTriggerConfig.ParseMessage("{\"varsSchema\":[\"orderNo\",\"amount\"]}");
        Assert.Equal(new[] { "orderNo", "amount" }, c.VarsSchema);
    }

    [Fact]
    public void Parse_EmptyOrBadJson_YieldsEmptyConfig()
    {
        Assert.Null(WfTriggerConfig.ParseTimer("{}").Cron == "" ? null : "x"); // Cron 默认空串
        Assert.Null(WfTriggerConfig.ParseEvent("not-json").VarsMap);
        Assert.Null(WfTriggerConfig.ParseMessage("").VarsSchema);
    }
}
```

- [ ] **Step 2: 建共享测试基座 + 写失败测试（FireAsync 行为，SQLite + 真 FlowEngine）**

先建共享基座（照 `FlowConcurrencyTests.cs` 逐字模式，本波 A-T2/B-T2/C-T1/D-T2/E-T1/F-T1 全部测试复用）：

```csharp
// CP6.Tests/Wf/FlowTriggerTestHarness.cs —— 共享基座：GenerateCreateScript + TEXT 替换建库 +
// AFTER UPDATE 触发器模拟 rowversion（本波额外给 Wf_FlowTrigger 建同款触发器，B-T2 双 worker 抢占用）
using System;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using CP6.Core.EFDbContext;
using CP6.Core.Services.Wf;
using CP6.Entity.DomainModels.Sys;
using CP6.Entity.DomainModels.Wf;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace CP6.Tests;

internal static class FlowTriggerTestHarness
{
    /// <summary>测试专用子类：声明两表带 rowversion 触发器（EF Core 8 SQLite 关 RETURNING 改 SELECT 读回，
    /// 令 [Timestamp] 并发令牌在 SQLite 基座真正生效——照 FlowConcurrencyTests 口径）。</summary>
    internal sealed class SqliteCP6Context : CP6Context
    {
        public SqliteCP6Context(DbContextOptions<CP6Context> o) : base(o) { }
        protected override void OnModelCreating(ModelBuilder mb)
        {
            base.OnModelCreating(mb);
            mb.Entity<Wf_FlowInstance>().ToTable(t => t.HasTrigger("trg_Wf_FlowInstance_RowVersion"));
            mb.Entity<Wf_FlowTrigger>().ToTable(t => t.HasTrigger("trg_Wf_FlowTrigger_RowVersion"));
        }
    }

    public static SqliteCP6Context Ctx(SqliteConnection c)
        => new(new DbContextOptionsBuilder<CP6Context>().UseSqlite(c).Options);

    public static FlowEngine Engine(CP6Context db) => new(db, new ApproverResolver(db));

    public static FlowTriggerService Service(CP6Context db) => new(db, Engine(db));

    public static SqliteConnection NewSqliteWithSchema()
    {
        var conn = new SqliteConnection("DataSource=:memory:");
        conn.Open();
        using (var setup = Ctx(conn))
        {
            var script = Regex.Replace(setup.Database.GenerateCreateScript(),
                                       "n?varchar\\(max\\)", "TEXT", RegexOptions.IgnoreCase);
            Exec(conn, script);
        }
        Exec(conn,
            "CREATE TRIGGER trg_Wf_FlowInstance_RowVersion AFTER UPDATE ON \"Wf_FlowInstance\" " +
            "BEGIN UPDATE \"Wf_FlowInstance\" SET \"RowVersion\" = randomblob(8) WHERE \"Id\" = NEW.\"Id\"; END;");
        Exec(conn,
            "CREATE TRIGGER trg_Wf_FlowTrigger_RowVersion AFTER UPDATE ON \"Wf_FlowTrigger\" " +
            "BEGIN UPDATE \"Wf_FlowTrigger\" SET \"RowVersion\" = randomblob(8) WHERE \"Id\" = NEW.\"Id\"; END;");
        return conn;
    }

    private static void Exec(SqliteConnection c, string sql)
    {
        using var cmd = c.CreateCommand();
        cmd.CommandText = sql;
        cmd.ExecuteNonQuery();
    }

    /// <summary>最小 schema：start → approval(指定人) → end（形状照 FlowConcurrencyTests.ForkSchema）。</summary>
    public static string MinimalSchemaJson(Guid approver) => JsonSerializer.Serialize(new FlowSchema
    {
        Start = "s",
        Nodes =
        {
            new FlowNode { Id = "s", Type = "start" },
            new FlowNode { Id = "a", Type = "approval", ApproverStrategy = "Specified", ApproverUserId = approver },
            new FlowNode { Id = "end", Type = "end" },
        },
        Edges = { new FlowEdge { From = "s", To = "a" }, new FlowEdge { From = "a", To = "end" } },
    });

    /// <summary>seed：一个流程定义（默认 enabled，flowKey 默认 "fk-trig"）+ 发起人 + 审批人。</summary>
    public static async Task<(Guid StarterId, Guid ApproverId)> SeedFlowAndUsersAsync(
        SqliteConnection conn, string flowKey = "fk-trig", bool flowEnabled = true, bool starterEnabled = true)
    {
        var starter = Guid.NewGuid();
        var approver = Guid.NewGuid();
        using var db = Ctx(conn);
        db.Sys_Users.AddRange(
            new Sys_User { Id = starter, UserName = $"st{starter:N}", Password = "x", RoleId = 1, Enable = starterEnabled },
            new Sys_User { Id = approver, UserName = $"ap{approver:N}", Password = "x", RoleId = 1, Enable = true });
        db.Wf_FlowDefs.Add(new Wf_FlowDef
        {
            Id = Guid.NewGuid(), FlowKey = flowKey, FlowName = flowKey, FormKey = "f",
            SchemaJson = MinimalSchemaJson(approver), Version = 1, Enable = flowEnabled,
        });
        await db.SaveChangesAsync();
        return (starter, approver);
    }

    public static Wf_FlowTrigger NewTrigger(string flowKey, int type, Guid starterId,
        bool enabled = true, string configJson = "{}", string? eventKey = null)
        => new()
        {
            FlowKey = flowKey, TriggerType = type, StarterUserId = starterId,
            Enabled = enabled, ConfigJson = configJson, EventKey = eventKey,
        };
}
```

再写 FireAsync 行为测试（全代码）：

```csharp
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
```

- [ ] **Step 3: 跑验证 FAIL** — `--filter "FlowTriggerConfigTests|FlowTriggerFireTests"`（编译失败）。

- [ ] **Step 4: 实现 WfTriggerConfig**

```csharp
// CP6.Core/Services/Wf/WfTriggerConfig.cs
using System.Text.Json;

namespace CP6.Core.Services.Wf;

public class WfTimerTriggerConfig { public string Cron { get; set; } = ""; public string? VarsJson { get; set; } }
public class WfEventTriggerConfig { public Dictionary<string, string>? VarsMap { get; set; } }
public class WfMessageTriggerConfig { public List<string>? VarsSchema { get; set; } }

/// <summary>ConfigJson 分型解析（spec §2.3）。坏 JSON → 空配置（校验在 FlowTriggerValidator，解析不抛）。</summary>
public static class WfTriggerConfig
{
    private static readonly JsonSerializerOptions Opts = new() { PropertyNameCaseInsensitive = true };

    public static WfTimerTriggerConfig ParseTimer(string? json) => Parse<WfTimerTriggerConfig>(json) ?? new();
    public static WfEventTriggerConfig ParseEvent(string? json) => Parse<WfEventTriggerConfig>(json) ?? new();
    public static WfMessageTriggerConfig ParseMessage(string? json) => Parse<WfMessageTriggerConfig>(json) ?? new();

    private static T? Parse<T>(string? json) where T : class
    {
        if (string.IsNullOrWhiteSpace(json)) return null;
        try { return JsonSerializer.Deserialize<T>(json, Opts); }
        catch (JsonException) { return null; }
    }
}
```

- [ ] **Step 5: 实现 FlowTriggerService（FireAsync 部分）**

```csharp
// CP6.Core/Services/Wf/FlowTriggerService.cs
using System.Security.Cryptography;
using System.Text;
using CP6.Core.EFDbContext;
using CP6.Entity.DomainModels.Wf;
using Microsoft.EntityFrameworkCore;

namespace CP6.Core.Services.Wf;

public class TriggerFireResult
{
    public bool Success { get; init; }
    /// <summary>幂等撞键命中既有成功流水（HTTP 层据此回 200 而非 201）</summary>
    public bool Replayed { get; init; }
    public Guid? InstanceId { get; init; }
    public string? Error { get; init; }
    public static TriggerFireResult Ok(Guid instanceId, bool replayed = false)
        => new() { Success = true, InstanceId = instanceId, Replayed = replayed };
    public static TriggerFireResult Fail(string error) => new() { Success = false, Error = error };
}

public interface IFlowTriggerService
{
    /// <summary>统一发起（D2，spec §3.1）：Enabled 检查 → 幂等闸（撞键幂等返回既有 InstanceId 不报错）
    /// → 运行时双检 E-WF-022/023 → 变量构造由调用方完成 → SubmitAsync(trigger.StarterUserId) → 写流水 → 更新水位。</summary>
    Task<TriggerFireResult> FireAsync(Wf_FlowTrigger trigger, string? varsJson,
                                      int source, string idempotencyKey, CancellationToken ct);

    /// <summary>timer 扫描一轮（worker 复用；lease 语义 = RowVersion 乐观并发 + NextDueUtc 前移即抢占）。</summary>
    Task<int> ScanTimersOnceAsync(CancellationToken ct);
}

public class FlowTriggerService : IFlowTriggerService
{
    /// <summary>占坑补跑宽限：FiredUtc 早于此宽限仍未回填的占坑行才补跑（避免与正在进行的第二段抢跑）</summary>
    public static readonly TimeSpan RecoveryGrace = TimeSpan.FromMinutes(2);
    private const int BatchSize = 50;

    private readonly CP6Context _db;
    private readonly IFlowEngine _engine;

    public FlowTriggerService(CP6Context db, IFlowEngine engine)
    {
        _db = db;
        _engine = engine;
    }

    public async Task<TriggerFireResult> FireAsync(Wf_FlowTrigger trigger, string? varsJson,
                                                   int source, string idempotencyKey, CancellationToken ct)
    {
        // ① Enabled 检查（spec §3.1 顺序：先于幂等闸）
        if (!trigger.Enabled) return TriggerFireResult.Fail("触发器已停用");

        // ② 幂等闸：先查既有流水（Local + 库，防同 context 二次调用漏变更追踪器）
        var fire = _db.Wf_TriggerFires.Local
                       .FirstOrDefault(f => f.TriggerId == trigger.Id && f.IdempotencyKey == idempotencyKey)
                   ?? await _db.Wf_TriggerFires
                       .FirstOrDefaultAsync(f => f.TriggerId == trigger.Id && f.IdempotencyKey == idempotencyKey, ct);
        if (fire == null)
        {
            fire = new Wf_TriggerFire
            {
                TriggerId = trigger.Id,
                IdempotencyKey = idempotencyKey,
                FiredUtc = DateTime.UtcNow,
                Source = source,
                PayloadHash = source == WfTriggerType.Timer ? null : HashOrNull(varsJson),
            };
            _db.Wf_TriggerFires.Add(fire);
            try { await _db.SaveChangesAsync(ct); }
            catch (DbUpdateException)
            {
                // 并发撞唯一索引：让位既有行（另一实例先占坑），转入撞键分支
                _db.Entry(fire).State = EntityState.Detached;
                fire = await _db.Wf_TriggerFires
                    .FirstAsync(f => f.TriggerId == trigger.Id && f.IdempotencyKey == idempotencyKey, ct);
            }
        }
        if (fire.InstanceId != null)
            return TriggerFireResult.Ok(fire.InstanceId.Value, replayed: true);   // 幂等成功不是错误（spec §3.1）
        // InstanceId==null（占坑未完成或上次失败）→ 补跑第二段（共享契约末条语义）

        // ③ 运行时双检（spec §5：发起人/流程可能在保存后被停用）
        var starterOk = await _db.Sys_Users.AnyAsync(u => u.Id == trigger.StarterUserId && u.Enable, ct);
        if (!starterOk) return await FailFireAsync(fire, "E-WF-022: 发起人不存在或已停用", ct);
        var flowOk = await _db.Wf_FlowDefs.AnyAsync(d => d.FlowKey == trigger.FlowKey && d.Enable, ct);
        if (!flowOk) return await FailFireAsync(fire, "E-WF-023: 目标流程不存在或未启用", ct);

        // ④ 第二段：SubmitAsync + 流水回填 + 水位 同一显式事务（映射表⑥，引擎原子接缝）
        //    trigger 可能被上游 ChangeTracker.Clear 失联 → 用库内跟踪实例回写水位
        var trackedTrigger = await _db.Wf_FlowTriggers.FirstOrDefaultAsync(t => t.Id == trigger.Id, ct) ?? trigger;
        try
        {
            await using var tx = await _db.Database.BeginTransactionAsync(ct);
            var instanceId = await _engine.SubmitAsync(trackedTrigger.FlowKey, trackedTrigger.StarterUserId, varsJson ?? "{}");
            fire.InstanceId = instanceId;
            fire.Error = null;                              // 失败重试成功 → 清错
            trackedTrigger.LastFiredUtc = DateTime.UtcNow;  // 水位
            await _db.SaveChangesAsync(ct);
            await tx.CommitAsync(ct);
            return TriggerFireResult.Ok(instanceId);
        }
        catch (Exception ex)
        {
            // SubmitAsync 半途实体已随事务回滚，但仍挂在变更追踪器上 → 清追踪器后重查流水行回填 Error
            _db.ChangeTracker.Clear();
            var fresh = await _db.Wf_TriggerFires.FirstAsync(f => f.Id == fire.Id, ct);
            fresh.Error = Trunc($"E-WF-024: {ex.Message}");
            await _db.SaveChangesAsync(ct);
            return TriggerFireResult.Fail(fresh.Error);
        }
    }

    public Task<int> ScanTimersOnceAsync(CancellationToken ct)
        => ScanTimersOnceAsync(DateTime.UtcNow, ct);

    /// <summary>测试重载（注入 nowUtc，映射表⑤）——B-T2 实现。</summary>
    public Task<int> ScanTimersOnceAsync(DateTime nowUtc, CancellationToken ct)
        => throw new NotImplementedException("B-T2");

    private async Task<TriggerFireResult> FailFireAsync(Wf_TriggerFire fire, string error, CancellationToken ct)
    {
        fire.Error = Trunc(error);
        await _db.SaveChangesAsync(ct);
        return TriggerFireResult.Fail(error);
    }

    private static string? HashOrNull(string? s)
        => s == null ? null : Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(s)));

    private static string Trunc(string s) => s.Length <= 1000 ? s : s[..1000];
}
```

> **调用方契约（注释进代码）**：FireAsync 失败路径会 `ChangeTracker.Clear()`——调用方在一次 FireAsync 失败后**不得复用先前批量加载的跟踪实体**（B-T2 扫描循环、C-T1 hook 循环均按 Id 逐条重查，见各任务）。

- [ ] **Step 6: DI** — `Program.cs` FlowEngine 注册块（`:107-108`）之后追加：

```csharp
builder.Services.AddScoped<CP6.Core.Services.Wf.IFlowTriggerService, CP6.Core.Services.Wf.FlowTriggerService>(); // 事件触发 start：三入口单一出口（D2）
```

- [ ] **Step 7: 跑验证 PASS + Wf 闸 + commit**

```bash
dotnet test CP6.Tests/CP6.Tests.csproj --filter "FlowTriggerConfigTests|FlowTriggerFireTests"
dotnet test CP6.Tests/CP6.Tests.csproj --filter Wf
git add -A && git commit -m "feat(wfs-trigger): A-T2 IFlowTriggerService.FireAsync 幂等闸+占坑复用+E-WF-022/023/024 运行时双检+SubmitAsync 原子接缝+DI"
```

---


---
## 附: 共享契约(plan全局, 精确名字)
## 共享契约（所有 Task 用这些**精确**名字，前后一致）

- `WfTriggerType`：`Timer=0 / Event=1 / Message=2`（int 常量，`WfStatus.cs`）。
- 实体字段：`Wf_FlowTrigger { FlowKey, TriggerType, ConfigJson, Enabled, EventKey, StarterUserId, NextDueUtc, LastFiredUtc, ApiKeyHash, RowVersion }`；`Wf_TriggerFire { TriggerId, IdempotencyKey, FiredUtc, InstanceId, Source, Error, PayloadHash }`（均继承 BaseTenantEntity）。
- `TriggerFireResult { bool Success; bool Replayed; Guid? InstanceId; string? Error; static Ok(Guid, bool replayed=false); static Fail(string); }`
- `IFlowTriggerService`（spec §3.1 逐字）：
  - `Task<TriggerFireResult> FireAsync(Wf_FlowTrigger trigger, string? varsJson, int source, string idempotencyKey, CancellationToken ct);`
  - `Task<int> ScanTimersOnceAsync(CancellationToken ct);`（实现类测试重载 `ScanTimersOnceAsync(DateTime nowUtc, CancellationToken ct)`）
- 幂等键口径（spec §2.2）：timer=`$"{trigger.Id}:{dueUtc:O}"`；event=`$"{eventId}:{trigger.Id}"`；message=`Idempotency-Key` 头；手动试发=`$"manual:{Guid.NewGuid():N}"`。
- `WfCronHelper { static bool IsValid(string?); static DateTime? NextUtc(string cron, DateTime afterUtc); static IReadOnlyList<DateTime> PreviewUtc(string cron, DateTime fromUtc, int count); }`
- `IWfTriggerBridgeHook`：
  - `Task<WfTriggerBridgeResult> OnEventAsync(string eventKey, string eventId, string payloadJson, string? userName);`（业务入口，写 outbox 台账）
  - `Task<WfTriggerBridgeResult> ReplayEventAsync(string eventKey, string eventId, string payloadJson, string? userName);`（dispatcher 重放入口，不再写新 outbox 行）
- `WfTriggerBridgeResult { bool Success; int MatchedCount; int FiredCount; string? Message; static Ok(int matched, int fired); static Skipped(string); static Failed(string); }`
- `WfTriggerEventPayload(string EventKey, string EventId, string PayloadJson, string? UserName)`（record，outbox 负载契约）。
- `WfTriggerVarsMapper { static string MapVars(Dictionary<string,string>? varsMap, string payloadJson); static string FilterBySchema(string bodyJson, IReadOnlyList<string>? schema); }`
- `WfApiKeyHelper { static string NewRawKey(); static string HashOf(string raw); static bool Verify(string raw, string? storedHash); }`
- `WfTriggerConfig`：`ParseTimer(string)→WfTimerTriggerConfig{Cron,VarsJson}` / `ParseEvent(string)→WfEventTriggerConfig{VarsMap}` / `ParseMessage(string)→WfMessageTriggerConfig{VarsSchema}`。
- 常量（`FlowTriggerService`）：`RecoveryGrace = TimeSpan.FromMinutes(2)`（补跑宽限）、`BatchSize = 50`、`Trunc` 截 1000。
- 错误码：`E-WF-022`（配置无效：cron/eventKey/varsMap/StarterUserId）/ `E-WF-023`（目标流程不可发起）/ `E-WF-024`（运行时发起失败，写 TriggerFire.Error）。message 端点 401/404/400 走 HTTP 语义不占 E-WF 码。
- FireAsync 撞键语义（spec §3.1 引申，全计划统一）：既有行 `InstanceId != null` → `Ok(instanceId, replayed:true)`（幂等成功非错误）；既有行 `InstanceId == null`（占坑未完成**或**上次失败）→ 补跑第二段（成功回填并清 Error / 失败覆写 Error）。timer 补跑扫描只捡 `Error==null` 的占坑行（spec §3.2 原文）；Error 行的重试机会来自 event outbox 重放与 message 客户端重试。


## 附: spec↔现状映射(落地口径)
| # | spec 表述 | 现状/落地口径 |
|---|---|---|
| ① | `FlowEngine.StartAsync`（§1/§3.1「StartAsync」） | 仓库实际发起入口是 **`IFlowEngine.SubmitAsync`**（签名见锚点表）。spec 的「StartAsync」语义即它，本计划一律落 `SubmitAsync(trigger.FlowKey, trigger.StarterUserId, varsJson)`。 |
| ② | 权限点 `OA.FlowTrigger.View/Edit`（§6） | 权限模型无字符串权限点，落地=菜单 734 回填 `MenuKey="oa-flow-admin"`（RoutePath 派生口径）+ `Sys_MenuAction` ActionCode **`FlowTrigger.View` / `FlowTrigger.Edit`** + RoleId=1 授予；控制器 `[RequirePermission("oa-flow-admin","FlowTrigger.View/Edit")]`。spec 权限点名原样保留在 ActionCode。 |
| ③ | UI 预设「每月末」（§4） | NCrontab 标准 5 段**无 `L` 语义**。预设「每月末」落 `0 0 28 * *` 并在 UI 文案注明「按每月 28 日近似」；真月末与工作日口径同列 spec §9 留后条目。cron 边界测试用「每月 31 日只在大月发」「2/29 只闰年发」验证 NCrontab 行为。 |
| ④ | `EventKey` 提列可索引（§2.1）、幂等复合唯一索引（§2.2） | SQL Server 索引键列不能是 nvarchar(max) → `FlowKey/EventKey [MaxLength(200)]`、`IdempotencyKey [MaxLength(200)]`、`ApiKeyHash/PayloadHash [MaxLength(64)]`、`Error [MaxLength(1000)]`。这是 spec「提列正是为可索引」的必然实现细节，非设计变更。message 端点与 event hook 相应校验键长 ≤200。 |
| ⑤ | `ScanTimersOnceAsync(CancellationToken ct)`（§3.1 接口） | 接口签名照 spec 不动；实现类 `FlowTriggerService` 另给 **`ScanTimersOnceAsync(DateTime nowUtc, CancellationToken ct)` 测试重载**（对齐 ServiceJob「注入 nowUtc」测试铁律），接口方法委托 `DateTime.UtcNow`。 |
| ⑥ | 「StartAsync 与流水在同一 SaveChanges 事务」（§3.1） | `SubmitAsync` 内部自带 `SaveChangesAsync` → 用 **显式事务** `BeginTransactionAsync` 包「SubmitAsync + 流水回填 + LastFiredUtc」达成同一原子提交（第二段整体原子）；占坑第一段在事务**之外**先行落库（两段式本义）。 |
| ⑦ | dispatcher 重放（§3.3） | hook 家族被 dispatcher 重放时若原样调 `OnEventAsync` 会**每次重放再写一行新 outbox**（Failed 行自增殖）。故接口拆双入口：`OnEventAsync`（业务调用，写台账）+ `ReplayEventAsync`（dispatcher 重放专用，同一执行逻辑**不再写新 outbox 行**，去重仍靠 TriggerFire 幂等闸）。spec「失败自动进 outbox / 重放原样复用 eventId」语义不变。 |

