### Task D-T1: 后端查询层 rowMode 分组 + 分页正确性

**Files:**
- Modify: `CP6.Core/Services/Oa/IInboxService.cs`（PendingAsync 签名）
- Modify: `CP6.Core/Services/Oa/InboxService.cs`（PendingAsync 实现）
- Modify: `CP6.WebApi/Controllers/Oa/InboxController.cs`（pending 端点参数 + 注入 IPrefService）
- Test: `CP6.Tests/Oa/PendingRowModeTests.cs`、`CP6.Tests/Oa/PrefMergeTests.cs`（GetRowModeAsync 用例追加）

**Interfaces:**
- Consumes: `IPrefService.GetRowModeAsync`（A-T2 已实现，本任务补测试与消费方）；`DoneAsync` 既有合并口径（`GroupBy(InstanceId)→OrderByDescending→First`，R5）。
- Produces: `Task<IReadOnlyList<InboxPendingItem>> PendingAsync(Guid userId, string rowMode = "merged", int? page = null, int? pageSize = null)`；`GET /api/oa/inbox/pending?rowMode=&page=&pageSize=`（rowMode 缺省 → 读**查看者本人**（me，非 act-as 被代理人）的偏好）。D-T2 前端与 B-T1 preview 依赖。
- **不变量**：merged 分组**先于** Skip/Take（同实例任务永不跨页出现两行）；expanded = 现状逐任务行；page/pageSize 缺省 = 全量（现状零变化）。

- [ ] **Step 1: 写失败测试**

```csharp
// CP6.Tests/Oa/PendingRowModeTests.cs
using CP6.Core.EFDbContext;
using CP6.Core.Services.Oa;
using CP6.Core.Services.Wf;
using CP6.Entity.DomainModels.Sys;
using CP6.Entity.DomainModels.Wf;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using System.Text.Json;
using Xunit;

namespace CP6.Tests.Oa;

public class PendingRowModeTests
{
    private static CP6Context NewDb() => new(new DbContextOptionsBuilder<CP6Context>()
        .UseInMemoryDatabase(Guid.NewGuid().ToString())
        .ConfigureWarnings(w => w.Ignore(InMemoryEventId.TransactionIgnoredWarning)).Options);
    private static FlowEngine Engine(CP6Context db) => new(db, new ApproverResolver(db));
    private static IInboxService Inbox(CP6Context db) => new InboxService(db, Engine(db),
        new ForecastService(db, new ApproverResolver(db), new ApprovalStagePlanner(new ApproverResolver(db))));

    /// <summary>并行三分支同审批人 → 同实例 3 个 Pending 任务（多状态多行素材）。返回 instanceId。</summary>
    private static async Task<Guid> SeedParallelSameApproverAsync(CP6Context db, Guid starter, Guid approver, string flowKey)
    {
        if (!await db.Sys_Users.AnyAsync(u => u.Id == starter))
            db.Sys_Users.Add(new Sys_User { Id = starter, UserName = "s", NickName = "发起人", Password = "x" });
        if (!await db.Sys_Users.AnyAsync(u => u.Id == approver))
            db.Sys_Users.Add(new Sys_User { Id = approver, UserName = "a", NickName = "审批人", Password = "x" });
        var schema = new FlowSchema
        {
            Nodes =
            {
                new FlowNode { Id = "split", Type = "parallelSplit" },
                new FlowNode { Id = "n1", Type = "approval", ApproverStrategy = "Specified", ApproverUserId = approver },
                new FlowNode { Id = "n2", Type = "approval", ApproverStrategy = "Specified", ApproverUserId = approver },
                new FlowNode { Id = "n3", Type = "approval", ApproverStrategy = "Specified", ApproverUserId = approver },
                new FlowNode { Id = "join", Type = "parallelJoin" },
                new FlowNode { Id = "end", Type = "end" },
            },
            Edges =
            {
                new FlowEdge { From = "split", To = "n1" },
                new FlowEdge { From = "split", To = "n2" },
                new FlowEdge { From = "split", To = "n3" },
                new FlowEdge { From = "n1", To = "join" },
                new FlowEdge { From = "n2", To = "join" },
                new FlowEdge { From = "n3", To = "join" },
                new FlowEdge { From = "join", To = "end" },
            },
        };
        db.Wf_FlowDefs.Add(new Wf_FlowDef { Id = Guid.NewGuid(), FlowKey = flowKey, FlowName = flowKey, FormKey = flowKey,
            SchemaJson = JsonSerializer.Serialize(schema), Version = 1, Enable = true });
        await db.SaveChangesAsync();
        return await Engine(db).SubmitAsync(flowKey, starter, "{}");
    }

    /// <summary>把用户全部 Pending 任务的 CreateDate 摆成确定性阶梯（排序/分页稳定）。</summary>
    private static async Task StaircaseAsync(CP6Context db, Guid approver)
    {
        var tasks = await db.Wf_FlowTasks.Where(t => t.AssigneeId == approver)
            .OrderBy(t => t.Id).ToListAsync();
        var baseline = new DateTime(2026, 7, 1, 8, 0, 0);
        for (var i = 0; i < tasks.Count; i++) tasks[i].CreateDate = baseline.AddMinutes(i);
        await db.SaveChangesAsync();
    }

    [Fact]
    public async Task Merged_SameInstanceThreeTasks_CollapsesToOneRow_LatestState()
    {
        using var db = NewDb();
        var starter = Guid.NewGuid(); var approver = Guid.NewGuid();
        var instId = await SeedParallelSameApproverAsync(db, starter, approver, "par");
        await StaircaseAsync(db, approver);

        var rows = await Inbox(db).PendingAsync(approver, rowMode: "merged");

        var row = Assert.Single(rows);
        Assert.Equal(instId, row.InstanceId);
        // 显最新态：合并行 = CreateDate 最大的那个任务
        var latest = await db.Wf_FlowTasks.Where(t => t.AssigneeId == approver)
            .OrderByDescending(t => t.CreateDate).FirstAsync();
        Assert.Equal(latest.Id, row.TaskId);
    }

    [Fact]
    public async Task Expanded_SameInstanceThreeTasks_ThreeRows()
    {
        using var db = NewDb();
        var starter = Guid.NewGuid(); var approver = Guid.NewGuid();
        await SeedParallelSameApproverAsync(db, starter, approver, "par");

        var rows = await Inbox(db).PendingAsync(approver, rowMode: "expanded");
        Assert.Equal(3, rows.Count);
        Assert.Equal(3, rows.Select(r => r.TaskId).Distinct().Count());
    }

    [Fact]
    public async Task Default_IsMerged()
    {
        using var db = NewDb();
        var starter = Guid.NewGuid(); var approver = Guid.NewGuid();
        await SeedParallelSameApproverAsync(db, starter, approver, "par");

        Assert.Single(await Inbox(db).PendingAsync(approver));   // 缺省参数 = merged（spec D5）
    }

    // ── 分页正确性：同实例 3 任务跨页界（spec §7）──
    [Fact]
    public async Task Merged_Paging_GroupsBeforeSkipTake_NoInstanceStraddlesPages()
    {
        using var db = NewDb();
        var starter = Guid.NewGuid(); var approver = Guid.NewGuid();
        var instA = await SeedParallelSameApproverAsync(db, starter, approver, "parA");   // 3 任务
        var instB = await SeedParallelSameApproverAsync(db, starter, approver, "parB");   // 3 任务
        await StaircaseAsync(db, approver);                                                // A(0-2分) < B(3-5分)

        var page1 = await Inbox(db).PendingAsync(approver, "merged", page: 1, pageSize: 1);
        var page2 = await Inbox(db).PendingAsync(approver, "merged", page: 2, pageSize: 1);
        var page3 = await Inbox(db).PendingAsync(approver, "merged", page: 3, pageSize: 1);

        Assert.Equal(instB, Assert.Single(page1).InstanceId);   // 分组后按最新 CreateDate 倒序
        Assert.Equal(instA, Assert.Single(page2).InstanceId);
        Assert.Empty(page3);                                     // 若分组晚于分页会错误地出现第 3 页
    }

    [Fact]
    public async Task Expanded_Paging_SkipTakeOverTaskRows()
    {
        using var db = NewDb();
        var starter = Guid.NewGuid(); var approver = Guid.NewGuid();
        await SeedParallelSameApproverAsync(db, starter, approver, "par");
        await StaircaseAsync(db, approver);

        var page1 = await Inbox(db).PendingAsync(approver, "expanded", page: 1, pageSize: 2);
        var page2 = await Inbox(db).PendingAsync(approver, "expanded", page: 2, pageSize: 2);

        Assert.Equal(2, page1.Count);
        Assert.Single(page2);
        Assert.Equal(3, page1.Concat(page2).Select(r => r.TaskId).Distinct().Count());   // 无重复无遗漏
    }

    [Fact]
    public async Task NoPaging_ReturnsAll_BehaviourUnchanged()
    {
        using var db = NewDb();
        var starter = Guid.NewGuid(); var approver = Guid.NewGuid();
        await SeedParallelSameApproverAsync(db, starter, approver, "par");
        Assert.Equal(3, (await Inbox(db).PendingAsync(approver, "expanded")).Count);
    }
}
```

`PrefMergeTests.cs` 追加 GetRowModeAsync 用例：

```csharp
    // ── GetRowModeAsync（D-T1 消费）──
    [Theory]
    [InlineData(null, "merged")]                                  // 无行 → 默认
    [InlineData("{}", "merged")]                                  // 无键 → 默认
    [InlineData("""{"rowMode":"expanded"}""", "expanded")]
    [InlineData("""{"rowMode":"merged"}""", "merged")]
    [InlineData("""{"rowMode":"garbage"}""", "merged")]           // 非法值 → 默认
    [InlineData("NOT_JSON{{{", "merged")]                         // 畸形 → 默认
    public async Task GetRowMode_ParsesTopLevelKey_DefaultMerged(string? prefsJson, string expected)
    {
        using var db = NewDb();
        var me = Guid.NewGuid();
        if (prefsJson is not null)
        {
            db.Wf_InboxPrefs.Add(new Wf_InboxPref { Id = Guid.NewGuid(), UserId = me, PrefsJson = prefsJson });
            await db.SaveChangesAsync();
        }
        Assert.Equal(expected, await Svc(db).GetRowModeAsync(me));
    }
```

- [ ] **Step 2: 跑测试验证 FAIL** — `dotnet test CP6.Tests/CP6.Tests.csproj --filter "PendingRowModeTests"`。预期：编译失败（PendingAsync 无该签名）。

- [ ] **Step 3: 实现**

`IInboxService.cs` 的 PendingAsync 行替换为：

```csharp
    // ── 未處理（T5 + wfs-inbox-ux §5 rowMode）──
    // rowMode: "merged"(默认，同实例多任务合并一行显最新态) | "expanded"(逐任务平铺)。
    // page/pageSize 可选（null=全量，现状不变）；merged 下分组先于分页（跨页正确性）。
    Task<IReadOnlyList<InboxPendingItem>> PendingAsync(Guid userId, string rowMode = "merged", int? page = null, int? pageSize = null);
```

`InboxService.cs` 的 `PendingAsync` 方法签名改为上式，方法体在 `.ToListAsync()`（:30）与「Batch-load frozen stage plans」段（:32）之间插入分组+分页（分组口径逐字照 `DoneAsync:143-144`）：

```csharp
        // ── rowMode（wfs-inbox-ux §5）：merged=同实例合并取最新（照 DoneAsync 既有口径）；分组先于分页 ──
        if (rowMode != "expanded")
            rows = rows.GroupBy(x => x.i.Id)
                       .Select(g => g.OrderByDescending(x => x.t.CreateDate).First())
                       .OrderByDescending(x => x.t.CreateDate)
                       .ToList();
        if (page is { } p && pageSize is { } ps && p >= 1 && ps >= 1)
            rows = rows.Skip((p - 1) * ps).Take(ps).ToList();
```

（`rows` 为匿名类型列表，`var` 推断不变；后续 stage plan 批量加载与投影零改。）

`InboxController.cs`：ctor 注入 `IPrefService`（字段 `_pref`，构造参数照既有风格追加）；`Pending` action 替换为：

```csharp
    [HttpGet("pending")]
    public async Task<IActionResult> Pending([FromQuery] string? rowMode = null,
        [FromQuery] int? page = null, [FromQuery] int? pageSize = null)
    {
        try
        {
            var (eff, _) = await EffectiveAsync();
            // 显示偏好属查看者本人（me），与 act-as 被代理人（eff）无关
            var me = await CurrentUserIdAsync();
            var mode = rowMode is "merged" or "expanded" ? rowMode : await _pref.GetRowModeAsync(me);
            return Ok2(await _inbox.PendingAsync(eff, mode, page, pageSize));
        }
        catch (InvalidOperationException e) { return Err(e); }
    }
```

（B-T1 的 `BatchTransferPreviewAsync` 若此前以旧签名调用 `PendingAsync(fromUserId)`，本步改为 `PendingAsync(fromUserId, rowMode: "expanded")`——见 B-T1 注。）

- [ ] **Step 4: 跑测试验证 PASS** — `dotnet test CP6.Tests/CP6.Tests.csproj --filter "PendingRowModeTests|PrefMergeTests"`。

- [ ] **Step 5: 回归闸 + commit** — **关键回归**：既有 `InboxServiceTests.Pending_*` / `SerialInboxDtoTests` 等均为单任务实例 → merged 分组对其无观察差异，必须照绿；C5 冲突已登记（多任务同实例场景默认行为变化 = spec D5 明文要求）。

```bash
dotnet test CP6.Tests/CP6.Tests.csproj --filter "Oa|Wf"
git add -A && git commit -m "feat(wfs-inbox): D-T1 PendingAsync rowMode合并/平铺+分组先于分页+端点偏好回落"
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

## 附: R5 rowMode现状+冲突C5
### R5 rowMode / 列表查询现状

- `InboxService.PendingAsync`（`InboxService.cs:19-65`）**现状 = 每任务一行（即 expanded 形态）**，无分组无分页；`DoneAsync`（:142-147, :169-171）已有「`GroupBy(InstanceId).OrderByDescending.First()` 取最新」合并口径——merged 模式照抄此口径。
- 信箱列表端点均无分页参数；设置页 `pageSize` 偏好存在但列表未消费。rowMode 分页正确性通过服务层可选 `page/pageSize` 参数落地（分组先于 Skip/Take），前端列表暂维持全量拉取（现状），参数供测试与后续消费。
| C5 | spec §5「merged=默认=现状」 vs `PendingAsync` 现状实为逐任务行 | 按 spec 文本执行：merged 为默认。行为差异仅限「同实例多待办同人」场景（并行分支/会签同人），QA 走查确认 |
