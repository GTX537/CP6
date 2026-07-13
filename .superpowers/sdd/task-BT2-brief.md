### Task B-T2: 剪枝路径（触发分流 + PruneToken + join 补放行；cascade 零 diff）

> 依赖 A-T1/A-T2/A-T4 + B-T1。

**Files:**
- Create: `CP6.Core/Services/Wf/FlowEngine.Prune.cs`
- Modify: `CP6.Core/Services/Wf/FlowEngine.cs`（`ActOnceAsync` :221-226 else 分支分流）
- Modify: `CP6.Core/Services/Wf/FlowEngine.Tokens.cs`（加 `CancelPendingTasksOfToken`）
- Test: `CP6.Tests/Wf/BranchPruneTests.cs`

- [ ] **Step 1: 写失败测试**

```csharp
// CP6.Tests/Wf/BranchPruneTests.cs
using System.Text.Json;
using CP6.Core.EFDbContext;
using CP6.Core.Services.Wf;
using CP6.Entity.DomainModels.Wf;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace CP6.Tests.Wf;

/// <summary>驳回剪枝矩阵（hardening spec §4/§9）：单支剪/兄弟继续/剪后 join 补放行/全剪光→Rejected/
/// cascade 默认零 diff/FormTo 履历状态。通知计数用 CountingPruneNotifier（仿 NotificationEngineHookTests）。</summary>
public class BranchPruneTests
{
    private sealed class CountingPruneNotifier : IWfNotifier
    {
        public int PrunedCount { get; private set; }
        public int RejectedCount { get; private set; }
        public Task TodoCreatedAsync(Guid assigneeId, Guid instanceId, Guid taskId, string flowKey) => Task.CompletedTask;
        public Task FlowApprovedAsync(Guid starterId, Guid instanceId, string flowKey) => Task.CompletedTask;
        public Task FlowRejectedAsync(Guid starterId, Guid instanceId, string flowKey, string? comment)
        { RejectedCount++; return Task.CompletedTask; }
        public Task BranchPrunedAsync(Guid starterId, Guid instanceId, string flowKey, string nodeId, string? comment)
        { PrunedCount++; return Task.CompletedTask; }
    }

    private static CP6Context NewDb() => new(new DbContextOptionsBuilder<CP6Context>()
        .UseInMemoryDatabase(Guid.NewGuid().ToString())
        .ConfigureWarnings(w => w.Ignore(InMemoryEventId.TransactionIgnoredWarning)).Options);
    private static FlowEngine Engine(CP6Context db, IWfNotifier? n = null) => new(db, new ApproverResolver(db), n);

    // start → split[onBranchReject 可配] → (a, b) → join → end
    private static FlowSchema ForkSchema(Guid ua, Guid ub, string? onBranchReject) => new()
    {
        Start = "s",
        Nodes =
        {
            new FlowNode { Id = "s", Type = "start" },
            new FlowNode { Id = "split", Type = "parallelSplit", OnBranchReject = onBranchReject },
            new FlowNode { Id = "a", Type = "approval", ApproverStrategy = "Specified", ApproverUserId = ua },
            new FlowNode { Id = "b", Type = "approval", ApproverStrategy = "Specified", ApproverUserId = ub },
            new FlowNode { Id = "join", Type = "parallelJoin" },
            new FlowNode { Id = "end", Type = "end" },
        },
        Edges =
        {
            new FlowEdge { From = "s", To = "split" },
            new FlowEdge { From = "split", To = "a" }, new FlowEdge { From = "split", To = "b" },
            new FlowEdge { From = "a", To = "join" }, new FlowEdge { From = "b", To = "join" },
            new FlowEdge { From = "join", To = "end" },
        },
    };

    private static async Task SeedAsync(CP6Context db, Guid ua, Guid ub, string? obr)
    {
        db.Wf_FlowDefs.Add(new Wf_FlowDef { Id = Guid.NewGuid(), FlowKey = "pr", FlowName = "pr", FormKey = "f",
            SchemaJson = JsonSerializer.Serialize(ForkSchema(ua, ub, obr)), Version = 1, Enable = true });
        await db.SaveChangesAsync();
    }

    [Fact]
    public async Task Prune_SingleBranch_SiblingContinues_ThenApproves()
    {
        using var db = NewDb();
        var notifier = new CountingPruneNotifier();
        var ua = Guid.NewGuid(); var ub = Guid.NewGuid();
        await SeedAsync(db, ua, ub, "prune");
        await Engine(db, notifier).SubmitAsync("pr", Guid.NewGuid(), "{}");

        // a 驳回 → 只剪 a 支
        var ta = await db.Wf_FlowTasks.SingleAsync(t => t.AssigneeId == ua && t.Status == FlowTaskStatus.Pending);
        await Engine(db, notifier).ActAsync(ta.Id, ua, approve: false, "a 部门否");

        var inst = await db.Wf_FlowInstances.SingleAsync();
        Assert.Equal(FlowInstanceStatus.Running, inst.Status);                       // ★ 不连坐
        Assert.True(await db.Wf_FlowTokens.AnyAsync(t => t.NodeId == "a" && t.Status == FlowTokenStatus.Pruned));
        Assert.True(await db.Wf_FlowTasks.AnyAsync(t => t.AssigneeId == ub && t.Status == FlowTaskStatus.Pending)); // 兄弟不倒
        Assert.Equal(1, await db.Wf_FlowHistories.CountAsync(h => h.Action == "branchPruned"));
        Assert.Equal(1, notifier.PrunedCount);
        Assert.Equal(0, notifier.RejectedCount);
        // a 支 Pending 履历 → Voided；b 支不受扰
        Assert.False(await db.Wf_FlowFormTos.AnyAsync(f => f.NodeId == "a" && f.Status == FlowFormToStatus.Pending));
        Assert.True(await db.Wf_FlowFormTos.AnyAsync(f => f.NodeId == "b" && f.Status == FlowFormToStatus.Pending));

        // b 过 → join 动态计票（Pruned 从等待集消失）→ Approved
        var tb = await db.Wf_FlowTasks.SingleAsync(t => t.AssigneeId == ub && t.Status == FlowTaskStatus.Pending);
        await Engine(db, notifier).ActAsync(tb.Id, ub, approve: true);
        Assert.Equal(FlowInstanceStatus.Approved, (await db.Wf_FlowInstances.SingleAsync()).Status);
    }

    [Fact]
    public async Task Prune_JoinBackfill_ParkedSiblingReleases_NoFalseCollapse()
    {
        using var db = NewDb();
        var notifier = new CountingPruneNotifier();
        var ua = Guid.NewGuid(); var ub = Guid.NewGuid();
        await SeedAsync(db, ua, ub, "prune");
        await Engine(db, notifier).SubmitAsync("pr", Guid.NewGuid(), "{}");

        // 先 b 过（b 到场 join 停泊），再驳 a → 剪枝使 join 凑齐 → 补放行 → Approved（且不得误判全剪光）
        var tb = await db.Wf_FlowTasks.SingleAsync(t => t.AssigneeId == ub && t.Status == FlowTaskStatus.Pending);
        await Engine(db, notifier).ActAsync(tb.Id, ub, approve: true);
        Assert.Equal(FlowInstanceStatus.Running, (await db.Wf_FlowInstances.SingleAsync()).Status);

        var ta = await db.Wf_FlowTasks.SingleAsync(t => t.AssigneeId == ua && t.Status == FlowTaskStatus.Pending);
        await Engine(db, notifier).ActAsync(ta.Id, ua, approve: false, "否");

        var inst = await db.Wf_FlowInstances.SingleAsync();
        Assert.Equal(FlowInstanceStatus.Approved, inst.Status);                      // ★ 补放行成功
        Assert.Equal(1, notifier.PrunedCount);
        Assert.Equal(0, notifier.RejectedCount);                                     // ★ 未误判剪光递归驳回
        Assert.False(await db.Wf_FlowTokens.AnyAsync(t => t.Status == FlowTokenStatus.Active));
    }

    [Fact]
    public async Task Prune_AllBranches_CollapsesToInstanceRejected()
    {
        using var db = NewDb();
        var notifier = new CountingPruneNotifier();
        var ua = Guid.NewGuid(); var ub = Guid.NewGuid();
        await SeedAsync(db, ua, ub, "prune");
        await Engine(db, notifier).SubmitAsync("pr", Guid.NewGuid(), "{}");

        var ta = await db.Wf_FlowTasks.SingleAsync(t => t.AssigneeId == ua && t.Status == FlowTaskStatus.Pending);
        await Engine(db, notifier).ActAsync(ta.Id, ua, approve: false);
        Assert.Equal(FlowInstanceStatus.Running, (await db.Wf_FlowInstances.SingleAsync()).Status);

        var tb = await db.Wf_FlowTasks.SingleAsync(t => t.AssigneeId == ub && t.Status == FlowTaskStatus.Pending);
        await Engine(db, notifier).ActAsync(tb.Id, ub, approve: false);              // 最后一支也剪 → 全剪光

        var inst = await db.Wf_FlowInstances.SingleAsync();
        Assert.Equal(FlowInstanceStatus.Rejected, inst.Status);                      // ★ 上弹到顶（无外层）→ Rejected
        Assert.Equal(2, notifier.PrunedCount);
        Assert.Equal(1, notifier.RejectedCount);                                     // 终态分发照走
        Assert.False(await db.Wf_FlowTokens.AnyAsync(t => t.Status == FlowTokenStatus.Active));
        Assert.False(await db.Wf_FlowFormTos.AnyAsync(f => f.Status == FlowFormToStatus.Pending));
    }

    [Theory]
    [InlineData(null)]          // 未配置（现状）
    [InlineData("cascade")]     // 显式 cascade
    public async Task Cascade_Default_ZeroDiff_RejectTerminatesWholeInstance(string? obr)
    {
        using var db = NewDb();
        var notifier = new CountingPruneNotifier();
        var ua = Guid.NewGuid(); var ub = Guid.NewGuid();
        await SeedAsync(db, ua, ub, obr);
        await Engine(db, notifier).SubmitAsync("pr", Guid.NewGuid(), "{}");

        var ta = await db.Wf_FlowTasks.SingleAsync(t => t.AssigneeId == ua && t.Status == FlowTaskStatus.Pending);
        await Engine(db, notifier).ActAsync(ta.Id, ua, approve: false, "no");

        // 与 ParallelGatewayTests.Parallel_RejectTerminates 逐字等价的终态
        var inst = await db.Wf_FlowInstances.SingleAsync();
        Assert.Equal(FlowInstanceStatus.Rejected, inst.Status);
        Assert.Equal(0, await db.Wf_FlowTokens.CountAsync(t => t.Status == FlowTokenStatus.Active));
        Assert.Equal(0, await db.Wf_FlowTokens.CountAsync(t => t.Status == FlowTokenStatus.Pruned));  // ★ cascade 不产生 Pruned
        Assert.Equal(0, notifier.PrunedCount);
        Assert.Equal(0, await db.Wf_FlowHistories.CountAsync(h => h.Action == "branchPruned"));
    }

    [Fact]
    public async Task Prune_LinearFlow_NoFork_FallsBackToCascade()
    {
        // 线性流（token ForkId==null）上即便某节点被驳回，也走既有连坐路径（prune 只对分支 token 有意义）
        using var db = NewDb();
        var ua = Guid.NewGuid();
        var schema = new FlowSchema
        {
            Start = "s",
            Nodes =
            {
                new FlowNode { Id = "s", Type = "start" },
                new FlowNode { Id = "a", Type = "approval", ApproverStrategy = "Specified", ApproverUserId = ua },
                new FlowNode { Id = "end", Type = "end" },
            },
            Edges = { new FlowEdge { From = "s", To = "a" }, new FlowEdge { From = "a", To = "end" } },
        };
        db.Wf_FlowDefs.Add(new Wf_FlowDef { Id = Guid.NewGuid(), FlowKey = "lin", FlowName = "lin", FormKey = "f",
            SchemaJson = JsonSerializer.Serialize(schema), Version = 1, Enable = true });
        await db.SaveChangesAsync();
        await Engine(db).SubmitAsync("lin", Guid.NewGuid(), "{}");

        var ta = await db.Wf_FlowTasks.SingleAsync(t => t.Status == FlowTaskStatus.Pending);
        await Engine(db).ActAsync(ta.Id, ua, approve: false);
        Assert.Equal(FlowInstanceStatus.Rejected, (await db.Wf_FlowInstances.SingleAsync()).Status);
    }
}
```

- [ ] **Step 2: 跑验证 FAIL** — `--filter BranchPruneTests`：prune 场景失败（现状连坐把实例打成 Rejected）；cascade/线性两测应在旧实现下已绿（等价基准）。

- [ ] **Step 3: 实现**

`FlowEngine.Tokens.cs` 加：

```csharp
    /// <summary>本 token 的在途/挂起任务 → Cancelled（剪枝/子树清场用）。Local + localIds-exclusion 惯用法。</summary>
    internal void CancelPendingTasksOfToken(Guid instanceId, Guid tokenId)
    {
        foreach (var t in _db.Wf_FlowTasks.Local.Where(t => t.InstanceId == instanceId && t.TokenId == tokenId
            && (t.Status == FlowTaskStatus.Pending || t.Status == FlowTaskStatus.Suspended)).ToList())
            t.Status = FlowTaskStatus.Cancelled;
        var localIds = _db.Wf_FlowTasks.Local.Where(t => t.InstanceId == instanceId).Select(t => t.Id).ToHashSet();
        foreach (var t in _db.Wf_FlowTasks.Where(t => t.InstanceId == instanceId && t.TokenId == tokenId
            && (t.Status == FlowTaskStatus.Pending || t.Status == FlowTaskStatus.Suspended)
            && !localIds.Contains(t.Id)).ToList())
            t.Status = FlowTaskStatus.Cancelled;
    }
```

`FlowEngine.Prune.cs` 全文：

```csharp
using CP6.Entity.DomainModels.Wf;

namespace CP6.Core.Services.Wf;

/// <summary>驳回剪枝（hardening spec §4）。partial：与 FlowEngine 共享 scoped DbContext 与内部方法。
/// 铁律：剪枝绝不改 inst.Status（除全剪光递归到顶走既有 Rejected 路径）；不自行 SaveChanges
/// （随 ActOnceAsync 尾部统一落库）；终态分发接缝（DispatchIfFinished 在 SaveChanges 前）保持不动。</summary>
public partial class FlowEngine
{
    /// <summary>剪枝入口（ActOnceAsync 驳回分支调用）。true=已按 prune 处理；false=按 cascade（调用方走既有连坐）。
    /// 仅当 token 有 fork 血缘、且其本层 split 配置 onBranchReject=="prune" 时才剪。</summary>
    internal async Task<bool> TryPruneBranchAsync(Wf_FlowInstance inst, FlowSchema schema, Wf_FlowToken token,
        Guid actorId, string? comment)
    {
        var all = SnapshotTokens(inst.Id);
        var split = FindSplitNode(schema, all, token);
        if (split is null || !IsPrune(split)) return false;
        await PruneTokenAsync(inst, schema, token, actorId, comment);
        return true;
    }

    /// <summary>定位生成 token.ForkId 批次的 split 节点：ForkParent(token).NodeId（§4.1 定案：ParentTokenId 上溯，零迁移）。</summary>
    internal static FlowNode? FindSplitNode(FlowSchema schema, IReadOnlyList<Wf_FlowToken> all, Wf_FlowToken token)
    {
        if (token.ForkId is null) return null;
        var parent = TokenLineage.ForkParent(all, token);
        return parent is null ? null : FindNode(schema, parent.NodeId);
    }

    internal static bool IsPrune(FlowNode split)
        => string.Equals(split.OnBranchReject?.Trim(), "prune", StringComparison.OrdinalIgnoreCase);

    private static bool IsJoinType(FlowNode n) => FlowGraph.IsJoinType(n);

    /// <summary>剪本支：token → Pruned；本支任务 Cancelled、Pending FormTo → Voided（tokenId 过滤复用）；
    /// 记 branchPruned 履历 + BranchPruned 通知；再做 join 补放行探测与全剪光递归上弹。</summary>
    private async Task PruneTokenAsync(Wf_FlowInstance inst, FlowSchema schema, Wf_FlowToken token,
        Guid actorId, string? comment)
    {
        token.Status = FlowTokenStatus.Pruned;
        CancelPendingTasksOfToken(inst.Id, token.Id);
        VoidPendingFormTos(inst.Id, tokenId: token.Id);
        AddHistory(inst.Id, token.NodeId, actorId, "branchPruned", comment);
        await _notifier.BranchPrunedAsync(inst.StarterId, inst.Id, inst.FlowKey, token.NodeId, comment);
        await ReleaseOrCollapseAsync(inst, schema, token, actorId, comment);
    }

    /// <summary>剪枝后的 fork 批次收束（spec §4.2.3/§4.2.4，顺序敏感）：
    /// ① join 补放行：同 ForkId 停在 join 型节点的 Active token 重入 OnEnterAsync（计数幂等，重入安全）；
    ///    检测到任一停泊 token 变为 Consumed（即 join 已齐批放行）→ 立即返回——续 token 已上弹属上层批次，
    ///    此时「无 Active 穿过本批次」是正常收束而非剪光，若继续判剪光会误递归驳回（计划期新发现护栏）。
    /// ② 全剪光检测（血缘感知，与 §3.3 同款判据）：不存在「穿过本 fork 批次」的在途 Active token →
    ///    视同该 fork 的续 token 被驳回，递归应用上一层 fork 的 OnBranchReject：
    ///    外层 prune → 剪外层该支（记痕+通知+递归收束）；外层 cascade / 无外层 → 实例 Rejected 走既有终态路径。</summary>
    private async Task ReleaseOrCollapseAsync(Wf_FlowInstance inst, FlowSchema schema, Wf_FlowToken deadBranchToken,
        Guid actorId, string? comment)
    {
        var forkId = deadBranchToken.ForkId!.Value;

        // ① join 补放行探测
        var all = SnapshotTokens(inst.Id);
        bool released = false;
        var parkedByNode = all.Where(t => t.ForkId == forkId && t.Status == FlowTokenStatus.Active)
            .GroupBy(t => t.NodeId).ToList();
        foreach (var g in parkedByNode)
        {
            var node = FindNode(schema, g.Key);
            if (node is null || !IsJoinType(node)) continue;
            var probe = g.First();
            await EnterNodeAsync(inst, schema, node, probe);
            if (probe.Status == FlowTokenStatus.Consumed) released = true;   // join 齐批放行了
        }
        if (released) return;   // 正常收束，绝不判剪光

        // ② 全剪光递归上弹（血缘感知：内层子树在途也算活支）
        all = SnapshotTokens(inst.Id);
        if (all.Any(t => t.Status == FlowTokenStatus.Active && TokenLineage.CrossesFork(all, t, forkId)))
            return;   // 还有活支 → 本批次继续等

        var forkParent = TokenLineage.ForkParent(all, deadBranchToken);
        var outerSplit = forkParent is null || forkParent.ForkId is null
            ? null : FindSplitNode(schema, all, forkParent);
        if (forkParent is not null && outerSplit is not null && IsPrune(outerSplit))
        {
            // 外层 prune：视同 forkParent（外层该支代表）被驳回 → 剪外层该支并继续递归收束。
            // forkParent 已 Consumed（进 split 时退场），无需改状态；其在途后代刚被判定为零。
            AddHistory(inst.Id, forkParent.NodeId, actorId, "branchPruned", comment);
            await _notifier.BranchPrunedAsync(inst.StarterId, inst.Id, inst.FlowKey, forkParent.NodeId, comment);
            await ReleaseOrCollapseAsync(inst, schema, forkParent, actorId, comment);
        }
        else
        {
            // 外层 cascade / 无外层 → 实例 Rejected（既有连坐终态；DispatchIfFinished 由 ActOnceAsync 尾部统一做）
            inst.Status = FlowInstanceStatus.Rejected;
            CancelAllActiveTokens(inst.Id);
            VoidPendingFormTos(inst.Id);
        }
    }
}
```

`FlowEngine.cs` `ActOnceAsync` 驳回 else 分支（:221-226）改为——**cascade 路径三行原文保留，只包一层分流**：

```csharp
        else
        {
            // hardening B-T2 驳回分流（spec §4.1）：token 有 fork 血缘且本层 split 配 prune → 剪枝；否则既有连坐一行不改。
            // ForkId==null 时不 LoadSchema（cascade 默认路径零额外开销）。
            var rejTok = await _db.Wf_FlowTokens.FirstOrDefaultAsync(t => t.Id == task.TokenId);
            var pruned = false;
            if (rejTok is not null && rejTok.ForkId is not null)
            {
                var pruneSchema = await LoadSchemaAsync(inst.FlowKey);
                pruned = await TryPruneBranchAsync(inst, pruneSchema, rejTok, actorId, comment);
            }
            if (!pruned)
            {
                inst.Status = FlowInstanceStatus.Rejected;
                CancelAllActiveTokens(inst.Id);   // ★ 驳回 = terminate，兄弟分支连坐
                VoidPendingFormTos(inst.Id);      // ★ T9：驳回连坐，全 Pending 传签履历行 → 作废
            }
        }
```

- [ ] **Step 4: 跑验证 PASS** — `--filter BranchPruneTests` 全绿。
- [ ] **Step 5: Wf 闸（重点盯 ParallelGatewayTests.Parallel_RejectTerminates 等连坐不变量照绿）+ commit**

```bash
dotnet test CP6.Tests/CP6.Tests.csproj --filter Wf
git add -A && git commit -m "feat(wfs-hardening): B-T2 驳回剪枝路径(触发分流+PruneToken+join补放行护栏,cascade零diff)"
```

---

## 落码纪律 / Global Constraints（每个 Task 都遵守）

- **基线锁定**：后端 `dotnet test CP6.Tests/CP6.Tests.csproj` = **1509 通过（5 skip=SQLite 既知）** → 本波只增不减、全绿；前端 `npm run test`（vitest）= **320 全绿** → +N 全绿；`npm run type-check`（package.json 既有命令，含大堆内存参数）+ `npm run build` 全过。
- **EF clean（本波零迁移）**：只改 SchemaJson POCO（`FlowNode.OnBranchReject`）+ 常量（`FlowTokenStatus.Pruned`），**不加实体列、不生成迁移**。每波末跑 `dotnet ef migrations has-pending-model-changes --project CP6.Core/CP6.Core.csproj --startup-project CP6.WebApi/CP6.WebApi.csproj --context CP6Context` 必须 clean。
- **27 个既有 Wf 不变量测试一个断言不许改**（`CP6.Tests/Wf/**` 既有文件只增不改；`ParallelGatewayTests` 4+1 个并行语义测试是动态计票的等价性铁闸）。唯一例外见 D-T1：前端 `designerModel.test.ts:45` 的 palette 类型清单断言随 palette 扩展同步更新（前端测试不在 27 不变量范围，且该断言就是「palette 全集清单」本身）。
- **默认路径行为与现状全等**：无 inclusive 节点 + 无 onBranchReject 配置的流程，token 状态序列 / 任务 / FormTo / 通知 bit 级等价（cascade 一行不改、动态计票旧场景回归锁定、线性退回走既有全清场分支逐字保留）。
- **引擎内写路径三律（黄金模板铁律）**：① 先校验后写（一切结构化拒绝在任何状态突变之前抛出）；② 幂等（join 计数重入安全、已办任务再办 no-op）；③ handler/引擎内部方法**绝不自行 SaveChanges**（统一由 ActAsync/SendBackAsync 等外壳收口，剪枝/退回全部改动随既有 SaveChanges 落库）。
- **E 波紧跟 D 波不留窗口**：D-T2 合入后立即执行 E-T1/E-T2，不允许「有 UI 无 i18n/无 QA」的中间态过夜。
- **零跨模块污染**：不碰 `cp6.web/src/views/space/**`、`Services/*Space*`、Space 迁移/DbSet。每 Task 完成 `git show --stat` 复核。
- **零硬编码色**：前端新增视觉全部走 Design System token（`var(--cp-warn)` 家族等），沿 `DesignerCanvas.vue` `.dot-*` / `GatewayNode.vue` 既有 token 用法。
- **五语 i18n**：ja / zh-CN / zh-TW / en / ko，新 UI 文案全 `t()` 运行时键，键值入 `I18nOa*ScreenSeed` 家族新 seed。
- **隔离 worktree**：建议 `git worktree add C:/CP6-wfs-hardening -b feat/wfs-kernel-hardening main`（off `fb90d75`），不污染 `C:\CP6` 工作区。
- **subagent-driven TDD**：每 Task 全新编码子代理（模型按 model-policy：Opus 4.8）→ 主代理 `git show` diff 复核 → 本地 commit **不 push**。节奏：先写失败测试 → 跑验证 FAIL → 最小实现 → 跑验证 PASS → commit。提交信息 `feat(wfs-hardening): <Task 号> 中文摘要`。

---

## 侦察结论（spec §5.3 各核实项，已实读代码定案 —— 执行者照此实现，不再二次侦察）

| 核实项 | 结论 |
|---|---|
| §4.1 **split 节点定位机制** | `Wf_FlowToken`（`CP6.Entity/DomainModels/Wf/Wf_FlowToken.cs`）**没有** split nodeId 列，只有 `ParentTokenId`/`ForkId`/`StagePlanJson`。选定 **ParentTokenId 上溯**：分叉时 `ParallelSplitNodeHandler.cs:21` 以 `parent: ctx.Token.Id` 生子 token，而 ctx.Token 在被消费前 NodeId 已被 `AdvanceToken`（`FlowEngine.Tokens.cs:102`）置为 split 节点 id、`ConsumeToken` 不改 NodeId ⇒ **`ForkParent(t)`（Id==t.ParentTokenId 的 token）.NodeId 恒等于生成 t.ForkId 批次的 split 节点 id**。join 续 token 的「上弹一层」血缘（`ParallelJoinNodeHandler.cs:25-27`）保证该不变量对每层 fork 成立。零迁移。 |
| §5.3 **SendBackToNodeAsync 重生血缘现状** | `AdvancedFlow.cs:141` — `SpawnToken(inst, target, parent: null, fork: null)`，**血缘归零**（根 token）。SameBranch 规则必须改为携带剥离层血缘 `(parent: strip.ParentTokenId, fork: strip.ForkId)`；BeforeSplit/线性流保留归零重生（现状逐字）。 |
| §5.3 **CancelAllActiveTokens 过滤重载** | `FlowEngine.Tokens.cs:40` 现为全实例清场（含 B-T3 的 Pending Wf_ServiceJob 清场）、无过滤参数。不改它；**新增 `CancelTokenSubtree(Guid instanceId, Guid rootTokenId)`**（C-T2）：按 ParentTokenId 闭包算子树，子树内 Active token→Cancelled、Pending/Suspended 任务→Cancelled、Pending FormTo→Voided、Pending ServiceJob→Cancelled（镜像既有 Local ∪ DB localIds-exclusion 惯用法）。**子树闭包正确性论证**：join 续 token 血缘「上弹一层」（parent=祖父），故任何仍在途的分支延续 token 会重新挂在剥离层同级 —— 作用域分析（C-T1）从 current token 血缘出发选剥离层时选到的正是该延续 token 本身，ParentTokenId 后代闭包捕获全部需清场的活 token（C-T3 嵌套测试锁定）。 |
| §5.3 **StageRound 递增与局部退回相容性** | `NextStageRound`（`FlowEngine.ReadModel.cs:53`）按 `(instanceId, nodeId, tokenId, stageIndex)` 键控取 Max+1。SameBranch 重生的是**新 tokenId** ⇒ 新 token 串簽轮次从 0 起，与现状全清场重生同构，**天然相容，零改**。prevStage 退回不换 token（`AdvancedFlow.cs:161-163`），轮次 +1 语义不受本波影响。 |
| §5.3 **剥离层判定与 fork 栈共用血缘辅助** | 新 `TokenLineage` 静态类（A-T1）：`AncestorChain` / `CrossesFork` / `ForkParent` / `ForkStack`。剪枝递归上弹（B-T2/B-T3）与退回剥离层解析（C-T1）共用，单一口径。token 快照沿用 `ParallelJoinNodeHandler.AllTokens` 的 Local ∪ DB 身份映射去重口径，抽为 `FlowEngine.SnapshotTokens`（A-T1）。 |
| **计票退化护栏（计划期新发现）** | 旧静态计票在「join 被 ForkId==null 的线性 token 进入」的怪异 schema 下按入边数计（等不齐永停）；朴素动态判据会立即放行 ⇒ 行为漂移。定案：`GatewayJoinHelper` 对 **ForkId==null 保留旧静态入边计票路径**，bit 级等价（A-T2 专测锁定）。 |
| **剪后补放行 ≠ 全剪光（计划期新发现）** | 剪枝后 join 若齐批放行，同批 token 全部 Consumed、续 token 属上层批次 ⇒ 「无 Active 穿过本批次」也成立，若不加判别会误判全剪光递归驳回。定案：补放行探测中**检测到任一停泊 token 重入后变为 Consumed（即 join 已放行）则立即返回**，不再走全剪光检查（B-T2 `Prune_JoinBackfill_*` 锁定）。 |

**发现的 spec 与代码现状出入（不改 spec，按下述口径落码）**：
1. spec §5.2 称 BeforeSplit 为「现行为」——实际现状是 `CrossesParallelBlock`（`AdvancedFlow.cs:196`）对一切跨网关退回直接拒绝 E-WF-012，并非允许后全清场。本波 BeforeSplit = **放开该禁令后套用既有全清场机制**（行为上是新放开的能力，机制逐字复用现状代码块）。既有测试无「跨并行块拒绝」断言（已 grep 核实），无不变量冲突。
2. spec §4.2.2 通知联动信箱 spec 偏好矩阵——`PersistentWfNotifier` 现按 `NotificationPrefs` 强类型字段开关，无 BranchPruned 键。本波 `BranchPrunedAsync` **不查偏好开关**（等价信箱 spec「缺键默认 true」三态坍缩），偏好矩阵接管由信箱 spec 落地时统一改造。
3. spec §1 的 FlowEngine 行号锚点有 ±10 行漂移（办理实际在 `ActOnceAsync :136`、会签计票 `:169-177`、驳回连坐 `:221-226`），语义描述全部核实无误。

---

## File Structure（创建/修改清单，每文件一职责）

**后端 `CP6.Core/Services/Wf`**
- Modify `WfStatus.cs` — `FlowTokenStatus` 加 `Pruned = 3`。
- Modify `FlowSchema.cs` — `FlowNode` 加 `public string? OnBranchReject { get; set; }`。
- Create `TokenLineage.cs` — 血缘辅助纯函数（AncestorChain/CrossesFork/ForkParent/ForkStack）。
- Create `FlowGraph.cs` — schema 图论辅助（ReachableFrom/BfsDepths/NearestCommonJoin/BranchDomain）。
- Modify `FlowEngine.Tokens.cs` — 新增 `SnapshotTokens` / `CancelTokenSubtree` / `CancelPendingTasksOfToken` / `CancelPendingServiceJobsOfToken`。
- Create `NodeHandlers/GatewayJoinHelper.cs` — 双 join 共享动态计票放行（D4）。
- Modify `NodeHandlers/ParallelJoinNodeHandler.cs` — 委托 GatewayJoinHelper（删私有 AllTokens/静态计票）。
- Create `NodeHandlers/InclusiveSplitNodeHandler.cs` — 第 7 个 handler。
- Create `NodeHandlers/InclusiveJoinNodeHandler.cs` — 第 8 个 handler。
- Modify `FlowEngine.cs` — `DefaultHandlers()` 加 2 handler；`ActOnceAsync` 驳回分支加 prune 分流（cascade 一行不改）。
- Create `FlowEngine.Prune.cs` — partial：`TryPruneBranchAsync`/`PruneTokenAsync`/`ReleaseOrCollapseAsync`/`FindSplitNode`/`IsPrune`/`IsJoinType`。
- Create `SendBackScopeAnalyzer.cs` — `SendBackScope` 枚举 + `Analyze` 纯函数。
- Modify `AdvancedFlow.cs` — `SendBackToNodeAsync` 三规则接线（删 `CrossesParallelBlock`/`NodesBetween`）。
- Modify `FlowSchemaValidator.cs` — E-WF-020 / E-WF-021 规则。
- Modify `IWfNotifier.cs` — 加 `BranchPrunedAsync` + `NullWfNotifier` 实现。

**后端其他**
- Modify `CP6.Entity/DomainModels/Wf/WfNotificationType.cs` — 加 `BranchPruned = 5`。
- Modify `CP6.WebApi/Services/PersistentWfNotifier.cs` — `BranchPrunedAsync` 三渠道实现。
- Modify `CP6.WebApi/Services/SignalRWfNotifier.cs` — `BranchPrunedAsync` no-op（接口补全）。
- Modify `CP6.WebApi/Program.cs` — DI 注册 2 个新 INodeHandler（:113 后）；i18n concat（:1819 链尾）。
- Create `CP6.WebApi/Seed/I18nOaKernelHardeningScreenSeed.cs` — 五语 12 键。

**前端 `cp6.web/src`**
- Modify `views/oa/designer/designerModel.ts` — `SchemaNode.onBranchReject` + palette 2 入口 + validateClient E-WF-020/021 镜像。
- Modify `views/oa/designer/designerModel.test.ts` — :45 palette 类型清单断言补 2 类型。
- Create `views/oa/designer/designerModel.hardening.test.ts` — 新 vitest。
- Create `views/oa/designer/nodes/InclusiveGatewayNode.vue` — 空心圆菱形节点（BPMN 惯例）。
- Modify `views/oa/designer/DesignerCanvas.vue` — 注册 2 节点模板 + palette 空心 dot 样式。
- Modify `views/oa/designer/NodePropertyPanel.vue` — 「分支驳回策略」段（parallelSplit/inclusiveSplit）。
- Modify `types/oa/notification.ts` — `NotificationType.BranchPruned = 5` 镜像。

**测试 / QA**
- Create `CP6.Tests/Wf/TokenLineageTests.cs` / `DynamicJoinCountTests.cs` / `InclusiveGatewayTests.cs` / `InclusiveValidatorTests.cs` / `BranchPruneTests.cs` / `BranchPruneNestedTests.cs` / `SendBackScopeTests.cs` / `TokenSubtreeCancelTests.cs` / `SendBackThreeRuleTests.cs`。
- Modify `CP6.Tests/Oa/NotificationEngineHookTests.cs` — `CountingNotifier` 补 `BranchPrunedAsync` 计数。
- Create `docs/superpowers/qa/wfs-kernel-hardening/{README.md,seed.sql,qa_kernel_hardening.ps1}` — gstack harness。

---

## 共享契约（所有 Task 用这些**精确**名字与签名，前后一致，不许漂移）

```csharp
// WfStatus.cs
public const int Pruned = 3;                                  // FlowTokenStatus 新常量

// FlowSchema.cs / FlowNode
public string? OnBranchReject { get; set; }                   // null/"cascade"=连坐(现状)；"prune"=剪枝

// TokenLineage.cs（internal static class TokenLineage）
public static IEnumerable<Wf_FlowToken> AncestorChain(IReadOnlyList<Wf_FlowToken> all, Wf_FlowToken t);
public static bool CrossesFork(IReadOnlyList<Wf_FlowToken> all, Wf_FlowToken t, Guid forkId);
public static Wf_FlowToken? ForkParent(IReadOnlyList<Wf_FlowToken> all, Wf_FlowToken t);
public static List<(Wf_FlowToken BranchToken, Guid ForkId, string SplitNodeId)> ForkStack(
    IReadOnlyList<Wf_FlowToken> all, Wf_FlowToken t);          // 内→外

// FlowGraph.cs（internal static class FlowGraph）
public static FlowNode? NearestCommonJoin(FlowSchema schema, FlowNode split);
public static HashSet<string> ReachableFrom(FlowSchema schema, string startId);
public static HashSet<string> BranchDomain(FlowSchema schema, string edgeTargetId, string pairedJoinId);

// FlowEngine.Tokens.cs（partial FlowEngine）
internal IReadOnlyList<Wf_FlowToken> SnapshotTokens(Guid instanceId);          // Local ∪ DB 去重快照
internal void CancelTokenSubtree(Guid instanceId, Guid rootTokenId);           // 剥离层子树清场

// NodeHandlers/GatewayJoinHelper.cs
internal static class GatewayJoinHelper { public static Task TryReleaseAsync(NodeContext ctx, string historyAction); }

// FlowEngine.Prune.cs（partial FlowEngine）
internal Task<bool> TryPruneBranchAsync(Wf_FlowInstance inst, FlowSchema schema, Wf_FlowToken token, Guid actorId, string? comment);
internal static FlowNode? FindSplitNode(FlowSchema schema, IReadOnlyList<Wf_FlowToken> all, Wf_FlowToken token);
internal static bool IsPrune(FlowNode split);

// SendBackScopeAnalyzer.cs
public enum SendBackScope { SameBranch, BeforeSplit, SiblingBranch }
public static (SendBackScope Scope, Wf_FlowToken? StripToken) Analyze(
    FlowSchema schema, IReadOnlyList<Wf_FlowToken> all, Wf_FlowToken current,
    string currentNodeId, string targetNodeId);                // 结构不可判定 → throw E-WF-012

// IWfNotifier.cs / WfNotificationType.cs
Task BranchPrunedAsync(Guid starterId, Guid instanceId, string flowKey, string nodeId, string? comment);
public const int BranchPruned = 5;
```

- 节点类型串：`"inclusiveSplit"` / `"inclusiveJoin"`（比较一律 OrdinalIgnoreCase，对齐 `EnterNodeAsync` 的 ToLowerInvariant 分发）。
- 履历 action：`"inclusiveSplit"` / `"inclusiveJoin"` / `"branchPruned"`；退回沿用 `"sendback"`。
- 错误码：**E-WF-019**（运行时，退回兄弟支拒绝）/ **E-WF-020**（静态，inclusive default 边）/ **E-WF-021**（静态，配对 + onBranchReject 值域/落点）。
- 前端 i18n 键：`oa.designer.gw.inclusiveSplit|inclusiveJoin|branchReject|branchReject.cascade|branchReject.prune|branchRejectHint`、`oa.designer.errInclusiveDefault|errInclusivePair|errBranchReject`、`E-WF-019|E-WF-020|E-WF-021`（12 键五语）。

---

