### Task A-T2: PrefService 矩阵读取（per-request 缓存）+ 服务端合并写 + PrefController 端点

**Files:**
- Modify: `CP6.Core/Services/Oa/IPrefService.cs`
- Modify: `CP6.Core/Services/Oa/PrefService.cs`
- Modify: `CP6.WebApi/Controllers/Oa/PrefController.cs`
- Test: `CP6.Tests/Oa/PrefMergeTests.cs`

**Interfaces:**
- Consumes: `NotifyMatrix.IsEnabled` / `NotifyMatrix.Rows()`（A-T1）。
- Produces: `Task<bool> IsEnabledAsync(Guid userId, string type, string channel)`、`Task SaveMergeAsync(Guid userId, string partialJson)`（patch 顶层键为 `null` → 删除该键）、`POST /api/oa/pref/save` 的 `SavePrefReq(string PrefsJson, bool Merge = false)`、`GET /api/oa/pref/notify-matrix` → `Ok2(NotifyMatrix.Rows())`。A-T3（notifier）与 A-T4/D-T2（前端）依赖。

- [ ] **Step 1: 写失败测试**

```csharp
// CP6.Tests/Oa/PrefMergeTests.cs
using CP6.Core.EFDbContext;
using CP6.Core.Services.Oa;
using CP6.Entity.DomainModels.Wf;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using System.Text.Json;
using Xunit;

namespace CP6.Tests.Oa;

public class PrefMergeTests
{
    private static CP6Context NewDb() => new(new DbContextOptionsBuilder<CP6Context>()
        .UseInMemoryDatabase(Guid.NewGuid().ToString())
        .ConfigureWarnings(w => w.Ignore(InMemoryEventId.TransactionIgnoredWarning)).Options);
    private static PrefService Svc(CP6Context db) => new(db);

    // ── 合并写不覆盖他键（spec §7）──
    [Fact]
    public async Task SaveMerge_PatchesTopLevelKey_PreservesOthers()
    {
        using var db = NewDb();
        var me = Guid.NewGuid();
        await Svc(db).SaveAsync(me, """{"pageSize":50,"notify":{"todo":false}}""");

        await Svc(db).SaveMergeAsync(me, """{"rowMode":"expanded"}""");

        using var doc = JsonDocument.Parse(await Svc(db).GetAsync(me));
        Assert.Equal(50, doc.RootElement.GetProperty("pageSize").GetInt32());              // 他键保留
        Assert.False(doc.RootElement.GetProperty("notify").GetProperty("todo").GetBoolean());
        Assert.Equal("expanded", doc.RootElement.GetProperty("rowMode").GetString());       // 新键并入
    }

    [Fact]
    public async Task SaveMerge_ReplacesKeyWholesale_And_NullDeletesKey()
    {
        using var db = NewDb();
        var me = Guid.NewGuid();
        await Svc(db).SaveAsync(me, """{"pageSize":50,"notify":{"todo":false,"email":false}}""");

        await Svc(db).SaveMergeAsync(me, """{"notify":{"todoCreated":{"inApp":false}}}""");  // 顶层键整体替换
        using (var doc = JsonDocument.Parse(await Svc(db).GetAsync(me)))
        {
            var notify = doc.RootElement.GetProperty("notify");
            Assert.False(notify.TryGetProperty("todo", out _));                              // 旧扁平键被替换掉
            Assert.False(notify.GetProperty("todoCreated").GetProperty("inApp").GetBoolean());
            Assert.Equal(50, doc.RootElement.GetProperty("pageSize").GetInt32());
        }

        await Svc(db).SaveMergeAsync(me, """{"notify":null}""");                             // 恢复默认 = 删键
        using (var doc = JsonDocument.Parse(await Svc(db).GetAsync(me)))
        {
            Assert.False(doc.RootElement.TryGetProperty("notify", out _));
            Assert.Equal(50, doc.RootElement.GetProperty("pageSize").GetInt32());
        }
    }

    [Fact]
    public async Task SaveMerge_NoRow_CreatesRow()
    {
        using var db = NewDb();
        var me = Guid.NewGuid();
        await Svc(db).SaveMergeAsync(me, """{"rowMode":"expanded"}""");
        Assert.Equal(1, await db.Wf_InboxPrefs.CountAsync(p => p.UserId == me));
    }

    [Fact]
    public async Task SaveMerge_BadPatchJson_Throws_i18nKey()
    {
        using var db = NewDb();
        var ex = await Assert.ThrowsAsync<InvalidOperationException>(
            () => Svc(db).SaveMergeAsync(Guid.NewGuid(), "NOT_JSON{{{"));
        Assert.Equal("oa.pref.errBadJson", ex.Message);
    }

    // ── IsEnabledAsync：查库 + per-request 缓存（缓存不跨请求，spec §7）──
    [Fact]
    public async Task IsEnabledAsync_ReadsMatrix_FromDb()
    {
        using var db = NewDb();
        var me = Guid.NewGuid();
        await Svc(db).SaveAsync(me, """{"notify":{"flowRejected":{"email":false}}}""");
        var svc = Svc(db);
        Assert.True (await svc.IsEnabledAsync(me, "flowRejected", "inApp"));
        Assert.False(await svc.IsEnabledAsync(me, "flowRejected", "email"));
        Assert.True (await svc.IsEnabledAsync(Guid.NewGuid(), "flowRejected", "email"));   // 无行 → true
    }

    [Fact]
    public async Task IsEnabledAsync_CachesWithinInstance_NotAcrossInstances()
    {
        using var db = NewDb();
        var me = Guid.NewGuid();
        db.Wf_InboxPrefs.Add(new Wf_InboxPref { Id = Guid.NewGuid(), UserId = me, PrefsJson = "{}" });
        await db.SaveChangesAsync();

        var svc1 = Svc(db);                                             // 模拟请求 1（Scoped 实例）
        Assert.True(await svc1.IsEnabledAsync(me, "todoCreated", "inApp"));   // 首查 → 缓存 "{}"

        var row = await db.Wf_InboxPrefs.SingleAsync(p => p.UserId == me);
        row.PrefsJson = """{"notify":{"todoCreated":{"inApp":false}}}""";
        await db.SaveChangesAsync();

        Assert.True(await svc1.IsEnabledAsync(me, "todoCreated", "inApp"));   // 同实例（同请求）：命中缓存，仍 true
        Assert.False(await Svc(db).IsEnabledAsync(me, "todoCreated", "inApp")); // 新实例（新请求）：读到新值
    }

    [Fact]
    public async Task SaveMerge_InvalidatesOwnCache()
    {
        using var db = NewDb();
        var me = Guid.NewGuid();
        var svc = Svc(db);
        Assert.True(await svc.IsEnabledAsync(me, "todoCreated", "inApp"));                 // 缓存默认 "{}"
        await svc.SaveMergeAsync(me, """{"notify":{"todoCreated":{"inApp":false}}}""");
        Assert.False(await svc.IsEnabledAsync(me, "todoCreated", "inApp"));                // 同实例保存后读到新值
    }
}
```

- [ ] **Step 2: 跑测试验证 FAIL** — `dotnet test CP6.Tests/CP6.Tests.csproj --filter PrefMergeTests`。预期：编译失败（新方法不存在）。

- [ ] **Step 3: 实现服务层**

`IPrefService.cs` 追加（保留既有三方法与注释不动）：

```csharp
    /// <summary>矩阵偏好查询（wfs-inbox-ux §2.2）。逐收件人×逐通道；Scoped 实例内字典缓存（= per-request）。</summary>
    Task<bool> IsEnabledAsync(Guid userId, string type, string channel);

    /// <summary>顶层键合并写（wfs-inbox-ux §6）：读-改-写单次 SaveChanges；patch 键值为 null → 删除该键。
    /// patch 非法 JSON → InvalidOperationException("oa.pref.errBadJson")。</summary>
    Task SaveMergeAsync(Guid userId, string partialJson);

    /// <summary>rowMode 显示偏好（wfs-inbox-ux §5）："merged"（默认）| "expanded"。</summary>
    Task<string> GetRowModeAsync(Guid userId);
```

`PrefService.cs` 追加（`GetRowModeAsync` 在 D-T1 实现，此处先加接口成员会导致编译失败——**本任务一并给最小实现**，D-T1 只加测试与消费方）：

```csharp
    // ── wfs-inbox-ux：矩阵偏好 + 合并写 ────────────────────────────────────

    /// <summary>per-request 缓存：本服务 Scoped 注册（Program.cs:151），实例生命周期=单请求。</summary>
    private readonly Dictionary<Guid, string> _prefsCache = new();

    private async Task<string> GetCachedAsync(Guid userId)
    {
        if (_prefsCache.TryGetValue(userId, out var cached)) return cached;
        var json = await GetAsync(userId);
        _prefsCache[userId] = json;
        return json;
    }

    /// <inheritdoc/>
    public async Task<bool> IsEnabledAsync(Guid userId, string type, string channel) =>
        NotifyMatrix.IsEnabled(await GetCachedAsync(userId), type, channel);

    /// <inheritdoc/>
    public async Task SaveMergeAsync(Guid userId, string partialJson)
    {
        System.Text.Json.Nodes.JsonObject patch;
        try
        {
            patch = System.Text.Json.Nodes.JsonNode.Parse(
                string.IsNullOrWhiteSpace(partialJson) ? "{}" : partialJson) as System.Text.Json.Nodes.JsonObject
                ?? throw new InvalidOperationException("oa.pref.errBadJson");
        }
        catch (JsonException)
        {
            throw new InvalidOperationException("oa.pref.errBadJson");
        }

        System.Text.Json.Nodes.JsonObject baseObj;
        try
        {
            baseObj = System.Text.Json.Nodes.JsonNode.Parse(await GetAsync(userId)) as System.Text.Json.Nodes.JsonObject
                      ?? new System.Text.Json.Nodes.JsonObject();
        }
        catch (JsonException)
        {
            baseObj = new System.Text.Json.Nodes.JsonObject();   // 库内畸形 → 以 patch 重建（与解析回落口径一致）
        }

        foreach (var kv in patch.ToList())
        {
            if (kv.Value is null) baseObj.Remove(kv.Key);                       // null → 删键（恢复默认）
            else baseObj[kv.Key] = kv.Value.DeepClone();                        // 顶层键整体替换
        }

        await SaveAsync(userId, baseObj.ToJsonString());
        _prefsCache.Remove(userId);                                             // 同请求内后续读取到新值
    }

    /// <inheritdoc/>
    public async Task<string> GetRowModeAsync(Guid userId)
    {
        try
        {
            using var doc = JsonDocument.Parse(await GetCachedAsync(userId));
            if (doc.RootElement.TryGetProperty("rowMode", out var el)
                && el.ValueKind == JsonValueKind.String && el.GetString() == "expanded")
                return "expanded";
        }
        catch (JsonException) { }
        return "merged";
    }
```

- [ ] **Step 4: 跑测试验证 PASS** — `dotnet test CP6.Tests/CP6.Tests.csproj --filter PrefMergeTests`。

- [ ] **Step 5: 控制器端点**

`PrefController.cs`：`SavePrefReq` 换签名 + `Save` 分流 + 新 `NotifyMatrixRows` action（`Get`/`Ok2`/`Err` 既有帮手不动）：

```csharp
    // ── 保存偏好 ──

    /// <summary>Merge=false：整串覆盖（既有行为不变）；Merge=true：服务端顶层键合并写（wfs-inbox-ux §6）。</summary>
    public record SavePrefReq(string PrefsJson, bool Merge = false);

    [HttpPost("save")]
    public async Task<IActionResult> Save([FromBody] SavePrefReq r)
    {
        try
        {
            var me = await CurrentUserIdAsync();
            if (r.Merge) await _pref.SaveMergeAsync(me, r.PrefsJson);
            else await _pref.SaveAsync(me, r.PrefsJson);
            return Ok2();
        }
        catch (InvalidOperationException e) { return Err(e); }
    }

    // ── 通知矩阵元数据（类型轴 + 通道支持标志，驱动设置 UI 格子禁用）──

    [HttpGet("notify-matrix")]
    public IActionResult NotifyMatrixRows() => Ok2(NotifyMatrix.Rows());
```

（`SavePrefReq` 加默认参 `Merge = false`：既有前端只传 `{ prefsJson }`，JSON 绑定缺字段取默认 → **既有调用方零变化**。）

- [ ] **Step 6: 编译 + 回归闸 + commit**

```bash
dotnet build CP6.WebApi/CP6.WebApi.csproj
dotnet test CP6.Tests/CP6.Tests.csproj --filter "PrefServiceTests|PrefMergeTests"   # 既有 PrefServiceTests 照绿（GetNotifyPrefsAsync 未动）
git add -A && git commit -m "feat(wfs-inbox): A-T2 PrefService 矩阵读取/合并写/rowMode + save Merge 分流 + notify-matrix 端点"
```

---


---
## 附: 共享契约(plan全局)
## 共享契约（所有 Task 用这些**精确**名字）

```csharp
// CP6.Core/Services/Oa/NotifyMatrix.cs
public record NotifyMatrixRow(string TypeKey, int TypeValue, bool InAppSupported, bool EmailSupported);
public static class NotifyMatrix
{
    public const string ChannelInApp = "inApp";
    public const string ChannelEmail = "email";
    public static bool IsEnabled(string prefsJson, string type, string channel);
    public static IReadOnlyList<NotifyMatrixRow> Rows();
}

// IPrefService 新增
Task<bool> IsEnabledAsync(Guid userId, string type, string channel);  // per-request 缓存（Scoped 实例内字典）
Task SaveMergeAsync(Guid userId, string partialJson);                 // 顶层键合并；patch 值为 null → 删除该键
Task<string> GetRowModeAsync(Guid userId);                            // "merged" | "expanded"，缺省 merged

// IInboxService 变更/新增
Task<IReadOnlyList<InboxPendingItem>> PendingAsync(Guid userId, string rowMode = "merged", int? page = null, int? pageSize = null);
Task<BatchTransferReport> BatchTransferAsync(Guid actorId, Guid fromUserId, Guid toUserId, string? comment, BatchTransferFilter? filter = null);
Task<BatchTransferPreview> BatchTransferPreviewAsync(Guid fromUserId, BatchTransferFilter? filter = null);

// InboxModels.cs 新增（批量上限常量在 InboxService：private const int MaxBatchTransfer = 500;）
public record BatchTransferFilter(string? FlowKey = null, DateTime? BeforeUtc = null, IReadOnlyList<Guid>? TaskIds = null);
public record BatchTransferItemResult(Guid TaskId, string FlowKey, bool Ok, string? Error);
public record BatchTransferReport(int Total, int Succeeded, IReadOnlyList<BatchTransferItemResult> Failed);
public record BatchTransferPreview(int Total, IReadOnlyList<InboxPendingItem> Sample);   // Sample = 前 10 条
```

```ts
// cp6.web/src/views/oa/settings/notifyMatrixModel.ts
export interface NotifyMatrixRow { typeKey: string; typeValue: number; inAppSupported: boolean; emailSupported: boolean }
export type MatrixState = Record<string, { inApp: boolean; email: boolean }>
export function buildMatrixState(prefsJson: string, rows: NotifyMatrixRow[]): MatrixState
export function toNotifyPatch(state: MatrixState): string        // → '{"notify":{...}}'

// cp6.web/src/views/oa/inbox/inboxModel.ts 新增
export function parseRowMode(prefsJson: string | undefined): 'merged' | 'expanded'
```

- 端点：`POST /api/oa/pref/save`（`SavePrefReq(string PrefsJson, bool Merge = false)`）、`GET /api/oa/pref/notify-matrix`、`GET /api/oa/inbox/pending?rowMode=&page=&pageSize=`、`POST /api/oa/inbox/batch-transfer`、`POST /api/oa/inbox/batch-transfer/preview`。
- 业务错误 i18n 键（不占 E-WF 码，走既有「message=键、前端 t(raw)」口径）：`oa.bt.errSameUser` / `oa.bt.errTargetInvalid` / `oa.bt.errTooMany` / `oa.pref.errBadJson`。
- 通知类型键（camelCase 枚举名）：`todoCreated` / `flowApproved` / `flowRejected` / `timeout` / （`branchPruned` 若枚举已合入）。

## 附: 侦察R2并发口径
### R2 InboxPref 并发口径（spec §6 核实项，结论）

- `Wf_InboxPref : BaseTenantEntity`（`CP6.Entity/DomainModels/Wf/Wf_InboxPref.cs`）——继承链 `BaseTenantEntity : BaseEntity`，**无 RowVersion**（RowVersion 只在 `BaseBizEntity` 上，本表不继承）。
- `PrefService.SaveAsync` 是**整串覆盖**（无合并）；合并目前全在前端 `InboxSettings.vue` 的 `storedRaw` spread。无并发控制，last-write-wins。
- **本计划口径**：零迁移约束下**不加 RowVersion**；新增**服务端顶层键合并写** `SaveMergeAsync`（读-改-写在单请求单 SaveChanges 内完成），把跨会话键覆盖窗口收敛到毫秒级；单用户自改冲突概率可忽略（spec §6 原话），文档化 last-write-wins per top-level key。
