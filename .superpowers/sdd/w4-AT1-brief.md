### Task A-T1: NotifyMatrix 纯函数（IsEnabled 三态坍缩 + 遗留兼容 + 反射类型轴）

**Files:**
- Create: `CP6.Core/Services/Oa/NotifyMatrix.cs`
- Test: `CP6.Tests/Oa/NotifyMatrixTests.cs`

**Interfaces:**
- Consumes: `WfNotificationType`（`CP6.Entity.DomainModels.Wf`，const int 反射）。
- Produces: `NotifyMatrix.IsEnabled(string prefsJson, string type, string channel)`、`NotifyMatrix.Rows()`、常量 `ChannelInApp="inApp"` / `ChannelEmail="email"`、`record NotifyMatrixRow(string TypeKey, int TypeValue, bool InAppSupported, bool EmailSupported)` —— A-T2/A-T3/A-T4 全依赖。

- [ ] **Step 1: 写失败测试**

```csharp
// CP6.Tests/Oa/NotifyMatrixTests.cs
using CP6.Core.Services.Oa;
using Xunit;

namespace CP6.Tests.Oa;

public class NotifyMatrixTests
{
    // ── 三态坍缩：缺行/缺键/缺通道键 → true（spec §2.1）──
    [Theory]
    [InlineData("")]                                            // 空串（等价无行）
    [InlineData("{}")]                                          // 无 notify 键
    [InlineData("""{"notify":{}}""")]                           // notify 空对象（无类型键）
    [InlineData("""{"notify":{"todoCreated":{}}}""")]           // 类型对象存在但无通道键
    [InlineData("NOT_VALID_JSON{{{")]                           // 畸形 JSON 回落 true 不抛
    public void IsEnabled_ThreeStateCollapse_DefaultsTrue(string prefsJson)
    {
        Assert.True(NotifyMatrix.IsEnabled(prefsJson, "todoCreated", NotifyMatrix.ChannelInApp));
        Assert.True(NotifyMatrix.IsEnabled(prefsJson, "todoCreated", NotifyMatrix.ChannelEmail));
    }

    [Fact]
    public void IsEnabled_NewMatrixShape_PerTypePerChannel()
    {
        const string json = """{"notify":{"flowRejected":{"inApp":true,"email":false},"todoCreated":{"inApp":false}}}""";
        Assert.True (NotifyMatrix.IsEnabled(json, "flowRejected", "inApp"));
        Assert.False(NotifyMatrix.IsEnabled(json, "flowRejected", "email"));
        Assert.False(NotifyMatrix.IsEnabled(json, "todoCreated",  "inApp"));
        Assert.True (NotifyMatrix.IsEnabled(json, "todoCreated",  "email"));   // 缺通道键 → true
        Assert.True (NotifyMatrix.IsEnabled(json, "flowApproved", "inApp"));   // 缺类型键 → true
    }

    // ── 遗留扁平形态兼容（C2：既有 notify.{todo,...,email} 语义逐位等价）──
    [Fact]
    public void IsEnabled_LegacyFlat_EventOff_KillsBothChannels()
    {
        const string json = """{"notify":{"todo":false,"email":true}}""";
        Assert.False(NotifyMatrix.IsEnabled(json, "todoCreated", "inApp"));
        Assert.False(NotifyMatrix.IsEnabled(json, "todoCreated", "email"));   // 现状：事件关 → 整跳（含邮件）
        Assert.True (NotifyMatrix.IsEnabled(json, "flowApproved", "inApp")); // 其他事件不受影响
    }

    [Fact]
    public void IsEnabled_LegacyFlat_GlobalEmailOff_KillsOnlyEmail()
    {
        const string json = """{"notify":{"todo":true,"approved":true,"email":false}}""";
        Assert.True (NotifyMatrix.IsEnabled(json, "todoCreated",  "inApp"));
        Assert.False(NotifyMatrix.IsEnabled(json, "todoCreated",  "email"));
        Assert.False(NotifyMatrix.IsEnabled(json, "flowApproved", "email"));
        Assert.False(NotifyMatrix.IsEnabled(json, "flowRejected", "email"));  // 缺 rejected 键也吃全局 email
    }

    [Fact]
    public void IsEnabled_NewShapeWinsOverLegacy_WhenTypeObjectPresent()
    {
        // 同一 notify 里新旧混存：类型键为对象 → 走新形态，无视遗留 email 全局开关
        const string json = """{"notify":{"email":false,"todoCreated":{"inApp":true,"email":true}}}""";
        Assert.True(NotifyMatrix.IsEnabled(json, "todoCreated", "email"));
    }

    // ── 类型轴（反射枚举，数据驱动）+ 邮件动作核定（R1）──
    [Fact]
    public void Rows_ReflectsEnum_WithSupportFlags()
    {
        var rows = NotifyMatrix.Rows();
        Assert.Contains(rows, r => r is { TypeKey: "todoCreated",  TypeValue: 1, InAppSupported: true,  EmailSupported: true });
        Assert.Contains(rows, r => r is { TypeKey: "flowApproved", TypeValue: 2, InAppSupported: true,  EmailSupported: true });
        Assert.Contains(rows, r => r is { TypeKey: "flowRejected", TypeValue: 3, InAppSupported: true,  EmailSupported: true });
        Assert.Contains(rows, r => r is { TypeKey: "timeout",      TypeValue: 4, InAppSupported: false, EmailSupported: false }); // 无发送路径（R1）
        // BranchPruned 未合入时不出现；合入后（hardening spec §4.2）自动长出且双通道 true——不对存在性做负断言，保证两 spec 任意合并顺序都绿
        foreach (var r in rows.Where(r => r.TypeKey == "branchPruned"))
        {
            Assert.True(r.InAppSupported);
            Assert.True(r.EmailSupported);
        }
    }
}
```

- [ ] **Step 2: 跑测试验证 FAIL** — `dotnet test CP6.Tests/CP6.Tests.csproj --filter NotifyMatrixTests`。预期：编译失败（`NotifyMatrix` 不存在）。

- [ ] **Step 3: 最小实现**

```csharp
// CP6.Core/Services/Oa/NotifyMatrix.cs
using System.Reflection;
using System.Text.Json;
using CP6.Entity.DomainModels.Wf;

namespace CP6.Core.Services.Oa;

/// <summary>通知矩阵一行（类型轴 = WfNotificationType 反射；Supported 标志驱动 UI 格子禁用）。</summary>
public record NotifyMatrixRow(string TypeKey, int TypeValue, bool InAppSupported, bool EmailSupported);

/// <summary>
/// 通知偏好矩阵纯函数（wfs-inbox-ux §2）。
/// PrefsJson.notify 新形态：{"notify":{"todoCreated":{"inApp":bool,"email":bool},...}}。
/// 三态坍缩：无行/无 notify 键/无类型键/无通道键/解析失败 → true（默认全开，D2 零迁移）。
/// 遗留扁平形态（{"notify":{"todo":bool,...,"email":bool}}）兼容：类型键非对象时回落——
/// 事件键=false → 双通道关；全局 email=false → 仅邮件关（与既有 ParseNotifyPrefs 语义逐位等价）。
/// </summary>
public static class NotifyMatrix
{
    public const string ChannelInApp = "inApp";
    public const string ChannelEmail = "email";

    /// <summary>新类型键 → 遗留扁平键 映射（仅既有四类型有遗留形态）。</summary>
    private static readonly Dictionary<string, string> LegacyKeyMap = new()
    {
        ["todoCreated"] = "todo",
        ["flowApproved"] = "approved",
        ["flowRejected"] = "rejected",
        ["timeout"] = "timeout",
    };

    /// <summary>
    /// 通道支持清单（2026-07-05 实读核定，R1）：
    /// todoCreated/flowApproved/flowRejected = PersistentWfNotifier 三方法，站内+邮件双动作；
    /// timeout = 全库无生产者（超时提醒以 TodoCreated 发出）→ 双禁用；
    /// branchPruned = hardening spec §4.2 预留（合入 IWfNotifier.BranchPrunedAsync 即双通道生效）。
    /// 未登记的新类型默认 (inApp:true, email:false)——站内可开关、邮件保守禁用。
    /// </summary>
    private static readonly Dictionary<string, (bool InApp, bool Email)> Support = new()
    {
        ["todoCreated"]  = (true, true),
        ["flowApproved"] = (true, true),
        ["flowRejected"] = (true, true),
        ["timeout"]      = (false, false),
        ["branchPruned"] = (true, true),
    };

    public static bool IsEnabled(string prefsJson, string type, string channel)
    {
        if (string.IsNullOrWhiteSpace(prefsJson)) return true;
        try
        {
            using var doc = JsonDocument.Parse(prefsJson);
            if (!doc.RootElement.TryGetProperty("notify", out var notify) || notify.ValueKind != JsonValueKind.Object)
                return true;                                                      // 无 notify 键 → 默认开

            if (notify.TryGetProperty(type, out var typeEl) && typeEl.ValueKind == JsonValueKind.Object)
            {
                // 新矩阵形态：仅字面 false 为关；缺通道键/true/非布尔 → 开
                return !(typeEl.TryGetProperty(channel, out var ch) && ch.ValueKind == JsonValueKind.False);
            }

            // 遗留扁平形态回落（C2）
            if (!LegacyKeyMap.TryGetValue(type, out var legacyKey)) return true;  // 新类型无遗留形态 → 开
            var eventOn = !(notify.TryGetProperty(legacyKey, out var ev) && ev.ValueKind == JsonValueKind.False);
            if (channel == ChannelInApp) return eventOn;
            var emailOn = !(notify.TryGetProperty("email", out var em) && em.ValueKind == JsonValueKind.False);
            return eventOn && emailOn;                                            // 既有语义：事件关→整跳；email 关→仅邮件跳
        }
        catch (JsonException)
        {
            return true;                                                          // 畸形 JSON → 默认开（与 ParseNotifyPrefs 一致）
        }
    }

    /// <summary>类型轴 = 反射 WfNotificationType 全部 public const int（BranchPruned 合入即自动长出）。</summary>
    public static IReadOnlyList<NotifyMatrixRow> Rows() =>
        typeof(WfNotificationType)
            .GetFields(BindingFlags.Public | BindingFlags.Static)
            .Where(f => f.IsLiteral && f.FieldType == typeof(int))
            .Select(f =>
            {
                var key = char.ToLowerInvariant(f.Name[0]) + f.Name[1..];         // TodoCreated → todoCreated
                var (inApp, email) = Support.TryGetValue(key, out var s) ? s : (true, false);
                return new NotifyMatrixRow(key, (int)f.GetRawConstantValue()!, inApp, email);
            })
            .OrderBy(r => r.TypeValue)
            .ToList();
}
```

- [ ] **Step 4: 跑测试验证 PASS** — `dotnet test CP6.Tests/CP6.Tests.csproj --filter NotifyMatrixTests`，预期全绿。

- [ ] **Step 5: 全量回归闸 + commit**

```bash
dotnet test CP6.Tests/CP6.Tests.csproj --filter "Oa|Wf"    # 既有 Oa/Wf 照绿
git add -A && git commit -m "feat(wfs-inbox): A-T1 NotifyMatrix 三态坍缩+遗留扁平兼容+反射类型轴"
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

## 附: 侦察R1通知栈现状+冲突C1/C2/C3
### R1 通知栈现状

- `WfNotificationType`（`CP6.Entity/DomainModels/Wf/WfNotificationType.cs`）**实际值域 4 个 const int**：`TodoCreated=1, FlowApproved=2, FlowRejected=3, Timeout=4`。`BranchPruned` **尚未合入**（hardening spec §4.2 同期新增）。
- `IWfNotifier`（`CP6.Core/Services/Wf/IWfNotifier.cs`）只有 3 个方法：`TodoCreatedAsync / FlowApprovedAsync / FlowRejectedAsync`。**没有 TimeoutAsync**。
- **邮件动作清单**（矩阵格子禁用依据）：`PersistentWfNotifier`（`CP6.WebApi/Services/PersistentWfNotifier.cs`）3 个方法都有 `TrySendEmailAsync` 邮件动作 → **todoCreated / flowApproved / flowRejected 双通道有效**；`Timeout(4)` **全库无生产者**（`WfTimeoutService.ScanOnceAsync` 的 remind/escalate 均调 `TodoCreatedAsync`，以 Type=1 发出）→ **timeout 行 inApp+email 双格子禁用**（带提示，数据驱动，将来接独立发送路径自动点亮）。
- **既有偏好机制**（关键）：`IPrefService.GetNotifyPrefsAsync` → `PrefService.ParseNotifyPrefs`（`CP6.Core/Services/Oa/PrefService.cs:38-62`）已解析 `PrefsJson.notify` 键，但是**扁平形态** `{"notify":{"todo":bool,"approved":bool,"rejected":bool,"timeout":bool,"email":bool}}`——事件开关 + 单一全局 email 开关，非矩阵。`notify` 键已被占用。
### Spec 与现状冲突登记（**不改 spec**，实现取向如下）

| # | 冲突 | 实现取向 |
|---|------|---------|
| C1 | spec §2.1 示例键 `taskArrived` vs 实际枚举 `TodoCreated` | spec 自注「示意，按实际枚举对齐」→ 类型键 = camelCase 枚举名：`todoCreated/flowApproved/flowRejected/timeout`（+`branchPruned` 若合入） |
| C2 | spec D2「notify 新键零迁移」 vs `notify` 键已被**扁平形态**占用（含用户已存的 `todo:false` 等） | `IsEnabled` 在类型键非对象时回落解析遗留扁平键（事件关→双通道关；`email:false`→仅邮件关），**语义与现状逐位等价** = D2「向后兼容零数据迁移」的落实；矩阵 UI 保存后写新嵌套形态整体替换 `notify` 键 |
| C3 | spec §2.1 示例含 timeout 行 email 开关 vs Timeout **无任何发送路径**（含邮件） | timeout 行保留（类型轴=枚举值域）但 inApp+email 双格禁用 + 提示（spec §2.3 授权 plan 核定格子有效性） |
| C4 | spec §3.1 `权限点 OA.Inbox.BatchTransfer` vs 实际机制 (menuKey, action) 二元组 | 映射为 `("oa-inbox", "batch-transfer")`，见 R4 |
| C5 | spec §5「merged=默认=现状」 vs `PendingAsync` 现状实为逐任务行 | 按 spec 文本执行：merged 为默认。行为差异仅限「同实例多待办同人」场景（并行分支/会签同人），QA 走查确认 |
| C6 | spec §4「Sign Records 弹窗全屏化」 vs 无独立签核弹窗 | 对应现状落点 = FlowTimeline 堆叠 + TransferDialog/SendBackDialog 移动端全屏（`width 100vw`） |
| C7 | spec §3.1 `filter.beforeUtc` vs 库内 `CreateDate` 为服务器本地时（`DateTime.Now` 全库惯例） | 参数名照 spec，直接与 `CreateDate` 比较；QA README 注明传服务器本地时刻 |
