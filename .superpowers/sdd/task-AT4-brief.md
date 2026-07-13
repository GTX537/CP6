### Task A-T4: FlowGraph 配对辅助 + 校验 E-WF-020/021

> 依赖 A-T3（节点类型已存在）。`NearestCommonJoin` 同时是 C-T1 作用域分析的配对口径，签名不许漂移。

**Files:**
- Create: `CP6.Core/Services/Wf/FlowGraph.cs`
- Modify: `CP6.Core/Services/Wf/FlowSchema.cs`（FlowNode 加 `OnBranchReject` —— 提前到本 Task，供校验值域规则用；引擎侧消费在 B-T2）
- Modify: `CP6.Core/Services/Wf/FlowSchemaValidator.cs`
- Test: `CP6.Tests/Wf/InclusiveValidatorTests.cs`

- [ ] **Step 1: 写失败测试**

```csharp
// CP6.Tests/Wf/InclusiveValidatorTests.cs
using CP6.Core.Services.Wf;

namespace CP6.Tests.Wf;

/// <summary>E-WF-020/021 静态校验（hardening spec §6）。构造模式沿既有 ServiceTaskValidatorTests。</summary>
public class InclusiveValidatorTests
{
    // 合法基准：isplit → a["x > 0"], d[default] → ijoin → end
    private static FlowSchema Valid()
    {
        var s = new FlowSchema
        {
            Start = "s",
            Nodes =
            {
                new FlowNode { Id = "s", Type = "start" },
                new FlowNode { Id = "isplit", Type = "inclusiveSplit" },
                new FlowNode { Id = "a", Type = "approval", ApproverStrategy = "Specified", ApproverUserId = Guid.NewGuid() },
                new FlowNode { Id = "d", Type = "approval", ApproverStrategy = "Specified", ApproverUserId = Guid.NewGuid() },
                new FlowNode { Id = "ijoin", Type = "inclusiveJoin" },
                new FlowNode { Id = "end", Type = "end" },
            },
            Edges =
            {
                new FlowEdge { From = "s", To = "isplit" },
                new FlowEdge { From = "isplit", To = "a", Condition = "x > 0" },
                new FlowEdge { From = "isplit", To = "d" },
                new FlowEdge { From = "a", To = "ijoin" }, new FlowEdge { From = "d", To = "ijoin" },
                new FlowEdge { From = "ijoin", To = "end" },
            },
        };
        return s;
    }

    [Fact]
    public void ValidInclusivePair_Passes()
        => Assert.Empty(FlowSchemaValidator.Validate(Valid()));

    [Fact]
    public void NoDefaultEdge_E_WF_020()
    {
        var s = Valid();
        s.Edges.First(e => e.From == "isplit" && e.To == "d").Condition = "y > 0";   // 两条全带条件，无 default
        Assert.Contains("E-WF-020", FlowSchemaValidator.Validate(s));
    }

    [Fact]
    public void TwoDefaultEdges_E_WF_020()
    {
        var s = Valid();
        s.Edges.First(e => e.From == "isplit" && e.To == "a").Condition = null;      // 两条都无条件
        Assert.Contains("E-WF-020", FlowSchemaValidator.Validate(s));
    }

    [Fact]
    public void SingleOutEdge_E_WF_020()
    {
        var s = Valid();
        s.Edges.RemoveAll(e => e.From == "isplit" && e.To == "a");
        s.Edges.RemoveAll(e => e.From == "a");
        s.Nodes.RemoveAll(n => n.Id == "a");
        Assert.Contains("E-WF-020", FlowSchemaValidator.Validate(s));
    }

    [Fact]
    public void PairedWithParallelJoin_E_WF_021()
    {
        var s = Valid();
        s.Nodes.First(n => n.Id == "ijoin").Type = "parallelJoin";   // 最近公共汇聚类型错
        Assert.Contains("E-WF-021", FlowSchemaValidator.Validate(s));
    }

    [Fact]
    public void OrphanInclusiveJoin_E_WF_021()
    {
        // 无 split 配对的 inclusiveJoin（线性抵达）
        var s = new FlowSchema
        {
            Start = "s",
            Nodes =
            {
                new FlowNode { Id = "s", Type = "start" },
                new FlowNode { Id = "a", Type = "approval", ApproverStrategy = "Specified", ApproverUserId = Guid.NewGuid() },
                new FlowNode { Id = "b", Type = "approval", ApproverStrategy = "Specified", ApproverUserId = Guid.NewGuid() },
                new FlowNode { Id = "ijoin", Type = "inclusiveJoin" },
                new FlowNode { Id = "end", Type = "end" },
            },
            Edges =
            {
                new FlowEdge { From = "s", To = "a" },
                new FlowEdge { From = "a", To = "ijoin" }, new FlowEdge { From = "b", To = "ijoin" },
                new FlowEdge { From = "ijoin", To = "end" },
            },
        };
        Assert.Contains("E-WF-021", FlowSchemaValidator.Validate(s));
    }

    [Fact]
    public void OnBranchReject_BadValue_E_WF_021()
    {
        var s = Valid();
        s.Nodes.First(n => n.Id == "isplit").OnBranchReject = "explode";
        Assert.Contains("E-WF-021", FlowSchemaValidator.Validate(s));
    }

    [Fact]
    public void OnBranchReject_OnNonSplitNode_E_WF_021()
    {
        var s = Valid();
        s.Nodes.First(n => n.Id == "a").OnBranchReject = "prune";    // 写在 approval 上
        Assert.Contains("E-WF-021", FlowSchemaValidator.Validate(s));
    }

    [Fact]
    public void OnBranchReject_ValidValues_Pass()
    {
        var s = Valid();
        s.Nodes.First(n => n.Id == "isplit").OnBranchReject = "prune";
        Assert.Empty(FlowSchemaValidator.Validate(s));
        s.Nodes.First(n => n.Id == "isplit").OnBranchReject = "cascade";
        Assert.Empty(FlowSchemaValidator.Validate(s));
    }
}
```

- [ ] **Step 2: 跑验证 FAIL** — `--filter InclusiveValidatorTests`（OnBranchReject 编译失败 + 规则缺失断言失败）。

- [ ] **Step 3: 实现**

`FlowSchema.cs` `FlowNode` 服务任务字段块之后加（spec §2.1 逐字）：

```csharp
    // ── 内核 hardening（spec §2.1，可空向后兼容） ──
    /// <summary>分支驳回策略（仅 parallelSplit/inclusiveSplit 有意义）：null/"cascade"=连坐（现状）；"prune"=剪枝。</summary>
    public string? OnBranchReject { get; set; }
```

`FlowGraph.cs` 全文：

```csharp
namespace CP6.Core.Services.Wf;

/// <summary>schema 图论辅助。校验（E-WF-021 配对）与退回作用域分析（SendBackScopeAnalyzer）
/// 共用同一「最近公共汇聚 join」口径（spec §5.3 单一口径要求）。全部 BFS，环路安全（visited）。</summary>
internal static class FlowGraph
{
    internal static bool IsJoinType(FlowNode n)
        => (n.Type ?? "").Trim().ToLowerInvariant() is "paralleljoin" or "inclusivejoin";

    /// <summary>从 startId 正向可达节点集（含自身）。</summary>
    public static HashSet<string> ReachableFrom(FlowSchema schema, string startId)
    {
        var seen = new HashSet<string> { startId };
        var q = new Queue<string>(); q.Enqueue(startId);
        while (q.Count > 0)
        {
            var cur = q.Dequeue();
            foreach (var e in schema.Edges.Where(e => e.From == cur))
                if (seen.Add(e.To)) q.Enqueue(e.To);
        }
        return seen;
    }

    /// <summary>split 的配对 join = 各出边（IsError!=true）可达集交集中的 join 型节点里、距 split BFS 最近者。
    /// 无出边 / 无公共 join → null（校验报 E-WF-021；退回作用域分析保守拒 E-WF-012）。</summary>
    public static FlowNode? NearestCommonJoin(FlowSchema schema, FlowNode split)
    {
        var outs = schema.Edges.Where(e => e.From == split.Id && e.IsError != true).ToList();
        if (outs.Count == 0) return null;
        HashSet<string>? common = null;
        foreach (var e in outs)
        {
            var r = ReachableFrom(schema, e.To);
            if (common is null) common = r; else common.IntersectWith(r);
        }
        if (common is null || common.Count == 0) return null;

        // 距 split 最近（BFS 深度）且为 join 型
        var depth = new Dictionary<string, int> { [split.Id] = 0 };
        var q = new Queue<string>(); q.Enqueue(split.Id);
        while (q.Count > 0)
        {
            var cur = q.Dequeue();
            foreach (var e in schema.Edges.Where(e => e.From == cur))
                if (!depth.ContainsKey(e.To)) { depth[e.To] = depth[cur] + 1; q.Enqueue(e.To); }
        }
        FlowNode? best = null; int bestD = int.MaxValue;
        foreach (var n in schema.Nodes)
        {
            if (!common.Contains(n.Id) || !IsJoinType(n)) continue;
            if (depth.TryGetValue(n.Id, out var d) && d < bestD) { best = n; bestD = d; }
        }
        return best;
    }

    /// <summary>分支域：从 split 某出边目标出发的正向可达集（含该目标），<b>不进入、不穿过</b> 配对 join。
    /// 退回作用域分析用（SameBranch/SiblingBranch 判定）。</summary>
    public static HashSet<string> BranchDomain(FlowSchema schema, string edgeTargetId, string pairedJoinId)
    {
        var seen = new HashSet<string>();
        if (edgeTargetId == pairedJoinId) return seen;
        seen.Add(edgeTargetId);
        var q = new Queue<string>(); q.Enqueue(edgeTargetId);
        while (q.Count > 0)
        {
            var cur = q.Dequeue();
            foreach (var e in schema.Edges.Where(e => e.From == cur))
            {
                if (e.To == pairedJoinId) continue;   // 到配对 join 即分支域边界
                if (seen.Add(e.To)) q.Enqueue(e.To);
            }
        }
        return seen;
    }
}
```

`FlowSchemaValidator.cs` 在 ⑨ 错误出边规则之后、④ 可达性规则之前插入（风格对齐既有分节注释）：

```csharp
        // ⑩ inclusive 网关（hardening E-WF-020/021）
        // E-WF-020：inclusiveSplit 出边（非错误边）须 ≥2 且恰好一条无条件 default 兜底边
        foreach (var n in schema.Nodes.Where(n => T(n) == "inclusivesplit"))
        {
            var outs = schema.Edges.Where(e => e.From == n.Id && e.IsError != true).ToList();
            var dflt = outs.Count(e => string.IsNullOrWhiteSpace(e.Condition));
            if (outs.Count < 2 || dflt != 1) { errs.Add("E-WF-020"); break; }
        }

        // E-WF-021a：每个 inclusiveSplit 的最近公共汇聚须存在且类型为 inclusiveJoin
        var pairedJoinIds = new HashSet<string>();
        foreach (var n in schema.Nodes.Where(n => T(n) == "inclusivesplit"))
        {
            var join = FlowGraph.NearestCommonJoin(schema, n);
            if (join is null || T(join) != "inclusivejoin") { errs.Add("E-WF-021"); continue; }
            pairedJoinIds.Add(join.Id);
        }
        // E-WF-021b：inclusiveJoin 入边 ≥2 且被至少一个 inclusiveSplit 配对（孤立 join 报错）
        foreach (var n in schema.Nodes.Where(n => T(n) == "inclusivejoin"))
            if (schema.Edges.Count(e => e.To == n.Id) < 2 || !pairedJoinIds.Contains(n.Id))
            { errs.Add("E-WF-021"); break; }
        // E-WF-021c：onBranchReject 值域 ∈ {cascade, prune}（大小写不敏感）且仅允许写在 split 型节点上
        foreach (var n in schema.Nodes)
        {
            if (string.IsNullOrWhiteSpace(n.OnBranchReject)) continue;
            var v = n.OnBranchReject.Trim().ToLowerInvariant();
            bool onSplit = T(n) is "parallelsplit" or "inclusivesplit";
            if (!onSplit || (v != "cascade" && v != "prune")) { errs.Add("E-WF-021"); break; }
        }
```

- [ ] **Step 4: 跑验证 PASS** — `--filter InclusiveValidatorTests` 全绿。
- [ ] **Step 5: Wf 闸 + commit**

```bash
dotnet test CP6.Tests/CP6.Tests.csproj --filter Wf
git add -A && git commit -m "feat(wfs-hardening): A-T4 FlowGraph配对辅助+FlowSchemaValidator E-WF-020/021+OnBranchReject POCO"
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

