### Task E-T1: 管理后端（CRUD + 启停 + 手动试发 + 流水 + key 重置 + cron 预览）

**Files:**
- Create: `CP6.Core/Services/Wf/FlowTriggerAdminService.cs`
- Create: `CP6.WebApi/Controllers/Oa/FlowTriggerAdminController.cs`
- Modify: `CP6.WebApi/Program.cs`（DI）
- Test: `CP6.Tests/Wf/FlowTriggerAdminTests.cs`

- [ ] **Step 1: 写失败测试**

```csharp
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
```

- [ ] **Step 2: 跑验证 FAIL**（`--filter FlowTriggerAdminTests`）。

- [ ] **Step 3: 实现服务**

```csharp
// CP6.Core/Services/Wf/FlowTriggerAdminService.cs
using CP6.Core.EFDbContext;
using CP6.Entity.DomainModels.Wf;
using Microsoft.EntityFrameworkCore;

namespace CP6.Core.Services.Wf;

public record FlowTriggerSaveReq(
    string FlowKey, int TriggerType, string ConfigJson, bool Enabled,
    string? EventKey, Guid StarterUserId);

public record FlowTriggerListItem(
    Guid Id, string FlowKey, int TriggerType, bool Enabled, string? EventKey,
    Guid StarterUserId, DateTime? NextDueUtc, DateTime? LastFiredUtc, bool HasApiKey, string ConfigJson);

public record TriggerFireListItem(
    Guid Id, string IdempotencyKey, DateTime FiredUtc, Guid? InstanceId, int Source, string? Error);

public interface IFlowTriggerAdminService
{
    Task<List<FlowTriggerListItem>> ListAsync(CancellationToken ct);
    Task<FlowTriggerListItem?> GetAsync(Guid id, CancellationToken ct);
    /// <summary>返回 (id, apiKeyPlain)。apiKeyPlain 仅 message 型创建时非空——明文只此一次（spec §3.4）。</summary>
    Task<(Guid Id, string? ApiKeyPlain)> CreateAsync(FlowTriggerSaveReq req, CancellationToken ct);
    Task UpdateAsync(Guid id, FlowTriggerSaveReq req, CancellationToken ct);
    Task SetEnabledAsync(Guid id, bool enabled, CancellationToken ct);
    /// <summary>重置 key（仅 message）：返回新明文，旧 key 即刻失效。</summary>
    Task<string> ResetKeyAsync(Guid id, CancellationToken ct);
    /// <summary>手动试发（权限同 Edit）：幂等键 = "manual:{GUID}"（spec §4）。</summary>
    Task<TriggerFireResult> ManualFireAsync(Guid id, CancellationToken ct);
    Task<List<TriggerFireListItem>> ListFiresAsync(Guid id, int take, CancellationToken ct);
}

public class FlowTriggerAdminService : IFlowTriggerAdminService
{
    private readonly CP6Context _db;
    private readonly IFlowTriggerService _fire;

    public FlowTriggerAdminService(CP6Context db, IFlowTriggerService fire)
    {
        _db = db;
        _fire = fire;
    }

    public async Task<List<FlowTriggerListItem>> ListAsync(CancellationToken ct)
        => await _db.Wf_FlowTriggers.OrderBy(t => t.FlowKey).ThenBy(t => t.TriggerType)
            .Select(t => ToItem(t)).ToListAsync(ct);

    public async Task<FlowTriggerListItem?> GetAsync(Guid id, CancellationToken ct)
    {
        var t = await _db.Wf_FlowTriggers.FirstOrDefaultAsync(x => x.Id == id, ct);
        return t == null ? null : ToItem(t);
    }

    public async Task<(Guid Id, string? ApiKeyPlain)> CreateAsync(FlowTriggerSaveReq req, CancellationToken ct)
    {
        await FlowTriggerValidator.ValidateAsync(_db, req, ct);   // F-T1 落地；E-T1 阶段先建含基本必填检查的桩（见 Step 3 末注）
        var t = new Wf_FlowTrigger
        {
            FlowKey = req.FlowKey, TriggerType = req.TriggerType,
            ConfigJson = string.IsNullOrWhiteSpace(req.ConfigJson) ? "{}" : req.ConfigJson,
            Enabled = req.Enabled,
            EventKey = req.TriggerType == WfTriggerType.Event ? req.EventKey : null,
            StarterUserId = req.StarterUserId,
        };
        string? plain = null;
        if (req.TriggerType == WfTriggerType.Message)
        {
            plain = WfApiKeyHelper.NewRawKey();
            t.ApiKeyHash = WfApiKeyHelper.HashOf(plain);
        }
        if (req.TriggerType == WfTriggerType.Timer)
            t.NextDueUtc = WfCronHelper.NextUtc(WfTriggerConfig.ParseTimer(t.ConfigJson).Cron, DateTime.UtcNow);
        _db.Wf_FlowTriggers.Add(t);
        await _db.SaveChangesAsync(ct);
        return (t.Id, plain);
    }

    public async Task UpdateAsync(Guid id, FlowTriggerSaveReq req, CancellationToken ct)
    {
        var t = await _db.Wf_FlowTriggers.FirstOrDefaultAsync(x => x.Id == id, ct)
                ?? throw new InvalidOperationException("E-WF-022: 触发器不存在");
        if (t.TriggerType != req.TriggerType)
            throw new InvalidOperationException("E-WF-022: 触发器类型不可变更（删除重建）");
        await FlowTriggerValidator.ValidateAsync(_db, req, ct);
        t.FlowKey = req.FlowKey;
        t.ConfigJson = string.IsNullOrWhiteSpace(req.ConfigJson) ? "{}" : req.ConfigJson;
        t.Enabled = req.Enabled;
        t.EventKey = req.TriggerType == WfTriggerType.Event ? req.EventKey : null;
        t.StarterUserId = req.StarterUserId;
        if (t.TriggerType == WfTriggerType.Timer)
            t.NextDueUtc = WfCronHelper.NextUtc(WfTriggerConfig.ParseTimer(t.ConfigJson).Cron, DateTime.UtcNow);
        await _db.SaveChangesAsync(ct);
    }

    public async Task SetEnabledAsync(Guid id, bool enabled, CancellationToken ct)
    {
        var t = await _db.Wf_FlowTriggers.FirstOrDefaultAsync(x => x.Id == id, ct)
                ?? throw new InvalidOperationException("E-WF-022: 触发器不存在");
        t.Enabled = enabled;
        if (enabled && t.TriggerType == WfTriggerType.Timer && t.NextDueUtc == null)
            t.NextDueUtc = WfCronHelper.NextUtc(WfTriggerConfig.ParseTimer(t.ConfigJson).Cron, DateTime.UtcNow);  // cron 修复后重新上膛
        await _db.SaveChangesAsync(ct);
    }

    public async Task<string> ResetKeyAsync(Guid id, CancellationToken ct)
    {
        var t = await _db.Wf_FlowTriggers.FirstOrDefaultAsync(x => x.Id == id, ct)
                ?? throw new InvalidOperationException("E-WF-022: 触发器不存在");
        if (t.TriggerType != WfTriggerType.Message)
            throw new InvalidOperationException("E-WF-022: 仅 message 触发器有 API key");
        var plain = WfApiKeyHelper.NewRawKey();
        t.ApiKeyHash = WfApiKeyHelper.HashOf(plain);
        await _db.SaveChangesAsync(ct);
        return plain;
    }

    public async Task<TriggerFireResult> ManualFireAsync(Guid id, CancellationToken ct)
    {
        var t = await _db.Wf_FlowTriggers.FirstOrDefaultAsync(x => x.Id == id, ct)
                ?? throw new InvalidOperationException("E-WF-022: 触发器不存在");
        var varsJson = t.TriggerType == WfTriggerType.Timer
            ? WfTriggerConfig.ParseTimer(t.ConfigJson).VarsJson
            : "{}";
        return await _fire.FireAsync(t, varsJson, t.TriggerType, $"manual:{Guid.NewGuid():N}", ct);
    }

    public async Task<List<TriggerFireListItem>> ListFiresAsync(Guid id, int take, CancellationToken ct)
        => await _db.Wf_TriggerFires.Where(f => f.TriggerId == id)
            .OrderByDescending(f => f.FiredUtc).Take(Math.Clamp(take, 1, 200))
            .Select(f => new TriggerFireListItem(f.Id, f.IdempotencyKey, f.FiredUtc, f.InstanceId, f.Source, f.Error))
            .ToListAsync(ct);

    private static FlowTriggerListItem ToItem(Wf_FlowTrigger t)
        => new(t.Id, t.FlowKey, t.TriggerType, t.Enabled, t.EventKey, t.StarterUserId,
               t.NextDueUtc, t.LastFiredUtc, t.ApiKeyHash != null, t.ConfigJson);
}
```

> **E-T1 阶段的 `FlowTriggerValidator` 桩**：本任务同文件夹先建最小版（仅必填检查，全代码）：
>
> ```csharp
> // CP6.Core/Services/Wf/FlowTriggerValidator.cs（E-T1 最小版；F-T1 以 TDD 扩成 spec §5 全量校验）
> public static class FlowTriggerValidator
> {
>     public static Task ValidateAsync(CP6Context db, FlowTriggerSaveReq req, CancellationToken ct)
>     {
>         if (string.IsNullOrWhiteSpace(req.FlowKey)) throw new InvalidOperationException("E-WF-023: FlowKey 必填");
>         if (req.TriggerType is < WfTriggerType.Timer or > WfTriggerType.Message)
>             throw new InvalidOperationException("E-WF-022: 触发器类型非法");
>         if (req.StarterUserId == Guid.Empty) throw new InvalidOperationException("E-WF-022: StarterUserId 必填");
>         return Task.CompletedTask;
>     }
> }
> ```

- [ ] **Step 4: 实现控制器**（仿 `FlowAdminController` 范式：`LocalizedControllerBase` + `Ok2`/`Err` 壳；权限点映射表②——`[RequirePermission]` 在 F-T2 seed 落地前会 403，本任务先贴特性、F-T2 seed 后 QA 验通）

```csharp
// CP6.WebApi/Controllers/Oa/FlowTriggerAdminController.cs
using CP6.Core.Auth;
using CP6.Core.Services.Wf;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace CP6.WebApi.Controllers.Oa;

/// <summary>流程触发器管理（spec §4，流程管理页「触发器」tab 后端）。
/// 权限点（spec §6 OA.FlowTrigger.* → 映射表②）：View=查，Edit=增改/启停/试发/重置 key。</summary>
[ApiController]
[Route("api/oa/flow-triggers")]
[Authorize]
public class FlowTriggerAdminController : LocalizedControllerBase
{
    private readonly IFlowTriggerAdminService _admin;

    public FlowTriggerAdminController(IFlowTriggerAdminService admin) { _admin = admin; }

    private IActionResult Ok2(object? data = null) => Ok(new { code = 0, message = "OK", data });
    private IActionResult Err(InvalidOperationException e) => BadRequest(new { code = 400, message = e.Message });

    [HttpGet("list")]
    [RequirePermission("oa-flow-admin", "FlowTrigger.View")]
    public async Task<IActionResult> List(CancellationToken ct) => Ok2(await _admin.ListAsync(ct));

    [HttpGet("{id:guid}")]
    [RequirePermission("oa-flow-admin", "FlowTrigger.View")]
    public async Task<IActionResult> Get(Guid id, CancellationToken ct)
    {
        var item = await _admin.GetAsync(id, ct);
        return item is null ? NotFound(new { code = 404, message = "E-WF-022" }) : Ok2(item);
    }

    [HttpPost]
    [RequirePermission("oa-flow-admin", "FlowTrigger.Edit")]
    public async Task<IActionResult> Create([FromBody] FlowTriggerSaveReq req, CancellationToken ct)
    {
        try
        {
            var (id, apiKeyPlain) = await _admin.CreateAsync(req, ct);
            return Ok2(new { id, apiKeyPlain });   // 明文只此一次（spec §3.4）
        }
        catch (InvalidOperationException e) { return Err(e); }
    }

    [HttpPut("{id:guid}")]
    [RequirePermission("oa-flow-admin", "FlowTrigger.Edit")]
    public async Task<IActionResult> Update(Guid id, [FromBody] FlowTriggerSaveReq req, CancellationToken ct)
    {
        try { await _admin.UpdateAsync(id, req, ct); return Ok2(); }
        catch (InvalidOperationException e) { return Err(e); }
    }

    [HttpPost("{id:guid}/enable")]
    [RequirePermission("oa-flow-admin", "FlowTrigger.Edit")]
    public async Task<IActionResult> Enable(Guid id, [FromBody] EnableReq r, CancellationToken ct)
    {
        try { await _admin.SetEnabledAsync(id, r.Enabled, ct); return Ok2(); }
        catch (InvalidOperationException e) { return Err(e); }
    }

    [HttpPost("{id:guid}/reset-key")]
    [RequirePermission("oa-flow-admin", "FlowTrigger.Edit")]
    public async Task<IActionResult> ResetKey(Guid id, CancellationToken ct)
    {
        try { return Ok2(new { apiKeyPlain = await _admin.ResetKeyAsync(id, ct) }); }
        catch (InvalidOperationException e) { return Err(e); }
    }

    [HttpPost("{id:guid}/manual-fire")]
    [RequirePermission("oa-flow-admin", "FlowTrigger.Edit")]   // 手动试发归 Edit（spec §6）
    public async Task<IActionResult> ManualFire(Guid id, CancellationToken ct)
    {
        try
        {
            var r = await _admin.ManualFireAsync(id, ct);
            return r.Success
                ? Ok2(new { r.InstanceId })
                : BadRequest(new { code = 400, message = r.Error });
        }
        catch (InvalidOperationException e) { return Err(e); }
    }

    [HttpGet("{id:guid}/fires")]
    [RequirePermission("oa-flow-admin", "FlowTrigger.View")]
    public async Task<IActionResult> Fires(Guid id, [FromQuery] int take, CancellationToken ct)
        => Ok2(await _admin.ListFiresAsync(id, take <= 0 ? 20 : take, ct));

    [HttpPost("cron-preview")]
    [RequirePermission("oa-flow-admin", "FlowTrigger.View")]
    public IActionResult CronPreview([FromBody] CronPreviewReq r)
        => WfCronHelper.IsValid(r.Cron)
            ? Ok2(new { next = WfCronHelper.PreviewUtc(r.Cron, DateTime.UtcNow, 5) })
            : BadRequest(new { code = 400, message = "E-WF-022" });

    public record EnableReq(bool Enabled);
    public record CronPreviewReq(string Cron);
}
```

- [ ] **Step 5: DI** — `Program.cs`（IFlowTriggerService 注册同块）：

```csharp
builder.Services.AddScoped<CP6.Core.Services.Wf.IFlowTriggerAdminService, CP6.Core.Services.Wf.FlowTriggerAdminService>();
```

- [ ] **Step 6: 跑验证 PASS + Wf 闸 + commit**

```bash
dotnet test CP6.Tests/CP6.Tests.csproj --filter FlowTriggerAdminTests
dotnet test CP6.Tests/CP6.Tests.csproj --filter Wf
git add -A && git commit -m "feat(wfs-trigger): E-T1 管理后端 CRUD/启停/手动试发/流水/key 重置/cron 预览+权限特性"
```

---


---
## 附: 共享契约(plan全局)
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


## 附: 控制器/权限现状锚点
| 权限/菜单 | 权限模型=MenuAction：`[RequirePermission(menuKey, action)]`（`CP6.Core/Auth/RequirePermissionAttribute.cs`，→`IPermissionService.HasActionAsync`）；menuKey = `RoutePath.Trim('/').Replace('/','-')` 派生（sso/field-audit 先例）。菜单种子内联 `Program.cs`（734=流程管理 `/oa/flow-admin`，**当前 MenuKey=null、控制器仅 `[Authorize]`**）；动作点 seed=`Sys_MenuAction`（定义）+`Sys_RoleAction`（RoleId=1 授予）幂等块（Program.cs:850-856 范本）。 |
| 前端 | 流程管理页=`cp6.web/src/views/oa/admin/FlowAdmin.vue`（97 行，CpPageShell+CpListPage，**当前无 tab**）。API 范式 `cp6.web/src/api/oa/*.ts`（`import http from '../http'`，导出 `xxxApi` 字面量，剥壳 `res.data ?? res`）。CpTag 用 `:tone="'ok'\|'muted'"`；对话框直接 el-dialog（`SendBackDialog.vue` 范本）。 |
| i18n seed | `CP6.WebApi/Seed/I18nOa*ScreenSeed.cs` 静态 `Sys_Lang[] Items`（五列 ZhCN/ZhTW/En/Ja/Ko + LangKey；错误码直接以 `E-WF-0xx` 作 LangKey）；注册在 `Program.cs:1812-1814` `.Concat(...Items)` 链追加。 |
| 控制器范式 | `FlowAdminController : LocalizedControllerBase`，`[ApiController][Route("api/oa/flow-admin")][Authorize]`；私有 `Ok2(data)` = `Ok(new { code = 0, message = "OK", data })`、`Err(e)` = 400 壳。 |
