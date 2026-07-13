### Task B-T1: InboxService.BatchTransferAsync + Preview（逐条独立事务 + 汇总报告）

**Files:**
- Modify: `CP6.Core/Services/Oa/InboxModels.cs`（record 族）
- Modify: `CP6.Core/Services/Oa/IInboxService.cs`
- Modify: `CP6.Core/Services/Oa/InboxService.cs`
- Test: `CP6.Tests/Oa/BatchTransferTests.cs`

**Interfaces:**
- Consumes: `IFlowEngine.TransferAsync(Guid taskId, Guid actorId, Guid toUserId, string? comment = null)`（**只调用不改动**，引擎内部单次 SaveChanges = 单条独立事务，R3）。
- Produces（共享契约原文）：`BatchTransferFilter / BatchTransferItemResult / BatchTransferReport / BatchTransferPreview` record 族 + `BatchTransferAsync` / `BatchTransferPreviewAsync`——B-T2 端点与 B-T3 UI 依赖。

- [ ] **Step 1: 写失败测试**

```csharp
// CP6.Tests/Oa/BatchTransferTests.cs
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

public class BatchTransferTests
{
    // 脚手架照 InboxServiceTests：InMemory + 真引擎 + 真 ForecastService
    private static CP6Context NewDb() => new(new DbContextOptionsBuilder<CP6Context>()
        .UseInMemoryDatabase(Guid.NewGuid().ToString())
        .ConfigureWarnings(w => w.Ignore(InMemoryEventId.TransactionIgnoredWarning)).Options);
    private static FlowEngine Engine(CP6Context db) => new(db, new ApproverResolver(db));
    private static IInboxService Inbox(CP6Context db) => new InboxService(db, Engine(db),
        new ForecastService(db, new ApproverResolver(db), new ApprovalStagePlanner(new ApproverResolver(db))));

    /// <summary>种 n 个实例（同一 flowKey）全部待办压给 from。返回 taskId 列表（按提交序）。</summary>
    private static async Task<List<Guid>> SeedPendingAsync(CP6Context db, Guid starter, Guid from, int n, string flowKey = "leave")
    {
        if (!await db.Sys_Users.AnyAsync(u => u.Id == starter))
            db.Sys_Users.Add(new Sys_User { Id = starter, UserName = "starter", NickName = "发起人", Password = "x" });
        if (!await db.Sys_Users.AnyAsync(u => u.Id == from))
            db.Sys_Users.Add(new Sys_User { Id = from, UserName = "from", NickName = "转出人", Password = "x" });
        if (!await db.Wf_FlowDefs.AnyAsync(d => d.FlowKey == flowKey))
            db.Wf_FlowDefs.Add(new Wf_FlowDef { Id = Guid.NewGuid(), FlowKey = flowKey, FlowName = flowKey, FormKey = flowKey,
                SchemaJson = JsonSerializer.Serialize(new FlowSchema {
                    Nodes = { new FlowNode { Id = "n1", Type = "approval", ApproverStrategy = "Specified", ApproverUserId = from },
                              new FlowNode { Id = "end", Type = "end" } },
                    Edges = { new FlowEdge { From = "n1", To = "end" } } }),
                Version = 1, Enable = true });
        await db.SaveChangesAsync();
        var ids = new List<Guid>();
        for (var i = 0; i < n; i++)
        {
            var instId = await Engine(db).SubmitAsync(flowKey, starter, "{}");
            ids.Add((await db.Wf_FlowTasks.SingleAsync(t => t.InstanceId == instId)).Id);
        }
        return ids;
    }

    private static Sys_User NewEnabledUser(Guid id, string name) =>
        new() { Id = id, UserName = name, NickName = name, Password = "x", Enable = true };

    // ── 部分成功 + 汇总（spec §7：逐条事务部分成功、失败明细）──
    [Fact]
    public async Task Batch_PartialSuccess_DirtyRowDoesNotBlockOthers()
    {
        using var db = NewDb();
        var starter = Guid.NewGuid(); var from = Guid.NewGuid();
        var to = Guid.NewGuid(); var admin = Guid.NewGuid();
        db.Sys_Users.Add(NewEnabledUser(to, "to"));
        await db.SaveChangesAsync();
        var taskIds = await SeedPendingAsync(db, starter, from, 3);

        // 弄脏中间一条：先办结（Status != Pending → TransferAsync 抛 E-WF-002）
        await Engine(db).ActAsync(taskIds[1], from, approve: true, comment: null);

        var report = await Inbox(db).BatchTransferAsync(admin, from, to, "离职移交");

        Assert.Equal(2, report.Total);          // 已办结那条不再是 Pending → 不入候选
        Assert.Equal(2, report.Succeeded);
        Assert.Empty(report.Failed);
        Assert.Equal(2, await db.Wf_FlowTasks.CountAsync(t => t.AssigneeId == to && t.Status == FlowTaskStatus.Pending));
    }

    [Fact]
    public async Task Batch_ExplicitTaskIds_DirtyRow_FailsWithDetail_ContinuesRest()
    {
        using var db = NewDb();
        var starter = Guid.NewGuid(); var from = Guid.NewGuid();
        var to = Guid.NewGuid(); var admin = Guid.NewGuid();
        db.Sys_Users.Add(NewEnabledUser(to, "to"));
        await db.SaveChangesAsync();
        var taskIds = await SeedPendingAsync(db, starter, from, 3);

        // 显式点名 3 条（重试口径：TaskIds 命中时不预筛状态，让引擎裁决）；第 2 条已办结
        await Engine(db).ActAsync(taskIds[1], from, approve: true, comment: null);
        var report = await Inbox(db).BatchTransferAsync(admin, from, to, null,
            new BatchTransferFilter(TaskIds: taskIds));

        Assert.Equal(3, report.Total);                                   // 点名 3 条全入候选
        Assert.Equal(2, report.Succeeded);                               // 循环中段失败不中断后续（D3）
        var f = Assert.Single(report.Failed);                            // 失败以明细行呈现（spec §3.2 重试同口径）
        Assert.Equal(taskIds[1], f.TaskId);
        Assert.Equal("E-WF-002", f.Error);                               // 引擎语义原样透出
        Assert.Equal("leave", f.FlowKey);
    }

    // ── 上限 500（spec §3.1 防御）──
    [Fact]
    public async Task Batch_Over500_Rejected_WithHintKey()
    {
        using var db = NewDb();
        var starter = Guid.NewGuid(); var from = Guid.NewGuid();
        var to = Guid.NewGuid();
        db.Sys_Users.Add(NewEnabledUser(to, "to"));
        await db.SaveChangesAsync();
        await SeedPendingAsync(db, starter, from, 501);

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(
            () => Inbox(db).BatchTransferAsync(Guid.NewGuid(), from, to, null));
        Assert.Equal("oa.bt.errTooMany", ex.Message);
        Assert.Equal(501, await db.Wf_FlowTasks.CountAsync(t => t.AssigneeId == from));   // 一条都没转（前置校验）
    }

    // ── from==to / to 停用 / to 不存在（跨租户同路径）──
    [Fact]
    public async Task Batch_FromEqualsTo_Rejected()
    {
        using var db = NewDb();
        var from = Guid.NewGuid();
        var ex = await Assert.ThrowsAsync<InvalidOperationException>(
            () => Inbox(db).BatchTransferAsync(Guid.NewGuid(), from, from, null));
        Assert.Equal("oa.bt.errSameUser", ex.Message);
    }

    [Fact]
    public async Task Batch_TargetDisabled_Rejected()
    {
        using var db = NewDb();
        var to = Guid.NewGuid();
        db.Sys_Users.Add(new Sys_User { Id = to, UserName = "to", NickName = "停用者", Password = "x", Enable = false });
        await db.SaveChangesAsync();
        var ex = await Assert.ThrowsAsync<InvalidOperationException>(
            () => Inbox(db).BatchTransferAsync(Guid.NewGuid(), Guid.NewGuid(), to, null));
        Assert.Equal("oa.bt.errTargetInvalid", ex.Message);
    }

    [Fact]
    public async Task Batch_TargetCrossTenant_Rejected_SamePathAsMissing()
    {
        using var db = NewDb();
        var to = Guid.NewGuid();
        // 显式设他租户（StampTenant 只盖 TenantId==Guid.Empty 的新增行，CP6Context.cs:2211-2213）
        db.Sys_Users.Add(new Sys_User { Id = to, UserName = "alien", NickName = "他租户", Password = "x",
            Enable = true, TenantId = Guid.NewGuid() });
        await db.SaveChangesAsync();
        // 全局查询过滤器（TenantId==CurrentTenantId）查不到 → 与不存在同路径拒绝
        var ex = await Assert.ThrowsAsync<InvalidOperationException>(
            () => Inbox(db).BatchTransferAsync(Guid.NewGuid(), Guid.NewGuid(), to, null));
        Assert.Equal("oa.bt.errTargetInvalid", ex.Message);
    }

    // ── 审计与引擎语义（spec §7：审计行齐全、TransferAsync 语义不变回归）──
    [Fact]
    public async Task Batch_WritesEngineAudit_HistoryAndFormToPair_PerTask()
    {
        using var db = NewDb();
        var starter = Guid.NewGuid(); var from = Guid.NewGuid();
        var to = Guid.NewGuid(); var admin = Guid.NewGuid();
        db.Sys_Users.Add(NewEnabledUser(to, "to"));
        await db.SaveChangesAsync();
        await SeedPendingAsync(db, starter, from, 2);

        await Inbox(db).BatchTransferAsync(admin, from, to, "移交");

        // Wf_FlowHistory：每条一行 action=transfer，ActorId=操作者（admin）
        Assert.Equal(2, await db.Wf_FlowHistories.CountAsync(h => h.Action == "transfer" && h.ActorId == admin));
        // Wf_FlowFormTo 双行：原行 Transferred(ActualHandlerId=from) + 新 Pending 行(ExpectedHandlerId=to)
        Assert.Equal(2, await db.Wf_FlowFormTos.CountAsync(f => f.Status == FlowFormToStatus.Transferred && f.ActualHandlerId == from));
        Assert.Equal(2, await db.Wf_FlowFormTos.CountAsync(f => f.Status == FlowFormToStatus.Pending && f.ExpectedHandlerId == to));
    }

    // ── filter 收窄 + preview ──
    [Fact]
    public async Task Batch_FilterByFlowKey_NarrowsCandidates()
    {
        using var db = NewDb();
        var starter = Guid.NewGuid(); var from = Guid.NewGuid();
        var to = Guid.NewGuid();
        db.Sys_Users.Add(NewEnabledUser(to, "to"));
        await db.SaveChangesAsync();
        await SeedPendingAsync(db, starter, from, 2, flowKey: "leave");
        await SeedPendingAsync(db, starter, from, 1, flowKey: "expense");

        var report = await Inbox(db).BatchTransferAsync(Guid.NewGuid(), from, to, null,
            new BatchTransferFilter(FlowKey: "leave"));

        Assert.Equal(2, report.Total);
        Assert.Equal(1, await db.Wf_FlowTasks.CountAsync(t => t.AssigneeId == from && t.Status == FlowTaskStatus.Pending)); // expense 留下
    }

    [Fact]
    public async Task Preview_ReturnsTotalAndSample_WithoutTransferring()
    {
        using var db = NewDb();
        var starter = Guid.NewGuid(); var from = Guid.NewGuid();
        await SeedPendingAsync(db, starter, from, 12);

        var preview = await Inbox(db).BatchTransferPreviewAsync(from);

        Assert.Equal(12, preview.Total);
        Assert.Equal(10, preview.Sample.Count);                                            // 抽样前 10
        Assert.Equal(12, await db.Wf_FlowTasks.CountAsync(t => t.AssigneeId == from));     // 只读
    }
}
```

- [ ] **Step 2: 跑测试验证 FAIL** — `dotnet test CP6.Tests/CP6.Tests.csproj --filter BatchTransferTests`。预期：编译失败（record/方法不存在）。

- [ ] **Step 3: 实现**

`InboxModels.cs` 末尾追加：

```csharp
// ── 在途批量转单（wfs-inbox-ux §3）──
public record BatchTransferFilter(string? FlowKey = null, DateTime? BeforeUtc = null, IReadOnlyList<Guid>? TaskIds = null);
public record BatchTransferItemResult(Guid TaskId, string FlowKey, bool Ok, string? Error);
public record BatchTransferReport(int Total, int Succeeded, IReadOnlyList<BatchTransferItemResult> Failed);
public record BatchTransferPreview(int Total, IReadOnlyList<InboxPendingItem> Sample);
```

`IInboxService.cs` 追加：

```csharp
    // ── 在途批量转单（wfs-inbox-ux §3）── actorId=操作者（管理员本人）；逐条独立事务（引擎 TransferAsync 内部 SaveChanges）
    Task<BatchTransferReport> BatchTransferAsync(Guid actorId, Guid fromUserId, Guid toUserId, string? comment, BatchTransferFilter? filter = null);
    Task<BatchTransferPreview> BatchTransferPreviewAsync(Guid fromUserId, BatchTransferFilter? filter = null);
```

`InboxService.cs` 追加（仿 `ActBatchAsAsync` 循环口径 :199-217）：

```csharp
    // ── 在途批量转单（wfs-inbox-ux §3，D3：逐条独立事务 + 汇总报告）──────────

    private const int MaxBatchTransfer = 500;

    /// <summary>
    /// 候选查询。常规路径：from 的全部 Pending 待办（Running 实例）按 filter 收窄；
    /// BeforeUtc 直接比对 CreateDate（库内为服务器本地时，C7）。
    /// <b>TaskIds 显式点名（=单条重试口径，spec §3.2）</b>：不预筛任务/实例状态，让引擎
    /// TransferAsync 裁决——已办结等脏数据以失败明细行（E-WF-002）呈现，不特殊处理；
    /// 仍保留 AssigneeId==from 归属过滤（已被转走的任务不再属 from，绝不能改派他人任务）。
    /// </summary>
    private async Task<List<(Guid TaskId, string FlowKey)>> QueryTransferCandidatesAsync(Guid fromUserId, BatchTransferFilter? f)
    {
        if (f?.TaskIds is { Count: > 0 } ids)
        {
            var named = await (from t in _db.Wf_FlowTasks
                               where t.AssigneeId == fromUserId && ids.Contains(t.Id)
                               join i in _db.Wf_FlowInstances on t.InstanceId equals i.Id
                               orderby t.CreateDate
                               select new { t.Id, i.FlowKey }).ToListAsync();
            return named.Select(x => (x.Id, x.FlowKey)).ToList();
        }

        var q = from t in _db.Wf_FlowTasks
                where t.AssigneeId == fromUserId && t.Status == FlowTaskStatus.Pending
                join i in _db.Wf_FlowInstances on t.InstanceId equals i.Id
                where i.Status == FlowInstanceStatus.Running
                select new { t.Id, i.FlowKey, t.CreateDate };
        if (!string.IsNullOrWhiteSpace(f?.FlowKey)) q = q.Where(x => x.FlowKey == f.FlowKey);
        if (f?.BeforeUtc is { } before) q = q.Where(x => x.CreateDate < before);
        var rows = await q.OrderBy(x => x.CreateDate).ToListAsync();
        return rows.Select(x => (x.Id, x.FlowKey)).ToList();
    }

    /// <inheritdoc/>
    public async Task<BatchTransferReport> BatchTransferAsync(
        Guid actorId, Guid fromUserId, Guid toUserId, string? comment, BatchTransferFilter? filter = null)
    {
        // 前置校验（入参级，400 口径，不占 E-WF 码）
        if (fromUserId == toUserId)
            throw new InvalidOperationException("oa.bt.errSameUser");
        var to = await _db.Sys_Users.FirstOrDefaultAsync(u => u.Id == toUserId);   // 全局租户过滤器：跨租户查不到（R3）
        if (to is null || !to.Enable)
            throw new InvalidOperationException("oa.bt.errTargetInvalid");

        var candidates = await QueryTransferCandidatesAsync(fromUserId, filter);
        if (candidates.Count > MaxBatchTransfer)
            throw new InvalidOperationException("oa.bt.errTooMany");               // 超上限 → 提示分批（防长事务假象与超时）

        var failed = new List<BatchTransferItemResult>();
        var succeeded = 0;
        foreach (var (taskId, flowKey) in candidates)
        {
            try
            {
                // 引擎动作只调用不改动：内部校验 + FormTo 双行 + history + 通知 + 单次 SaveChanges（=单条独立事务）
                await _engine.TransferAsync(taskId, actorId, toUserId, comment);
                succeeded++;
            }
            catch (InvalidOperationException e)                                    // 单条失败不中断后续（D3）
            {
                failed.Add(new BatchTransferItemResult(taskId, flowKey, false, e.Message));
            }
        }
        return new BatchTransferReport(candidates.Count, succeeded, failed);
    }

    /// <inheritdoc/>
    public async Task<BatchTransferPreview> BatchTransferPreviewAsync(Guid fromUserId, BatchTransferFilter? filter = null)
    {
        var candidates = await QueryTransferCandidatesAsync(fromUserId, filter);
        var candidateIds = candidates.Select(c => c.TaskId).Take(10).ToHashSet();
        var all = await PendingAsync(fromUserId, rowMode: "expanded");             // 复用列表读模型拿展示字段
        var sample = all.Where(p => candidateIds.Contains(p.TaskId)).ToList();
        return new BatchTransferPreview(candidates.Count, sample);
    }
```

> 注：`PendingAsync(userId, rowMode: "expanded")` 依赖 D-T1 的签名扩展。**若 X-B 先于 X-D 执行**：本任务先以 `PendingAsync(fromUserId)` 现签名调用（现状即逐任务行 = expanded 语义，R5），D-T1 改签名时默认参不破本调用——两种顺序都编译。执行者按当时签名取其一，语义相同。

- [ ] **Step 4: 跑测试验证 PASS** — `dotnet test CP6.Tests/CP6.Tests.csproj --filter BatchTransferTests`。

- [ ] **Step 5: 回归闸 + commit**

```bash
dotnet test CP6.Tests/CP6.Tests.csproj --filter "Oa|Wf"    # ActBatch/Transfer 既有语义零回归
git add -A && git commit -m "feat(wfs-inbox): B-T1 BatchTransferAsync 逐条独立事务+汇总+上限500+前置校验+preview"
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

## 附: R3转交引擎与批量口径
### R3 转交引擎与批量口径

- `IFlowEngine.TransferAsync`（`CP6.Core/Services/Wf/IFlowEngine.cs:54`，实现 `AdvancedFlow.cs:78-98`）：`Task TransferAsync(Guid taskId, Guid actorId, Guid toUserId, string? comment = null)`。校验 task Pending / 实例 Running / to 存在（**租户全局过滤器保证同租户：跨租户查不到 = E-WF-002**）且 ≠ 当前 assignee；改 `task.AssigneeId`、FormTo 双行（原行→Transferred + 新 Pending 行，from/to 审计在此）、`AddHistory(instId, nodeId, actorId, "transfer", comment)`、`TodoCreatedAsync` 通知新受让人、**末尾单次 `SaveChangesAsync` = 事务边界**。失败一律抛 `InvalidOperationException("E-WF-002")`。→ 批量逐条调它 = 逐条独立事务，天然满足 D3。
- 批量循环精确先例：`InboxService.ActBatchAsAsync`（`InboxService.cs:199-217`）——`foreach taskIds.Distinct()` + 前置校验 + try/catch `InvalidOperationException` 收集明细。
- 审计免费：`OperLogFilter`（全局，POST 自动记操作者/请求体/租户）+ `Wf_FlowHistory`（ActorId+action=transfer）+ `Wf_FlowFormTo`（from/to 对）。**无需新审计代码**。
- `TransferAsync` **不校验 to.Enable**（只查存在）→ spec「to 停用 → 400」由批量服务层前置校验补。
