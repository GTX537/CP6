# WFS 内核 Hardening Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking. **每个 Task 执行前必读对应 spec 章节**（`docs/superpowers/specs/2026-07-05-wfs-kernel-hardening-design.md`，唯一权威，决策 D1~D5 全锁不许重新设计）。本计划所有 C#/TS 代码块均按 2026-07-05 main（fb90d75）实读代码上下文写就，测试代码逐条给全。

**Goal:** WFS 引擎内核 hardening 三项：① inclusive 包容网关对（`inclusiveSplit`/`inclusiveJoin`，按条件走一到多条 + 唯一无条件 default 兜底边）；② 驳回剪枝（split 节点级 `onBranchReject: cascade(默认)|prune`，剪枝只死本分支、join 动态计票补放行、全剪光血缘感知递归上弹）；③ 退回三规则（SameBranch 分支内剥离层局部退回 / BeforeSplit 整块重来 / SiblingBranch 拒绝 E-WF-019）。设计器镜像（palette/属性面板/validateClient/五语 i18n/QA harness）。

**Architecture:** 零 EF 迁移（FlowNode 是 SchemaJson 内 POCO，token 剪枝走新常量 `FlowTokenStatus.Pruned=3`）。inclusive 网关 = 独立 handler 字典第 7/8 个（D3，不在 parallel handler 上加 mode 旗标）；parallelJoin 与 inclusiveJoin 共用 `GatewayJoinHelper` 静态辅助做 **D4 血缘感知动态计票**（放行 = 到场≥1 且 不存在「祖先链穿过本 fork 批次」的其他在途 Active token）。剪枝/退回共用 `TokenLineage` 血缘辅助（ForkParent 上溯定位 split，零新列）；校验 E-WF-021 与退回作用域分析共用 `FlowGraph.NearestCommonJoin` 同一配对口径。

**Tech Stack:** .NET 8 / EF Core（SQL Server 生产、InMemory+SQLite 测试）/ xUnit（`CP6.Tests/Wf`，InternalsVisibleTo 已开）/ Vue3 + Vue Flow（`cp6.web/src/views/oa/designer`）/ vitest。

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

## Wave H-A — 动态计票 + inclusive 网关（一切后续任务的地基）

### Task A-T1: Pruned 常量 + SnapshotTokens 抽取 + TokenLineage 血缘辅助（纯函数）

**Files:**
- Modify: `CP6.Core/Services/Wf/WfStatus.cs`
- Modify: `CP6.Core/Services/Wf/FlowEngine.Tokens.cs`（加 SnapshotTokens）
- Create: `CP6.Core/Services/Wf/TokenLineage.cs`
- Test: `CP6.Tests/Wf/TokenLineageTests.cs`

- [ ] **Step 1: 写失败测试**

```csharp
// CP6.Tests/Wf/TokenLineageTests.cs
using CP6.Core.Services.Wf;
using CP6.Entity.DomainModels.Wf;

namespace CP6.Tests.Wf;

public class TokenLineageTests
{
    private static readonly Guid InstId = Guid.NewGuid();

    private static Wf_FlowToken Tok(Guid id, string node, Guid? parent = null, Guid? fork = null,
        int status = FlowTokenStatus.Active)
        => new() { Id = id, InstanceId = InstId, NodeId = node, ParentTokenId = parent, ForkId = fork, Status = status };

    [Fact]
    public void FlowTokenStatus_Pruned_Is3()
        => Assert.Equal(3, FlowTokenStatus.Pruned);

    [Fact]
    public void CrossesFork_SelfAndAncestor_True_Miss_False()
    {
        var f1 = Guid.NewGuid(); var f2 = Guid.NewGuid(); var fx = Guid.NewGuid();
        var root  = Tok(Guid.NewGuid(), "split", status: FlowTokenStatus.Consumed);
        var b     = Tok(Guid.NewGuid(), "innerSplit", parent: root.Id, fork: f1, status: FlowTokenStatus.Consumed);
        var inner = Tok(Guid.NewGuid(), "x1", parent: b.Id, fork: f2);
        var all = new[] { root, b, inner };

        Assert.True(TokenLineage.CrossesFork(all, inner, f2));   // 自身 ForkId
        Assert.True(TokenLineage.CrossesFork(all, inner, f1));   // 祖先链穿过外层批次（防提前放行的关键）
        Assert.False(TokenLineage.CrossesFork(all, inner, fx));  // 无关批次
        Assert.False(TokenLineage.CrossesFork(all, root, f1));   // 根不穿任何批次
    }

    [Fact]
    public void ForkParent_ReturnsBatchGenerator()
    {
        var f1 = Guid.NewGuid();
        var root = Tok(Guid.NewGuid(), "split", status: FlowTokenStatus.Consumed);
        var a    = Tok(Guid.NewGuid(), "a", parent: root.Id, fork: f1);
        var all = new[] { root, a };

        var p = TokenLineage.ForkParent(all, a);
        Assert.NotNull(p);
        Assert.Equal(root.Id, p!.Id);
        Assert.Equal("split", p.NodeId);                          // §4.1 定案：父 token.NodeId 即 split 节点
        Assert.Null(TokenLineage.ForkParent(all, root));          // 根无父
    }

    [Fact]
    public void ForkStack_Nested_InnerToOuter()
    {
        var f1 = Guid.NewGuid(); var f2 = Guid.NewGuid();
        var root  = Tok(Guid.NewGuid(), "split", status: FlowTokenStatus.Consumed);
        var b     = Tok(Guid.NewGuid(), "innerSplit", parent: root.Id, fork: f1, status: FlowTokenStatus.Consumed);
        var inner = Tok(Guid.NewGuid(), "x1", parent: b.Id, fork: f2);
        var all = new[] { root, b, inner };

        var stack = TokenLineage.ForkStack(all, inner);
        Assert.Equal(2, stack.Count);
        Assert.Equal((inner.Id, f2, "innerSplit"), (stack[0].BranchToken.Id, stack[0].ForkId, stack[0].SplitNodeId));
        Assert.Equal((b.Id, f1, "split"), (stack[1].BranchToken.Id, stack[1].ForkId, stack[1].SplitNodeId));
    }

    [Fact]
    public void ForkStack_LinearToken_Empty()
    {
        var root = Tok(Guid.NewGuid(), "n1");
        Assert.Empty(TokenLineage.ForkStack(new[] { root }, root));
    }
}
```

- [ ] **Step 2: 跑测试验证 FAIL** — `dotnet test CP6.Tests/CP6.Tests.csproj --filter TokenLineageTests`。预期编译失败（TokenLineage/Pruned 不存在）。

- [ ] **Step 3: 最小实现**

`WfStatus.cs` 的 `FlowTokenStatus` 加一行（保持既有注释风格）：

```csharp
    /// <summary>剪枝（分支驳回不连坐，spec §2.3）。区别 Cancelled=清场类作废：Pruned 是分支死亡，join 动态计票时天然从等待集消失。</summary>
    public const int Pruned = 3;
```

`FlowEngine.Tokens.cs` 加（放在 `CancelAllActiveTokens` 之前）：

```csharp
    /// <summary>本实例 token 快照：Local（含本回合未落盘的）∪ DB（已落盘的），按引用去重
    /// （EF 身份映射保证同实体同引用）。口径抽自 ParallelJoinNodeHandler.AllTokens，供双 join 动态计票、
    /// 剪枝递归、退回作用域分析共用。</summary>
    internal IReadOnlyList<Wf_FlowToken> SnapshotTokens(Guid instanceId)
        => _db.Wf_FlowTokens.Local.Where(t => t.InstanceId == instanceId)
            .Concat(_db.Wf_FlowTokens.Where(t => t.InstanceId == instanceId).AsEnumerable())
            .Distinct().ToList();
```

`TokenLineage.cs` 全文：

```csharp
using CP6.Entity.DomainModels.Wf;

namespace CP6.Core.Services.Wf;

/// <summary>token 血缘辅助（内核 hardening spec §3.3/§4/§5 共用同一口径）。全部纯函数，
/// 输入为 <see cref="FlowEngine.SnapshotTokens"/> 的实例内 token 快照；祖先链走内存 ParentTokenId 上溯
/// （单实例 token 数小，零额外查询）。环路防御：visited 集合。</summary>
internal static class TokenLineage
{
    /// <summary>t 自身 + 沿 ParentTokenId 的全部祖先（自内向外）。</summary>
    public static IEnumerable<Wf_FlowToken> AncestorChain(IReadOnlyList<Wf_FlowToken> all, Wf_FlowToken t)
    {
        var seen = new HashSet<Guid>();
        for (var cur = t; cur is not null && seen.Add(cur.Id);
             cur = cur.ParentTokenId is Guid pid ? all.FirstOrDefault(x => x.Id == pid) : null)
            yield return cur;
    }

    /// <summary>t「穿过」fork 批次 forkId ⇔ t 自身或祖先链上存在 ForkId==forkId 的 token（spec §3.3 定义）。</summary>
    public static bool CrossesFork(IReadOnlyList<Wf_FlowToken> all, Wf_FlowToken t, Guid forkId)
        => AncestorChain(all, t).Any(x => x.ForkId == forkId);

    /// <summary>生成 t.ForkId 批次的 token = Id==t.ParentTokenId 者；其 NodeId 即该批次的 split 节点（§4.1 定案）。
    /// t 无父 → null。</summary>
    public static Wf_FlowToken? ForkParent(IReadOnlyList<Wf_FlowToken> all, Wf_FlowToken t)
        => t.ParentTokenId is Guid pid ? all.FirstOrDefault(x => x.Id == pid) : null;

    /// <summary>t 的 fork 栈（内→外）：祖先链上每个 ForkId 非空的 token 贡献一层
    /// (该层分支代表 token, forkId, split 节点 id)。同 forkId 只取最靠 t 的一个（防御）。</summary>
    public static List<(Wf_FlowToken BranchToken, Guid ForkId, string SplitNodeId)> ForkStack(
        IReadOnlyList<Wf_FlowToken> all, Wf_FlowToken t)
    {
        var stack = new List<(Wf_FlowToken, Guid, string)>();
        var seenForks = new HashSet<Guid>();
        foreach (var tok in AncestorChain(all, t))
        {
            if (tok.ForkId is not Guid f || !seenForks.Add(f)) continue;
            var parent = ForkParent(all, tok);
            if (parent is null) continue;   // 血缘断裂（不完整快照）→ 该层不可判定，跳过
            stack.Add((tok, f, parent.NodeId));
        }
        return stack;
    }
}
```

- [ ] **Step 4: 跑测试验证 PASS** — `--filter TokenLineageTests` 全绿。
- [ ] **Step 5: Wf 闸 + commit**

```bash
dotnet test CP6.Tests/CP6.Tests.csproj --filter Wf    # 既有照绿（本 Task 未改任何执行路径）
git add -A && git commit -m "feat(wfs-hardening): A-T1 Pruned常量+SnapshotTokens快照+TokenLineage血缘辅助"
```

---

### Task A-T2: GatewayJoinHelper 动态计票 + parallelJoin 改造（D4，回归锁定）

> 依赖 A-T1。**这是全计划风险最高的一步**：改既有 parallelJoin 语义实现，靠「旧场景全等回归 + 嵌套在途定点 + null-fork 退化」三层锁死。

**Files:**
- Create: `CP6.Core/Services/Wf/NodeHandlers/GatewayJoinHelper.cs`
- Modify: `CP6.Core/Services/Wf/NodeHandlers/ParallelJoinNodeHandler.cs`
- Test: `CP6.Tests/Wf/DynamicJoinCountTests.cs`

- [ ] **Step 1: 写失败测试**

```csharp
// CP6.Tests/Wf/DynamicJoinCountTests.cs
using System.Text.Json;
using CP6.Core.EFDbContext;
using CP6.Core.Services.Wf;
using CP6.Entity.DomainModels.Wf;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace CP6.Tests.Wf;

/// <summary>D4 动态计票定点回归（hardening spec §3.3/§9）。旧场景全等由既有 ParallelGatewayTests 5 测锁定，
/// 本文件补：①嵌套在途防提前放行（spec 评审抓过的洞）②ForkId==null 退化保持旧静态计票。</summary>
public class DynamicJoinCountTests
{
    private static CP6Context NewDb() => new(new DbContextOptionsBuilder<CP6Context>()
        .UseInMemoryDatabase(Guid.NewGuid().ToString())
        .ConfigureWarnings(w => w.Ignore(InMemoryEventId.TransactionIgnoredWarning)).Options);
    private static FlowEngine Engine(CP6Context db) => new(db, new ApproverResolver(db));

    // start → split → ( a → innerSplit → (a1,a2) → innerJoin → join , b → join ) → join → end
    private static FlowSchema NestedSchema(Guid u1, Guid u2, Guid ub) => new()
    {
        Start = "s",
        Nodes =
        {
            new FlowNode { Id = "s", Type = "start" },
            new FlowNode { Id = "split", Type = "parallelSplit" },
            new FlowNode { Id = "a", Type = "parallelSplit" },
            new FlowNode { Id = "a1", Type = "approval", ApproverStrategy = "Specified", ApproverUserId = u1 },
            new FlowNode { Id = "a2", Type = "approval", ApproverStrategy = "Specified", ApproverUserId = u2 },
            new FlowNode { Id = "innerJoin", Type = "parallelJoin" },
            new FlowNode { Id = "b", Type = "approval", ApproverStrategy = "Specified", ApproverUserId = ub },
            new FlowNode { Id = "join", Type = "parallelJoin" },
            new FlowNode { Id = "end", Type = "end" },
        },
        Edges =
        {
            new FlowEdge { From = "s", To = "split" },
            new FlowEdge { From = "split", To = "a" }, new FlowEdge { From = "split", To = "b" },
            new FlowEdge { From = "a", To = "a1" }, new FlowEdge { From = "a", To = "a2" },
            new FlowEdge { From = "a1", To = "innerJoin" }, new FlowEdge { From = "a2", To = "innerJoin" },
            new FlowEdge { From = "innerJoin", To = "join" },
            new FlowEdge { From = "b", To = "join" },
            new FlowEdge { From = "join", To = "end" },
        },
    };

    [Fact]
    public async Task NestedInFlight_OuterJoinWaits_UntilInnerSubtreeDone()
    {
        using var db = NewDb();
        Guid u1 = Guid.NewGuid(), u2 = Guid.NewGuid(), ub = Guid.NewGuid();
        db.Wf_FlowDefs.Add(new Wf_FlowDef { Id = Guid.NewGuid(), FlowKey = "nst", FlowName = "nst", FormKey = "f",
            SchemaJson = JsonSerializer.Serialize(NestedSchema(u1, u2, ub)), Version = 1, Enable = true });
        await db.SaveChangesAsync();
        await Engine(db).SubmitAsync("nst", Guid.NewGuid(), "{}");

        // ★ 先审 b：外层 A 支在内层子 fork 在途（同外层 ForkId 无 Active，只有血缘链）→ 外层 join 必须等
        var tb = await db.Wf_FlowTasks.SingleAsync(t => t.AssigneeId == ub && t.Status == FlowTaskStatus.Pending);
        await Engine(db).ActAsync(tb.Id, ub, approve: true);
        Assert.Equal(FlowInstanceStatus.Running, (await db.Wf_FlowInstances.SingleAsync()).Status);
        Assert.True(await db.Wf_FlowTasks.AnyAsync(t => t.AssigneeId == u1 && t.Status == FlowTaskStatus.Pending));
        Assert.True(await db.Wf_FlowTokens.AnyAsync(t => t.NodeId == "join" && t.Status == FlowTokenStatus.Active));

        var t1 = await db.Wf_FlowTasks.SingleAsync(t => t.AssigneeId == u1 && t.Status == FlowTaskStatus.Pending);
        await Engine(db).ActAsync(t1.Id, u1, approve: true);
        Assert.Equal(FlowInstanceStatus.Running, (await db.Wf_FlowInstances.SingleAsync()).Status);

        var t2 = await db.Wf_FlowTasks.SingleAsync(t => t.AssigneeId == u2 && t.Status == FlowTaskStatus.Pending);
        await Engine(db).ActAsync(t2.Id, u2, approve: true);   // 内层齐 → 上弹 → 外层齐 → end

        Assert.Equal(FlowInstanceStatus.Approved, (await db.Wf_FlowInstances.SingleAsync()).Status);
        Assert.False(await db.Wf_FlowTokens.AnyAsync(t => t.Status == FlowTokenStatus.Active));
    }

    [Fact]
    public async Task NullFork_LinearTokenAtJoin_KeepsLegacyStaticCount_ParksForever()
    {
        using var db = NewDb();
        var ua = Guid.NewGuid(); var ub = Guid.NewGuid();
        // 怪异 schema：join 有 2 条入边，但 token 沿线性路径（无 split）到达 join，ForkId==null
        var schema = new FlowSchema
        {
            Start = "s",
            Nodes =
            {
                new FlowNode { Id = "s", Type = "start" },
                new FlowNode { Id = "a", Type = "approval", ApproverStrategy = "Specified", ApproverUserId = ua },
                new FlowNode { Id = "b", Type = "approval", ApproverStrategy = "Specified", ApproverUserId = ub },
                new FlowNode { Id = "join", Type = "parallelJoin" },
                new FlowNode { Id = "end", Type = "end" },
            },
            Edges =
            {
                new FlowEdge { From = "s", To = "a" },
                new FlowEdge { From = "a", To = "join" }, new FlowEdge { From = "b", To = "join" },
                new FlowEdge { From = "join", To = "end" },
            },
        };
        db.Wf_FlowDefs.Add(new Wf_FlowDef { Id = Guid.NewGuid(), FlowKey = "nullfork", FlowName = "x", FormKey = "f",
            SchemaJson = JsonSerializer.Serialize(schema), Version = 1, Enable = true });
        await db.SaveChangesAsync();
        await Engine(db).SubmitAsync("nullfork", Guid.NewGuid(), "{}");

        var ta = await db.Wf_FlowTasks.SingleAsync(t => t.AssigneeId == ua && t.Status == FlowTaskStatus.Pending);
        await Engine(db).ActAsync(ta.Id, ua, approve: true);

        // 旧静态计票：到场 1 < 入边 2 → 永停泊。动态判据若不做 null 退化会在此放行 → 行为漂移（本测试拦住）
        Assert.Equal(FlowInstanceStatus.Running, (await db.Wf_FlowInstances.SingleAsync()).Status);
        Assert.True(await db.Wf_FlowTokens.AnyAsync(t => t.NodeId == "join" && t.Status == FlowTokenStatus.Active));
    }
}
```

- [ ] **Step 2: 跑验证基线** — `--filter DynamicJoinCountTests`：两测在**旧实现下也应绿**（这是等价性测试，先绿后改，改完必须仍绿）。再跑 `--filter ParallelGatewayTests` 确认 5 测绿。记录两组结果。

- [ ] **Step 3: 实现** — `GatewayJoinHelper.cs` 全文：

```csharp
using CP6.Entity.DomainModels.Wf;

namespace CP6.Core.Services.Wf;

/// <summary>并行/包容 join 共享放行逻辑（D3 不合并 handler、抽静态辅助；D4 血缘感知动态计票）。
/// 放行判据（spec §3.3）：本 join 到场数（同 ForkId Active）≥1 且 不存在「穿过本 fork 批次」的其他在途
/// Active token（停在本 join 的到场 token 除外）。血缘感知 = 分支进入内层 split 后同 ForkId 无 Active、
/// 但子 token 祖先链穿过本批次 → 仍挡放行（防「A 到场、B 在内层子 fork 在途」误判提前放行）。
/// ForkId==null 退化（线性 token 进 join 的怪异 schema）→ 沿用旧静态入边计票，bit 级等价。
/// 放行 = 消费同批到场 token + 续生一枚「上弹一层」血缘的 token 沿单出边继续（原 ParallelJoinNodeHandler 机制原样保留）。
/// 计数本身即幂等闸：未齐重入 no-op，重入安全（剪枝补放行依赖此性质）。</summary>
internal static class GatewayJoinHelper
{
    public static async Task TryReleaseAsync(NodeContext ctx, string historyAction)
    {
        var eng = ctx.Engine; var inst = ctx.Inst; var schema = ctx.Schema; var node = ctx.Node;
        var all = eng.SnapshotTokens(inst.Id);

        if (ctx.Token.ForkId is not Guid forkId)
        {
            // 退化路径：与旧 ParallelJoinNodeHandler 静态计票字节等价
            var inEdges = schema.Edges.Count(e => e.To == node.Id);
            var nullArrived = all.Count(t => t.NodeId == node.Id && t.ForkId == null
                && t.Status == FlowTokenStatus.Active);
            if (nullArrived < inEdges) return;
        }
        else
        {
            var arrivedCount = all.Count(t => t.NodeId == node.Id && t.ForkId == forkId
                && t.Status == FlowTokenStatus.Active);
            if (arrivedCount == 0) return;
            bool blocking = all.Any(t => t.Status == FlowTokenStatus.Active
                && !(t.NodeId == node.Id && t.ForkId == forkId)     // 停在本 join 的到场 token 除外
                && TokenLineage.CrossesFork(all, t, forkId));
            if (blocking) return;   // 还有活支（含内层子树在途）→ 停泊等
        }

        var batch = all.Where(t => t.NodeId == node.Id && t.ForkId == ctx.Token.ForkId
            && t.Status == FlowTokenStatus.Active).ToList();
        foreach (var t in batch) eng.ConsumeToken(t);

        var parentTok = ctx.Token.ParentTokenId is Guid pid
            ? all.FirstOrDefault(t => t.Id == pid) : null;
        var cont = eng.SpawnToken(inst, node, parent: parentTok?.ParentTokenId, fork: parentTok?.ForkId);
        eng.AddHistory(inst.Id, node.Id, inst.StarterId, historyAction, null);
        await eng.AdvanceToken(inst, schema, cont);   // 续 token 沿 join 单出边继续
    }
}
```

`ParallelJoinNodeHandler.cs` 改为纯委托（删 AllTokens 与静态计票，类注释更新为指向 GatewayJoinHelper）：

```csharp
using CP6.Entity.DomainModels.Wf;

namespace CP6.Core.Services.Wf;

/// <summary>并行汇聚网关（WFS P1 → hardening D4 动态计票）：放行判据与机制见 <see cref="GatewayJoinHelper"/>。
/// 无剪枝/无嵌套时与旧静态入边计票行为全等（ParallelGatewayTests + DynamicJoinCountTests 回归锁定）。</summary>
internal sealed class ParallelJoinNodeHandler : INodeHandler
{
    public string Type => "parallelJoin";
    public Task OnEnterAsync(NodeContext ctx) => GatewayJoinHelper.TryReleaseAsync(ctx, "parallelJoin");
}
```

- [ ] **Step 4: 跑验证 PASS** — `--filter DynamicJoinCountTests` + `--filter ParallelGatewayTests` + `--filter FlowConcurrencyTests` 全绿（并发重试路径复算 join 计数，必须专门确认）。
- [ ] **Step 5: 全量 Wf 闸 + commit**

```bash
dotnet test CP6.Tests/CP6.Tests.csproj --filter Wf    # 既有 27 不变量测试断言零改、全绿
git add -A && git commit -m "feat(wfs-hardening): A-T2 GatewayJoinHelper血缘感知动态计票+parallelJoin改造(旧场景全等回归)"
```

### Task A-T3: InclusiveSplit/InclusiveJoin 两 handler + 注册（第 7/8 个）

> 依赖 A-T2（join 放行走 GatewayJoinHelper）。

**Files:**
- Create: `CP6.Core/Services/Wf/NodeHandlers/InclusiveSplitNodeHandler.cs`
- Create: `CP6.Core/Services/Wf/NodeHandlers/InclusiveJoinNodeHandler.cs`
- Modify: `CP6.Core/Services/Wf/FlowEngine.cs`（`DefaultHandlers()` :38-43 加 2 项，注释「五 handler」改「八 handler」）
- Modify: `CP6.WebApi/Program.cs`（:113 `ParallelJoinNodeHandler` 注册行后加 2 行 DI）
- Test: `CP6.Tests/Wf/InclusiveGatewayTests.cs`

- [ ] **Step 1: 写失败测试**

```csharp
// CP6.Tests/Wf/InclusiveGatewayTests.cs
using System.Text.Json;
using CP6.Core.EFDbContext;
using CP6.Core.Services.Wf;
using CP6.Entity.DomainModels.Wf;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace CP6.Tests.Wf;

/// <summary>inclusive 网关行为（hardening spec §3.1/§3.2/§9）。构造模式沿 ParallelGatewayTests。</summary>
public class InclusiveGatewayTests
{
    private static CP6Context NewDb() => new(new DbContextOptionsBuilder<CP6Context>()
        .UseInMemoryDatabase(Guid.NewGuid().ToString())
        .ConfigureWarnings(w => w.Ignore(InMemoryEventId.TransactionIgnoredWarning)).Options);
    private static FlowEngine Engine(CP6Context db) => new(db, new ApproverResolver(db));

    // s → isplit → ( a["goA > 0"], b["goB > 0"], c["goC > 0"], d[default 无条件] ) → ijoin → end
    private static FlowSchema IncSchema(Guid ua, Guid ub, Guid uc, Guid ud) => new()
    {
        Start = "s",
        Nodes =
        {
            new FlowNode { Id = "s", Type = "start" },
            new FlowNode { Id = "isplit", Type = "inclusiveSplit" },
            new FlowNode { Id = "a", Type = "approval", ApproverStrategy = "Specified", ApproverUserId = ua },
            new FlowNode { Id = "b", Type = "approval", ApproverStrategy = "Specified", ApproverUserId = ub },
            new FlowNode { Id = "c", Type = "approval", ApproverStrategy = "Specified", ApproverUserId = uc },
            new FlowNode { Id = "d", Type = "approval", ApproverStrategy = "Specified", ApproverUserId = ud },
            new FlowNode { Id = "ijoin", Type = "inclusiveJoin" },
            new FlowNode { Id = "end", Type = "end" },
        },
        Edges =
        {
            new FlowEdge { From = "s", To = "isplit" },
            new FlowEdge { From = "isplit", To = "a", Condition = "goA > 0" },
            new FlowEdge { From = "isplit", To = "b", Condition = "goB > 0" },
            new FlowEdge { From = "isplit", To = "c", Condition = "goC > 0" },
            new FlowEdge { From = "isplit", To = "d" },                          // default 兜底边
            new FlowEdge { From = "a", To = "ijoin" }, new FlowEdge { From = "b", To = "ijoin" },
            new FlowEdge { From = "c", To = "ijoin" }, new FlowEdge { From = "d", To = "ijoin" },
            new FlowEdge { From = "ijoin", To = "end" },
        },
    };

    private static async Task<Guid> SeedAndSubmitAsync(CP6Context db, Guid ua, Guid ub, Guid uc, Guid ud, string vars)
    {
        db.Wf_FlowDefs.Add(new Wf_FlowDef { Id = Guid.NewGuid(), FlowKey = "inc", FlowName = "inc", FormKey = "f",
            SchemaJson = JsonSerializer.Serialize(IncSchema(ua, ub, uc, ud)), Version = 1, Enable = true });
        await db.SaveChangesAsync();
        return await Engine(db).SubmitAsync("inc", Guid.NewGuid(), vars);
    }

    [Fact]
    public async Task TwoOfThreeTrue_SpawnsOnlyTrueBranches_DefaultNotWalked()
    {
        using var db = NewDb();
        Guid ua = Guid.NewGuid(), ub = Guid.NewGuid(), uc = Guid.NewGuid(), ud = Guid.NewGuid();
        await SeedAndSubmitAsync(db, ua, ub, uc, ud, "{\"goA\":1,\"goB\":1,\"goC\":0}");

        Assert.Equal(2, await db.Wf_FlowTokens.CountAsync(t => t.Status == FlowTokenStatus.Active));
        Assert.True(await db.Wf_FlowTasks.AnyAsync(t => t.AssigneeId == ua && t.Status == FlowTaskStatus.Pending));
        Assert.True(await db.Wf_FlowTasks.AnyAsync(t => t.AssigneeId == ub && t.Status == FlowTaskStatus.Pending));
        Assert.False(await db.Wf_FlowTasks.AnyAsync(t => t.AssigneeId == uc));   // 假边不走
        Assert.False(await db.Wf_FlowTasks.AnyAsync(t => t.AssigneeId == ud));   // ★ 有真边时 default 不走
        Assert.Equal(1, await db.Wf_FlowHistories.CountAsync(h => h.Action == "inclusiveSplit"));

        // 只等实际激活的两支：a、b 都过 → 放行到 end
        var ta = await db.Wf_FlowTasks.SingleAsync(t => t.AssigneeId == ua && t.Status == FlowTaskStatus.Pending);
        await Engine(db).ActAsync(ta.Id, ua, approve: true);
        Assert.Equal(FlowInstanceStatus.Running, (await db.Wf_FlowInstances.SingleAsync()).Status);
        var tb = await db.Wf_FlowTasks.SingleAsync(t => t.AssigneeId == ub && t.Status == FlowTaskStatus.Pending);
        await Engine(db).ActAsync(tb.Id, ub, approve: true);
        Assert.Equal(FlowInstanceStatus.Approved, (await db.Wf_FlowInstances.SingleAsync()).Status);
    }

    [Fact]
    public async Task AllConditionsTrue_AllThreeWalk_DefaultStillNotWalked()
    {
        using var db = NewDb();
        Guid ua = Guid.NewGuid(), ub = Guid.NewGuid(), uc = Guid.NewGuid(), ud = Guid.NewGuid();
        await SeedAndSubmitAsync(db, ua, ub, uc, ud, "{\"goA\":1,\"goB\":1,\"goC\":1}");

        Assert.Equal(3, await db.Wf_FlowTokens.CountAsync(t => t.Status == FlowTokenStatus.Active));
        Assert.False(await db.Wf_FlowTasks.AnyAsync(t => t.AssigneeId == ud));   // ★ 全真条件边时 default 不走
    }

    [Fact]
    public async Task AllFalse_OnlyDefaultWalks_SingleBranchJoinReleases()
    {
        using var db = NewDb();
        Guid ua = Guid.NewGuid(), ub = Guid.NewGuid(), uc = Guid.NewGuid(), ud = Guid.NewGuid();
        await SeedAndSubmitAsync(db, ua, ub, uc, ud, "{\"goA\":0,\"goB\":0,\"goC\":0}");

        Assert.Equal(1, await db.Wf_FlowTokens.CountAsync(t => t.Status == FlowTokenStatus.Active));
        var td = await db.Wf_FlowTasks.SingleAsync(t => t.Status == FlowTaskStatus.Pending);
        Assert.Equal(ud, td.AssigneeId);                              // ★ 全假仅 default 兜底

        await Engine(db).ActAsync(td.Id, ud, approve: true);          // 单支到场即齐（活支==1）
        Assert.Equal(FlowInstanceStatus.Approved, (await db.Wf_FlowInstances.SingleAsync()).Status);
    }

    // 嵌套：s → psplit → ( isplit⊂parallel 支 , p2 支 ) → pjoin → end；inclusive 内嵌 a/b + default d
    [Fact]
    public async Task InclusiveInsideParallel_OuterJoinWaitsInclusiveSubtree()
    {
        using var db = NewDb();
        Guid ua = Guid.NewGuid(), ud = Guid.NewGuid(), up = Guid.NewGuid();
        var schema = new FlowSchema
        {
            Start = "s",
            Nodes =
            {
                new FlowNode { Id = "s", Type = "start" },
                new FlowNode { Id = "psplit", Type = "parallelSplit" },
                new FlowNode { Id = "isplit", Type = "inclusiveSplit" },
                new FlowNode { Id = "a", Type = "approval", ApproverStrategy = "Specified", ApproverUserId = ua },
                new FlowNode { Id = "d", Type = "approval", ApproverStrategy = "Specified", ApproverUserId = ud },
                new FlowNode { Id = "ijoin", Type = "inclusiveJoin" },
                new FlowNode { Id = "p2", Type = "approval", ApproverStrategy = "Specified", ApproverUserId = up },
                new FlowNode { Id = "pjoin", Type = "parallelJoin" },
                new FlowNode { Id = "end", Type = "end" },
            },
            Edges =
            {
                new FlowEdge { From = "s", To = "psplit" },
                new FlowEdge { From = "psplit", To = "isplit" }, new FlowEdge { From = "psplit", To = "p2" },
                new FlowEdge { From = "isplit", To = "a", Condition = "goA > 0" },
                new FlowEdge { From = "isplit", To = "d" },
                new FlowEdge { From = "a", To = "ijoin" }, new FlowEdge { From = "d", To = "ijoin" },
                new FlowEdge { From = "ijoin", To = "pjoin" },
                new FlowEdge { From = "p2", To = "pjoin" },
                new FlowEdge { From = "pjoin", To = "end" },
            },
        };
        db.Wf_FlowDefs.Add(new Wf_FlowDef { Id = Guid.NewGuid(), FlowKey = "mix", FlowName = "mix", FormKey = "f",
            SchemaJson = JsonSerializer.Serialize(schema), Version = 1, Enable = true });
        await db.SaveChangesAsync();
        await Engine(db).SubmitAsync("mix", Guid.NewGuid(), "{\"goA\":1}");

        // p2 先过 → 外层 pjoin 必须等 inclusive 子树（血缘感知）
        var tp = await db.Wf_FlowTasks.SingleAsync(t => t.AssigneeId == up && t.Status == FlowTaskStatus.Pending);
        await Engine(db).ActAsync(tp.Id, up, approve: true);
        Assert.Equal(FlowInstanceStatus.Running, (await db.Wf_FlowInstances.SingleAsync()).Status);

        var ta = await db.Wf_FlowTasks.SingleAsync(t => t.AssigneeId == ua && t.Status == FlowTaskStatus.Pending);
        await Engine(db).ActAsync(ta.Id, ua, approve: true);   // inclusive 活支只有 a → ijoin 放行 → pjoin 齐
        Assert.Equal(FlowInstanceStatus.Approved, (await db.Wf_FlowInstances.SingleAsync()).Status);
        Assert.False(await db.Wf_FlowTasks.AnyAsync(t => t.AssigneeId == ud));   // default 未走
    }

    // 嵌套反向：s → isplit → ( psplit⊂inclusive 支 → (p1,p2) → pj → ijoin , d[default] → ijoin ) → end
    [Fact]
    public async Task ParallelInsideInclusive_InclusiveJoinWaitsParallelSubtree()
    {
        using var db = NewDb();
        Guid u1 = Guid.NewGuid(), u2 = Guid.NewGuid(), ud = Guid.NewGuid();
        var schema = new FlowSchema
        {
            Start = "s",
            Nodes =
            {
                new FlowNode { Id = "s", Type = "start" },
                new FlowNode { Id = "isplit", Type = "inclusiveSplit" },
                new FlowNode { Id = "psplit", Type = "parallelSplit" },
                new FlowNode { Id = "p1", Type = "approval", ApproverStrategy = "Specified", ApproverUserId = u1 },
                new FlowNode { Id = "p2", Type = "approval", ApproverStrategy = "Specified", ApproverUserId = u2 },
                new FlowNode { Id = "pj", Type = "parallelJoin" },
                new FlowNode { Id = "d", Type = "approval", ApproverStrategy = "Specified", ApproverUserId = ud },
                new FlowNode { Id = "ijoin", Type = "inclusiveJoin" },
                new FlowNode { Id = "end", Type = "end" },
            },
            Edges =
            {
                new FlowEdge { From = "s", To = "isplit" },
                new FlowEdge { From = "isplit", To = "psplit", Condition = "goP > 0" },
                new FlowEdge { From = "isplit", To = "d" },
                new FlowEdge { From = "psplit", To = "p1" }, new FlowEdge { From = "psplit", To = "p2" },
                new FlowEdge { From = "p1", To = "pj" }, new FlowEdge { From = "p2", To = "pj" },
                new FlowEdge { From = "pj", To = "ijoin" },
                new FlowEdge { From = "d", To = "ijoin" },
                new FlowEdge { From = "ijoin", To = "end" },
            },
        };
        db.Wf_FlowDefs.Add(new Wf_FlowDef { Id = Guid.NewGuid(), FlowKey = "pin", FlowName = "pin", FormKey = "f",
            SchemaJson = JsonSerializer.Serialize(schema), Version = 1, Enable = true });
        await db.SaveChangesAsync();
        await Engine(db).SubmitAsync("pin", Guid.NewGuid(), "{\"goP\":1}");   // 真边=psplit 支，default 不走

        Assert.False(await db.Wf_FlowTasks.AnyAsync(t => t.AssigneeId == ud));
        var t1 = await db.Wf_FlowTasks.SingleAsync(t => t.AssigneeId == u1 && t.Status == FlowTaskStatus.Pending);
        await Engine(db).ActAsync(t1.Id, u1, approve: true);   // p1 过 → pj 停泊；ijoin 活支子树在途，必须等
        Assert.Equal(FlowInstanceStatus.Running, (await db.Wf_FlowInstances.SingleAsync()).Status);

        var t2 = await db.Wf_FlowTasks.SingleAsync(t => t.AssigneeId == u2 && t.Status == FlowTaskStatus.Pending);
        await Engine(db).ActAsync(t2.Id, u2, approve: true);   // pj 齐 → 上弹 → ijoin 齐（活支==1）→ end
        Assert.Equal(FlowInstanceStatus.Approved, (await db.Wf_FlowInstances.SingleAsync()).Status);
        Assert.False(await db.Wf_FlowTokens.AnyAsync(t => t.Status == FlowTokenStatus.Active));
    }
}
```

- [ ] **Step 2: 跑验证 FAIL** — `--filter InclusiveGatewayTests`。预期「未知节点类型：inclusiveSplit」异常（`EnterNodeAsync :273` 抛）。

- [ ] **Step 3: 实现** — `InclusiveSplitNodeHandler.cs` 全文：

```csharp
using CP6.Entity.DomainModels.Wf;

namespace CP6.Core.Services.Wf;

/// <summary>包容分叉网关（hardening spec §3.1，第 7 个 handler）：对全部条件出边求值取真边集 T；
/// T 非空 → 激活 T（default 不走）；T 空 → 激活唯一 default 兜底边。每激活边各生一枚同 ForkId 子 token
/// （与 parallelSplit 完全相同的血缘机制）。default 边 = 唯一无条件出边（E-WF-020 校验保证存在且唯一），
/// 不是恒真必走边。激活集为空 = 校验漏网属 bug → 抛引擎异常，不静默。</summary>
internal sealed class InclusiveSplitNodeHandler : INodeHandler
{
    public string Type => "inclusiveSplit";

    public async Task OnEnterAsync(NodeContext ctx)
    {
        var eng = ctx.Engine; var inst = ctx.Inst; var schema = ctx.Schema; var node = ctx.Node;
        eng.ConsumeToken(ctx.Token);                       // 入 token 退场

        var outs = schema.Edges.Where(e => e.From == node.Id && e.IsError != true).ToList();
        var condEdges = outs.Where(e => !string.IsNullOrWhiteSpace(e.Condition)).ToList();
        var defaults  = outs.Where(e =>  string.IsNullOrWhiteSpace(e.Condition)).ToList();

        // 注意不能对全边直接调 Evaluate：空表达式在 ExpressionEvaluator 里恒真，必须先分组
        var truthy = condEdges.Where(e => ExpressionEvaluator.Evaluate(e.Condition, inst.VarsJson)).ToList();
        var active = truthy.Count > 0 ? truthy : defaults.Take(1).ToList();

        var forkId = Guid.NewGuid();
        var activated = new List<string>();
        foreach (var edge in active)
        {
            var target = FlowEngine.FindNode(schema, edge.To);
            if (target is null) continue;
            var child = eng.SpawnToken(inst, target, parent: ctx.Token.Id, fork: forkId);
            activated.Add(edge.To);
            await eng.EnterNodeAsync(inst, schema, target, child);
        }
        if (activated.Count == 0)   // 防御式兜底：校验漏网（无 default 且全假 / 激活边目标缺失）
            throw new InvalidOperationException($"E-WF-020: inclusiveSplit {node.Id} 无可激活出边");
        eng.AddHistory(inst.Id, node.Id, inst.StarterId, "inclusiveSplit",
            "activated: " + string.Join(",", activated));
    }
}
```

`InclusiveJoinNodeHandler.cs` 全文：

```csharp
using CP6.Entity.DomainModels.Wf;

namespace CP6.Core.Services.Wf;

/// <summary>包容汇聚网关（hardening spec §3.2，第 8 个 handler）：与 parallelJoin 同构，共用
/// <see cref="GatewayJoinHelper"/> 动态计票（活支==实际激活边数，只等真走的分支——inclusive join 标准解）。
/// D3：独立节点类型，不与 parallelJoin 合并。</summary>
internal sealed class InclusiveJoinNodeHandler : INodeHandler
{
    public string Type => "inclusiveJoin";
    public Task OnEnterAsync(NodeContext ctx) => GatewayJoinHelper.TryReleaseAsync(ctx, "inclusiveJoin");
}
```

`FlowEngine.cs` `DefaultHandlers()`（:38-43）改为（注释同步）：

```csharp
    // ★ T5：start/approval/end + parallelSplit/parallelJoin；A-T6：serviceTask；
    // ★ hardening A-T3：inclusiveSplit/inclusiveJoin（第 7/8 个，spec D3）。
    private static IEnumerable<INodeHandler> DefaultHandlers() => new INodeHandler[]
    {
        new StartNodeHandler(), new ApprovalNodeHandler(), new EndNodeHandler(),
        new ParallelSplitNodeHandler(), new ParallelJoinNodeHandler(),
        new ServiceTaskNodeHandler(Array.Empty<IServiceTaskExecutor>()),
        new InclusiveSplitNodeHandler(), new InclusiveJoinNodeHandler(),
    };
```

`Program.cs` :114（ServiceTaskNodeHandler 注册行）后加：

```csharp
builder.Services.AddScoped<CP6.Core.Services.Wf.INodeHandler, CP6.Core.Services.Wf.InclusiveSplitNodeHandler>();  // hardening A-T3 节点处理器：包容分叉
builder.Services.AddScoped<CP6.Core.Services.Wf.INodeHandler, CP6.Core.Services.Wf.InclusiveJoinNodeHandler>();   // hardening A-T3 节点处理器：包容汇聚
```

- [ ] **Step 4: 跑验证 PASS** — `--filter InclusiveGatewayTests` 全绿；`dotnet build CP6.WebApi/CP6.WebApi.csproj` 过。
- [ ] **Step 5: Wf 闸 + commit**

```bash
dotnet test CP6.Tests/CP6.Tests.csproj --filter Wf
git add -A && git commit -m "feat(wfs-hardening): A-T3 InclusiveSplit/Join两handler+DefaultHandlers/DI注册(字典第7/8个)"
```

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

## Wave H-B — 驳回剪枝（依赖 H-A 全部；与 H-C 互不依赖可并行）

### Task B-T1: 剪枝通知契约（WfNotificationType.BranchPruned + IWfNotifier.BranchPrunedAsync × 4 实现）

**Files:**
- Modify: `CP6.Entity/DomainModels/Wf/WfNotificationType.cs`
- Modify: `CP6.Core/Services/Wf/IWfNotifier.cs`（接口 + NullWfNotifier）
- Modify: `CP6.WebApi/Services/SignalRWfNotifier.cs`（no-op 补全）
- Modify: `CP6.WebApi/Services/PersistentWfNotifier.cs`（三渠道实现）
- Modify: `CP6.Tests/Oa/NotificationEngineHookTests.cs`（CountingNotifier 补方法 + 计数，**不改既有断言**）

- [ ] **Step 1: 写失败测试** — `NotificationEngineHookTests.CountingNotifier` 加成员（供 B-T2 引擎测试复用；本 Task 先以常量测试落锚）：

```csharp
// CountingNotifier 内追加：
        public int PrunedCount { get; private set; }
        public string? LastPrunedNodeId { get; private set; }

        public Task BranchPrunedAsync(Guid starterId, Guid instanceId, string flowKey, string nodeId, string? comment)
        {
            PrunedCount++;
            LastPrunedNodeId = nodeId;
            return Task.CompletedTask;
        }
```

新增测试（放 `NotificationEngineHookTests` 类尾）：

```csharp
    [Fact]
    public void WfNotificationType_BranchPruned_Is5()
        => Assert.Equal(5, CP6.Entity.DomainModels.Wf.WfNotificationType.BranchPruned);
```

- [ ] **Step 2: 跑验证 FAIL** — `--filter NotificationEngineHookTests`（接口无该方法 → 编译失败）。

- [ ] **Step 3: 实现**

`WfNotificationType.cs` 加：

```csharp
    /// <summary>分支被剪枝（内核 hardening）→ 推送给发起人。独立类型键：偏好矩阵须与驳回可独立开关（信箱 spec §2.1 联动口径）。</summary>
    public const int BranchPruned = 5;
```

`IWfNotifier.cs` 接口加方法 + `NullWfNotifier` 加空实现：

```csharp
    /// <summary>并行/包容分支被剪枝（不连坐）→ 推送给发起人（内核 hardening spec §4.2.2）。</summary>
    Task BranchPrunedAsync(Guid starterId, Guid instanceId, string flowKey, string nodeId, string? comment);
```

```csharp
    public Task BranchPrunedAsync(Guid starterId, Guid instanceId, string flowKey, string nodeId, string? comment) => Task.CompletedTask;
```

`SignalRWfNotifier.cs` 加（与其 FlowApproved/FlowRejected 同款 no-op，注释指明由 PersistentWfNotifier 承载）：

```csharp
    public Task BranchPrunedAsync(Guid starterId, Guid instanceId, string flowKey, string nodeId, string? comment) => Task.CompletedTask;
```

`PersistentWfNotifier.cs` 仿 `FlowRejectedAsync` 三渠道（**v1 不查偏好开关**——`NotificationPrefs` 无 BranchPruned 键，等价信箱 spec「缺键默认 true」；信箱 spec 落地时统一接管偏好矩阵）：

```csharp
    // ── BranchPrunedAsync（内核 hardening）────────────────────────────────

    /// <inheritdoc />
    public async Task BranchPrunedAsync(Guid starterId, Guid instanceId, string flowKey, string nodeId, string? comment)
    {
        // 偏好：BranchPruned 是新类型键，现行 NotificationPrefs 无对应字段 → 缺键默认 true（信箱 spec §2.1 三态坍缩口径），
        // 本方法 v1 不做偏好门控；偏好矩阵化由信箱 spec 落地时统一改造。
        const string title = "您的申请有分支被驳回（其余分支继续）";
        var body = string.IsNullOrWhiteSpace(comment)
            ? $"流程 {flowKey} 的分支 {nodeId} 被驳回剪除，其余分支继续审批"
            : $"流程 {flowKey} 的分支 {nodeId} 被驳回剪除（{comment}），其余分支继续审批";

        await _notif.CreateAsync(
            starterId, WfNotificationType.BranchPruned,
            title, body, instanceId, taskId: null, flowKey);

        try
        {
            await _hub.Clients.All.SendAsync("WfNotification", new
            {
                type       = WfNotificationType.BranchPruned,
                userId     = starterId,
                instanceId,
                taskId     = (Guid?)null,
                flowKey
            });
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex,
                "SignalR WfNotification(BranchPruned) 失败，忽略（用户 {UserId}）", starterId);
        }

        var prefs = await _pref.GetNotifyPrefsAsync(starterId);
        if (prefs.Email)
            await TrySendEmailAsync(starterId, title, body);
    }
```

- [ ] **Step 4: 跑验证 PASS** — `--filter NotificationEngineHookTests` 全绿（既有断言零改）；`dotnet build CP6.WebApi/CP6.WebApi.csproj` 过。
- [ ] **Step 5: 全量闸 + commit**

```bash
dotnet test CP6.Tests/CP6.Tests.csproj
git add -A && git commit -m "feat(wfs-hardening): B-T1 BranchPruned通知契约(类型5+IWfNotifier方法+四实现三渠道)"
```

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

### Task B-T3: 全剪光嵌套递归上弹（内层全剪 × 外层 prune/cascade 两态）

> 依赖 B-T2。递归代码已在 B-T2 落地，本 Task 用嵌套矩阵测试锁定其正确性（TDD：若测试暴露递归缺陷，在此修）。

**Files:**
- Test: `CP6.Tests/Wf/BranchPruneNestedTests.cs`
- （仅当测试暴露缺陷时）Modify: `CP6.Core/Services/Wf/FlowEngine.Prune.cs`

- [ ] **Step 1: 写测试**

```csharp
// CP6.Tests/Wf/BranchPruneNestedTests.cs
using System.Text.Json;
using CP6.Core.EFDbContext;
using CP6.Core.Services.Wf;
using CP6.Entity.DomainModels.Wf;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace CP6.Tests.Wf;

/// <summary>嵌套剪枝递归上弹矩阵（hardening spec §4.2.4/§9）：内层全剪光 × 外层 prune/cascade 两态。</summary>
public class BranchPruneNestedTests
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
    private static FlowEngine Engine(CP6Context db, IWfNotifier n) => new(db, new ApproverResolver(db), n);

    // s → outer[外层策略] → ( inner[prune] → (x1,x2) → ij , b ) → oj → end
    private static FlowSchema Nested(Guid u1, Guid u2, Guid ub, string? outerPolicy) => new()
    {
        Start = "s",
        Nodes =
        {
            new FlowNode { Id = "s", Type = "start" },
            new FlowNode { Id = "outer", Type = "parallelSplit", OnBranchReject = outerPolicy },
            new FlowNode { Id = "inner", Type = "parallelSplit", OnBranchReject = "prune" },
            new FlowNode { Id = "x1", Type = "approval", ApproverStrategy = "Specified", ApproverUserId = u1 },
            new FlowNode { Id = "x2", Type = "approval", ApproverStrategy = "Specified", ApproverUserId = u2 },
            new FlowNode { Id = "ij", Type = "parallelJoin" },
            new FlowNode { Id = "b", Type = "approval", ApproverStrategy = "Specified", ApproverUserId = ub },
            new FlowNode { Id = "oj", Type = "parallelJoin" },
            new FlowNode { Id = "end", Type = "end" },
        },
        Edges =
        {
            new FlowEdge { From = "s", To = "outer" },
            new FlowEdge { From = "outer", To = "inner" }, new FlowEdge { From = "outer", To = "b" },
            new FlowEdge { From = "inner", To = "x1" }, new FlowEdge { From = "inner", To = "x2" },
            new FlowEdge { From = "x1", To = "ij" }, new FlowEdge { From = "x2", To = "ij" },
            new FlowEdge { From = "ij", To = "oj" },
            new FlowEdge { From = "b", To = "oj" },
            new FlowEdge { From = "oj", To = "end" },
        },
    };

    private static async Task SeedAsync(CP6Context db, Guid u1, Guid u2, Guid ub, string? outerPolicy)
    {
        db.Wf_FlowDefs.Add(new Wf_FlowDef { Id = Guid.NewGuid(), FlowKey = "nstpr", FlowName = "x", FormKey = "f",
            SchemaJson = JsonSerializer.Serialize(Nested(u1, u2, ub, outerPolicy)), Version = 1, Enable = true });
        await db.SaveChangesAsync();
    }

    [Fact]
    public async Task InnerAllPruned_OuterPrune_PrunesOuterBranch_SiblingCompletes()
    {
        using var db = NewDb();
        var n = new CountingPruneNotifier();
        Guid u1 = Guid.NewGuid(), u2 = Guid.NewGuid(), ub = Guid.NewGuid();
        await SeedAsync(db, u1, u2, ub, "prune");
        await Engine(db, n).SubmitAsync("nstpr", Guid.NewGuid(), "{}");

        var t1 = await db.Wf_FlowTasks.SingleAsync(t => t.AssigneeId == u1 && t.Status == FlowTaskStatus.Pending);
        await Engine(db, n).ActAsync(t1.Id, u1, approve: false);
        Assert.Equal(FlowInstanceStatus.Running, (await db.Wf_FlowInstances.SingleAsync()).Status);
        Assert.Equal(1, n.PrunedCount);                                    // 只剪 x1，未上弹

        var t2 = await db.Wf_FlowTasks.SingleAsync(t => t.AssigneeId == u2 && t.Status == FlowTaskStatus.Pending);
        await Engine(db, n).ActAsync(t2.Id, u2, approve: false);           // 内层全剪光 → 上弹外层（prune）剪外层该支

        var inst = await db.Wf_FlowInstances.SingleAsync();
        Assert.Equal(FlowInstanceStatus.Running, inst.Status);             // ★ 外层 prune：实例不死
        Assert.Equal(3, n.PrunedCount);                                    // x1 + x2 + 外层 inner 支（递归层记痕）
        Assert.Equal(3, await db.Wf_FlowHistories.CountAsync(h => h.Action == "branchPruned"));
        Assert.True(await db.Wf_FlowTasks.AnyAsync(t => t.AssigneeId == ub && t.Status == FlowTaskStatus.Pending)); // b 支不倒

        var tb = await db.Wf_FlowTasks.SingleAsync(t => t.AssigneeId == ub && t.Status == FlowTaskStatus.Pending);
        await Engine(db, n).ActAsync(tb.Id, ub, approve: true);            // b 过 → 外 join 只等活支 → end
        Assert.Equal(FlowInstanceStatus.Approved, (await db.Wf_FlowInstances.SingleAsync()).Status);
        Assert.Equal(0, n.RejectedCount);
    }

    [Fact]
    public async Task InnerAllPruned_OuterCascade_InstanceRejected()
    {
        using var db = NewDb();
        var n = new CountingPruneNotifier();
        Guid u1 = Guid.NewGuid(), u2 = Guid.NewGuid(), ub = Guid.NewGuid();
        await SeedAsync(db, u1, u2, ub, null);                             // 外层未配置 = cascade
        await Engine(db, n).SubmitAsync("nstpr", Guid.NewGuid(), "{}");

        var t1 = await db.Wf_FlowTasks.SingleAsync(t => t.AssigneeId == u1 && t.Status == FlowTaskStatus.Pending);
        await Engine(db, n).ActAsync(t1.Id, u1, approve: false);
        var t2 = await db.Wf_FlowTasks.SingleAsync(t => t.AssigneeId == u2 && t.Status == FlowTaskStatus.Pending);
        await Engine(db, n).ActAsync(t2.Id, u2, approve: false);           // 内层全剪光 → 上弹外层（cascade）→ 实例 Rejected

        var inst = await db.Wf_FlowInstances.SingleAsync();
        Assert.Equal(FlowInstanceStatus.Rejected, inst.Status);            // ★ 外层 cascade：整单驳回
        Assert.Equal(1, n.RejectedCount);
        // b 支被连坐清场：任务 Cancelled、token Cancelled、Pending 履历 Voided
        Assert.False(await db.Wf_FlowTasks.AnyAsync(t => t.AssigneeId == ub && t.Status == FlowTaskStatus.Pending));
        Assert.False(await db.Wf_FlowTokens.AnyAsync(t => t.Status == FlowTokenStatus.Active));
        Assert.False(await db.Wf_FlowFormTos.AnyAsync(f => f.Status == FlowFormToStatus.Pending));
    }

    [Fact]
    public async Task InnerOnePruned_OneApproved_InnerJoinBackfills_OuterContinues()
    {
        // 内层剪一支 + 另一支已到场 ij 停泊 → 补放行上弹 → 外层等 b（补放行与递归的边界回归）
        using var db = NewDb();
        var n = new CountingPruneNotifier();
        Guid u1 = Guid.NewGuid(), u2 = Guid.NewGuid(), ub = Guid.NewGuid();
        await SeedAsync(db, u1, u2, ub, "prune");
        await Engine(db, n).SubmitAsync("nstpr", Guid.NewGuid(), "{}");

        var t1 = await db.Wf_FlowTasks.SingleAsync(t => t.AssigneeId == u1 && t.Status == FlowTaskStatus.Pending);
        await Engine(db, n).ActAsync(t1.Id, u1, approve: true);            // x1 过 → 到场 ij 停泊
        var t2 = await db.Wf_FlowTasks.SingleAsync(t => t.AssigneeId == u2 && t.Status == FlowTaskStatus.Pending);
        await Engine(db, n).ActAsync(t2.Id, u2, approve: false);           // x2 剪 → ij 补放行 → 上弹外层等 b

        Assert.Equal(FlowInstanceStatus.Running, (await db.Wf_FlowInstances.SingleAsync()).Status);
        Assert.Equal(1, n.PrunedCount);                                    // 只剪 x2，无递归记痕

        var tb = await db.Wf_FlowTasks.SingleAsync(t => t.AssigneeId == ub && t.Status == FlowTaskStatus.Pending);
        await Engine(db, n).ActAsync(tb.Id, ub, approve: true);
        Assert.Equal(FlowInstanceStatus.Approved, (await db.Wf_FlowInstances.SingleAsync()).Status);
    }
}
```

- [ ] **Step 2: 跑验证** — `--filter BranchPruneNestedTests`。若递归实现（B-T2）有缺陷在此修至全绿（修改仅限 `FlowEngine.Prune.cs`）。
- [ ] **Step 3: Wf 闸 + commit**

```bash
dotnet test CP6.Tests/CP6.Tests.csproj --filter Wf
git add -A && git commit -m "test(wfs-hardening): B-T3 嵌套全剪光递归上弹矩阵(外层prune/cascade两态+补放行边界)"
```

---

## Wave H-C — 退回三规则（依赖 H-A；与 H-B 并行）

### Task C-T1: SendBackScopeAnalyzer 作用域纯函数

**Files:**
- Create: `CP6.Core/Services/Wf/SendBackScopeAnalyzer.cs`
- Test: `CP6.Tests/Wf/SendBackScopeTests.cs`

- [ ] **Step 1: 写失败测试**

```csharp
// CP6.Tests/Wf/SendBackScopeTests.cs
using CP6.Core.Services.Wf;
using CP6.Entity.DomainModels.Wf;

namespace CP6.Tests.Wf;

/// <summary>退回作用域纯函数（hardening spec §5.1）。schema + 内存 token 直测，不动库。</summary>
public class SendBackScopeTests
{
    private static readonly Guid InstId = Guid.NewGuid();

    private static Wf_FlowToken Tok(string node, Guid? parent = null, Guid? fork = null,
        int status = FlowTokenStatus.Active)
        => new() { Id = Guid.NewGuid(), InstanceId = InstId, NodeId = node,
                   ParentTokenId = parent, ForkId = fork, Status = status };

    // s → n0 → split → ( a1 → a2 , b1 ) → join → end
    private static FlowSchema ParallelSchema() => new()
    {
        Start = "s",
        Nodes =
        {
            new FlowNode { Id = "s", Type = "start" },
            new FlowNode { Id = "n0", Type = "approval", ApproverStrategy = "Specified", ApproverUserId = Guid.NewGuid() },
            new FlowNode { Id = "split", Type = "parallelSplit" },
            new FlowNode { Id = "a1", Type = "approval", ApproverStrategy = "Specified", ApproverUserId = Guid.NewGuid() },
            new FlowNode { Id = "a2", Type = "approval", ApproverStrategy = "Specified", ApproverUserId = Guid.NewGuid() },
            new FlowNode { Id = "b1", Type = "approval", ApproverStrategy = "Specified", ApproverUserId = Guid.NewGuid() },
            new FlowNode { Id = "join", Type = "parallelJoin" },
            new FlowNode { Id = "end", Type = "end" },
        },
        Edges =
        {
            new FlowEdge { From = "s", To = "n0" }, new FlowEdge { From = "n0", To = "split" },
            new FlowEdge { From = "split", To = "a1" }, new FlowEdge { From = "a1", To = "a2" },
            new FlowEdge { From = "split", To = "b1" },
            new FlowEdge { From = "a2", To = "join" }, new FlowEdge { From = "b1", To = "join" },
            new FlowEdge { From = "join", To = "end" },
        },
    };

    [Fact]
    public void LinearToken_NoForkStack_BeforeSplit()
    {
        var schema = ParallelSchema();
        var root = Tok("n0");
        var (scope, strip) = SendBackScopeAnalyzer.Analyze(schema, new[] { root }, root, "n0", "s");
        Assert.Equal(SendBackScope.BeforeSplit, scope);
        Assert.Null(strip);
    }

    [Fact]
    public void SameBranch_TargetUpstreamInOwnBranch()
    {
        var schema = ParallelSchema();
        var f = Guid.NewGuid();
        var root = Tok("split", status: FlowTokenStatus.Consumed);
        var a = Tok("a2", parent: root.Id, fork: f);
        var b = Tok("b1", parent: root.Id, fork: f);
        var all = new[] { root, a, b };

        var (scope, strip) = SendBackScopeAnalyzer.Analyze(schema, all, a, "a2", "a1");
        Assert.Equal(SendBackScope.SameBranch, scope);
        Assert.Equal(a.Id, strip!.Id);                                     // 剥离层 = 本层分支代表 token
    }

    [Fact]
    public void BeforeSplit_TargetUpstreamOfSplit()
    {
        var schema = ParallelSchema();
        var f = Guid.NewGuid();
        var root = Tok("split", status: FlowTokenStatus.Consumed);
        var a = Tok("a2", parent: root.Id, fork: f);
        var all = new[] { root, a };

        var (scope, strip) = SendBackScopeAnalyzer.Analyze(schema, all, a, "a2", "n0");
        Assert.Equal(SendBackScope.BeforeSplit, scope);
        Assert.Null(strip);
    }

    [Fact]
    public void SiblingBranch_TargetInSiblingDomain()
    {
        var schema = ParallelSchema();
        var f = Guid.NewGuid();
        var root = Tok("split", status: FlowTokenStatus.Consumed);
        var a = Tok("a2", parent: root.Id, fork: f);
        var all = new[] { root, a };

        var (scope, _) = SendBackScopeAnalyzer.Analyze(schema, all, a, "a2", "b1");
        Assert.Equal(SendBackScope.SiblingBranch, scope);
    }

    [Fact]
    public void Nested_TargetBetweenOuterSplitAndInnerSplit_StripIsOuterLayer()
    {
        // s → outer → ( h1 → inner → (x1,x2) → ij , b1 ) → oj → end；current 在 x1，target = h1（内层 split 之前、外层支内）
        var schema = new FlowSchema
        {
            Start = "s",
            Nodes =
            {
                new FlowNode { Id = "s", Type = "start" },
                new FlowNode { Id = "outer", Type = "parallelSplit" },
                new FlowNode { Id = "h1", Type = "approval", ApproverStrategy = "Specified", ApproverUserId = Guid.NewGuid() },
                new FlowNode { Id = "inner", Type = "parallelSplit" },
                new FlowNode { Id = "x1", Type = "approval", ApproverStrategy = "Specified", ApproverUserId = Guid.NewGuid() },
                new FlowNode { Id = "x2", Type = "approval", ApproverStrategy = "Specified", ApproverUserId = Guid.NewGuid() },
                new FlowNode { Id = "ij", Type = "parallelJoin" },
                new FlowNode { Id = "b1", Type = "approval", ApproverStrategy = "Specified", ApproverUserId = Guid.NewGuid() },
                new FlowNode { Id = "oj", Type = "parallelJoin" },
                new FlowNode { Id = "end", Type = "end" },
            },
            Edges =
            {
                new FlowEdge { From = "s", To = "outer" },
                new FlowEdge { From = "outer", To = "h1" }, new FlowEdge { From = "h1", To = "inner" },
                new FlowEdge { From = "inner", To = "x1" }, new FlowEdge { From = "inner", To = "x2" },
                new FlowEdge { From = "x1", To = "ij" }, new FlowEdge { From = "x2", To = "ij" },
                new FlowEdge { From = "ij", To = "oj" },
                new FlowEdge { From = "outer", To = "b1" }, new FlowEdge { From = "b1", To = "oj" },
                new FlowEdge { From = "oj", To = "end" },
            },
        };
        var fo = Guid.NewGuid(); var fi = Guid.NewGuid();
        var root = Tok("outer", status: FlowTokenStatus.Consumed);
        var h = Tok("inner", parent: root.Id, fork: fo, status: FlowTokenStatus.Consumed);  // 外层支代表，已进内层 split
        var x = Tok("x1", parent: h.Id, fork: fi);
        var all = new[] { root, h, x };

        var (scope, strip) = SendBackScopeAnalyzer.Analyze(schema, all, x, "x1", "h1");
        Assert.Equal(SendBackScope.SameBranch, scope);
        Assert.Equal(h.Id, strip!.Id);                                     // ★ 剥离层是外层支代表 token（spec §5.2 对称规则）

        // 而 target 在内层同支内时剥离层是内层
        var (scope2, strip2) = SendBackScopeAnalyzer.Analyze(schema, all, x, "x1", "x1");
        // 自退回在调用方被 E-WF-012 拦，这里直接验证内层域命中路径：target=x1 属 x 的最内层域
        Assert.Equal(SendBackScope.SameBranch, scope2);
        Assert.Equal(x.Id, strip2!.Id);
    }

    [Fact]
    public void UnresolvablePairing_Throws_E_WF_012()
    {
        // split 无公共 join（一支直通 end）→ 结构不可判定 → 保守拒绝
        var schema = new FlowSchema
        {
            Start = "s",
            Nodes =
            {
                new FlowNode { Id = "s", Type = "start" },
                new FlowNode { Id = "split", Type = "parallelSplit" },
                new FlowNode { Id = "a", Type = "approval", ApproverStrategy = "Specified", ApproverUserId = Guid.NewGuid() },
                new FlowNode { Id = "b", Type = "approval", ApproverStrategy = "Specified", ApproverUserId = Guid.NewGuid() },
                new FlowNode { Id = "end", Type = "end" },
            },
            Edges =
            {
                new FlowEdge { From = "s", To = "split" },
                new FlowEdge { From = "split", To = "a" }, new FlowEdge { From = "split", To = "b" },
                new FlowEdge { From = "a", To = "end" }, new FlowEdge { From = "b", To = "end" },
            },
        };
        var f = Guid.NewGuid();
        var root = Tok("split", status: FlowTokenStatus.Consumed);
        var a = Tok("a", parent: root.Id, fork: f);
        var ex = Assert.Throws<InvalidOperationException>(
            () => SendBackScopeAnalyzer.Analyze(schema, new[] { root, a }, a, "a", "s"));
        Assert.Contains("E-WF-012", ex.Message);
    }
}
```

- [ ] **Step 2: 跑验证 FAIL** — `--filter SendBackScopeTests`（类型不存在，编译失败）。

- [ ] **Step 3: 实现** — `SendBackScopeAnalyzer.cs` 全文：

```csharp
using CP6.Entity.DomainModels.Wf;

namespace CP6.Core.Services.Wf;

/// <summary>退回作用域（hardening spec §5.1 三分）。</summary>
public enum SendBackScope
{
    /// <summary>目标在当前分支可达域内 → 只剥离本支（剥离层子树清场 + 血缘保留重生）。</summary>
    SameBranch,
    /// <summary>目标在 fork 栈全部 split 之前（含线性流无 fork）→ 既有全清场整块重来。</summary>
    BeforeSplit,
    /// <summary>目标在兄弟分支域内 → 拒绝（E-WF-019，语义永久禁止）。</summary>
    SiblingBranch,
}

/// <summary>退回作用域判定（纯函数，动手清场前调用）。§5.3 定案：
/// fork 栈由 <see cref="TokenLineage.ForkStack"/> 血缘上溯（与剪枝共用口径）；
/// 分支域由 <see cref="FlowGraph.BranchDomain"/>（配对 join = <see cref="FlowGraph.NearestCommonJoin"/>，
/// 与校验 E-WF-021 共用口径）。逐层内→外：目标与当前节点同域 → SameBranch（首个命中层即
/// 「包含目标节点的最内层分支域」，剥离层 = 该层分支代表 token）；目标只在兄弟域 → SiblingBranch；
/// 全层不命中 → BeforeSplit。配对不可判定（无公共 join，环路/直通 end 的怪异 schema）→ 抛 E-WF-012 保守拒绝
/// （现状对跨网关退回本来就拒，非收紧）。</summary>
public static class SendBackScopeAnalyzer
{
    public static (SendBackScope Scope, Wf_FlowToken? StripToken) Analyze(
        FlowSchema schema, IReadOnlyList<Wf_FlowToken> all, Wf_FlowToken current,
        string currentNodeId, string targetNodeId)
    {
        foreach (var (branchToken, _, splitNodeId) in TokenLineage.ForkStack(all, current))
        {
            var split = schema.Nodes.FirstOrDefault(n => n.Id == splitNodeId)
                        ?? throw new InvalidOperationException("E-WF-012");
            var join = FlowGraph.NearestCommonJoin(schema, split)
                       ?? throw new InvalidOperationException("E-WF-012");
            var domains = schema.Edges.Where(e => e.From == split.Id && e.IsError != true)
                .Select(e => FlowGraph.BranchDomain(schema, e.To, join.Id)).ToList();

            var mine = domains.Where(d => d.Contains(currentNodeId)).ToList();
            if (mine.Any(d => d.Contains(targetNodeId)))
                return (SendBackScope.SameBranch, branchToken);
            if (domains.Any(d => d.Contains(targetNodeId)))
                return (SendBackScope.SiblingBranch, null);
            // 目标在本层块外 → 上探外层
        }
        return (SendBackScope.BeforeSplit, null);
    }
}
```

- [ ] **Step 4: 跑验证 PASS** — `--filter SendBackScopeTests` 全绿。
- [ ] **Step 5: Wf 闸 + commit**

```bash
dotnet test CP6.Tests/CP6.Tests.csproj --filter Wf
git add -A && git commit -m "feat(wfs-hardening): C-T1 SendBackScopeAnalyzer作用域纯函数(三分+最内剥离层+E-WF-012保守拒)"
```

---

### Task C-T2: CancelTokenSubtree 子树清场

**Files:**
- Modify: `CP6.Core/Services/Wf/FlowEngine.Tokens.cs`（加 `CancelTokenSubtree` + `CancelPendingServiceJobsOfToken`；若 H-B 未先行，`CancelPendingTasksOfToken` 在本 Task 落地，签名同 B-T2）
- Test: `CP6.Tests/Wf/TokenSubtreeCancelTests.cs`

- [ ] **Step 1: 写失败测试**

```csharp
// CP6.Tests/Wf/TokenSubtreeCancelTests.cs
using System.Text.Json;
using CP6.Core.EFDbContext;
using CP6.Core.Services.Wf;
using CP6.Entity.DomainModels.Wf;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace CP6.Tests.Wf;

/// <summary>剥离层子树清场（hardening spec §5.2）：只清子树、兄弟支零扰动。InternalsVisibleTo 直调引擎内部方法。</summary>
public class TokenSubtreeCancelTests
{
    private static CP6Context NewDb() => new(new DbContextOptionsBuilder<CP6Context>()
        .UseInMemoryDatabase(Guid.NewGuid().ToString())
        .ConfigureWarnings(w => w.Ignore(InMemoryEventId.TransactionIgnoredWarning)).Options);
    private static FlowEngine Engine(CP6Context db) => new(db, new ApproverResolver(db));

    // s → outer → ( inner → (x1,x2) → ij , b ) → oj → end（复用 B-T3 拓扑，无剪枝配置）
    private static FlowSchema Nested(Guid u1, Guid u2, Guid ub) => new()
    {
        Start = "s",
        Nodes =
        {
            new FlowNode { Id = "s", Type = "start" },
            new FlowNode { Id = "outer", Type = "parallelSplit" },
            new FlowNode { Id = "inner", Type = "parallelSplit" },
            new FlowNode { Id = "x1", Type = "approval", ApproverStrategy = "Specified", ApproverUserId = u1 },
            new FlowNode { Id = "x2", Type = "approval", ApproverStrategy = "Specified", ApproverUserId = u2 },
            new FlowNode { Id = "ij", Type = "parallelJoin" },
            new FlowNode { Id = "b", Type = "approval", ApproverStrategy = "Specified", ApproverUserId = ub },
            new FlowNode { Id = "oj", Type = "parallelJoin" },
            new FlowNode { Id = "end", Type = "end" },
        },
        Edges =
        {
            new FlowEdge { From = "s", To = "outer" },
            new FlowEdge { From = "outer", To = "inner" }, new FlowEdge { From = "outer", To = "b" },
            new FlowEdge { From = "inner", To = "x1" }, new FlowEdge { From = "inner", To = "x2" },
            new FlowEdge { From = "x1", To = "ij" }, new FlowEdge { From = "x2", To = "ij" },
            new FlowEdge { From = "ij", To = "oj" },
            new FlowEdge { From = "b", To = "oj" },
            new FlowEdge { From = "oj", To = "end" },
        },
    };

    [Fact]
    public async Task CancelSubtree_InnerForkKilled_SiblingBranchUntouched()
    {
        using var db = NewDb();
        Guid u1 = Guid.NewGuid(), u2 = Guid.NewGuid(), ub = Guid.NewGuid();
        db.Wf_FlowDefs.Add(new Wf_FlowDef { Id = Guid.NewGuid(), FlowKey = "sub", FlowName = "x", FormKey = "f",
            SchemaJson = JsonSerializer.Serialize(Nested(u1, u2, ub)), Version = 1, Enable = true });
        await db.SaveChangesAsync();
        var instId = await Engine(db).SubmitAsync("sub", Guid.NewGuid(), "{}");

        // 剥离层 = 外层「inner 支」代表 token：进了 inner split 的那枚（NodeId=="inner"，Consumed）
        var strip = await db.Wf_FlowTokens.SingleAsync(t => t.NodeId == "inner");
        var eng = Engine(db);
        eng.CancelTokenSubtree(instId, strip.Id);
        await db.SaveChangesAsync();

        // 子树内：x1/x2 token Cancelled、任务 Cancelled、Pending 履历 Voided
        Assert.False(await db.Wf_FlowTokens.AnyAsync(t =>
            (t.NodeId == "x1" || t.NodeId == "x2") && t.Status == FlowTokenStatus.Active));
        Assert.False(await db.Wf_FlowTasks.AnyAsync(t =>
            (t.AssigneeId == u1 || t.AssigneeId == u2) && t.Status == FlowTaskStatus.Pending));
        Assert.False(await db.Wf_FlowFormTos.AnyAsync(f =>
            (f.NodeId == "x1" || f.NodeId == "x2") && f.Status == FlowFormToStatus.Pending));

        // ★ 兄弟支 b 零扰动
        Assert.True(await db.Wf_FlowTokens.AnyAsync(t => t.NodeId == "b" && t.Status == FlowTokenStatus.Active));
        Assert.True(await db.Wf_FlowTasks.AnyAsync(t => t.AssigneeId == ub && t.Status == FlowTaskStatus.Pending));
        Assert.True(await db.Wf_FlowFormTos.AnyAsync(f => f.NodeId == "b" && f.Status == FlowFormToStatus.Pending));
        // 实例不被动状态（清场不改 inst.Status）
        Assert.Equal(FlowInstanceStatus.Running, (await db.Wf_FlowInstances.SingleAsync()).Status);
    }
}
```

- [ ] **Step 2: 跑验证 FAIL**（方法不存在，编译失败）。

- [ ] **Step 3: 实现** — `FlowEngine.Tokens.cs` 加：

```csharp
    /// <summary>剥离层子树清场（hardening spec §5.2 SameBranch）：root 及其 ParentTokenId 后代闭包内
    /// Active token → Cancelled；这些 token 的 Pending/Suspended 任务 → Cancelled、Pending FormTo → Voided、
    /// Pending ServiceJob → Cancelled。绝不触碰子树外（兄弟分支零扰动）、绝不改 inst.Status。
    /// 闭包正确性：join 续 token 血缘「上弹一层」重挂剥离层同级，故任何在途延续 token 要么在闭包内、
    /// 要么本身就是作用域分析选出的剥离层（侦察结论表第 3 行论证）。</summary>
    internal void CancelTokenSubtree(Guid instanceId, Guid rootTokenId)
    {
        var all = SnapshotTokens(instanceId);
        var subtree = new HashSet<Guid> { rootTokenId };
        bool grew = true;
        while (grew)
        {
            grew = false;
            foreach (var t in all)
                if (t.ParentTokenId is Guid p && subtree.Contains(p) && subtree.Add(t.Id)) grew = true;
        }
        foreach (var t in all)
            if (subtree.Contains(t.Id) && t.Status == FlowTokenStatus.Active)
                t.Status = FlowTokenStatus.Cancelled;
        foreach (var id in subtree)
        {
            CancelPendingTasksOfToken(instanceId, id);
            VoidPendingFormTos(instanceId, tokenId: id);
            CancelPendingServiceJobsOfToken(instanceId, id);
        }
    }

    /// <summary>本 token 的 Pending 服务作业 → Cancelled（镜像 CancelAllActiveTokens 的 B-T3 job 清场，tokenId 过滤）。</summary>
    internal void CancelPendingServiceJobsOfToken(Guid instanceId, Guid tokenId)
    {
        var now = DateTime.UtcNow;
        foreach (var j in _db.Wf_ServiceJobs.Local.Where(j => j.InstanceId == instanceId && j.TokenId == tokenId
            && j.Status == ServiceJobStatus.Pending).ToList())
        { j.Status = ServiceJobStatus.Cancelled; j.CompletedAtUtc = now; }
        var localJobIds = _db.Wf_ServiceJobs.Local.Where(j => j.InstanceId == instanceId).Select(j => j.Id).ToHashSet();
        foreach (var j in _db.Wf_ServiceJobs.Where(j => j.InstanceId == instanceId && j.TokenId == tokenId
            && j.Status == ServiceJobStatus.Pending && !localJobIds.Contains(j.Id)).ToList())
        { j.Status = ServiceJobStatus.Cancelled; j.CompletedAtUtc = now; }
    }
```

（若 H-C 先于 H-B 执行，`CancelPendingTasksOfToken` 按 B-T2 Step 3 的代码在本 Task 一并落地；两波都到时以先落者为准，签名一致。）

- [ ] **Step 4: 跑验证 PASS**。
- [ ] **Step 5: Wf 闸 + commit**

```bash
dotnet test CP6.Tests/CP6.Tests.csproj --filter Wf
git add -A && git commit -m "feat(wfs-hardening): C-T2 CancelTokenSubtree剥离层子树清场(token+任务+履历+服务作业,兄弟零扰动)"
```

### Task C-T3: SendBackToNodeAsync 三规则接线 + E-WF-019 + 集成矩阵

> 依赖 C-T1/C-T2（若与 H-B 并行，注意 `FlowEngine.Tokens.cs` 的 `CancelPendingTasksOfToken` 合并冲突——签名一致，取一份即可）。

**Files:**
- Modify: `CP6.Core/Services/Wf/AdvancedFlow.cs`（`SendBackToNodeAsync` :124-143 重写；删 `CrossesParallelBlock` :196-202 与 `NodesBetween` :204-210）
- Test: `CP6.Tests/Wf/SendBackThreeRuleTests.cs`

- [ ] **Step 1: 写失败测试**

```csharp
// CP6.Tests/Wf/SendBackThreeRuleTests.cs
using System.Text.Json;
using CP6.Core.EFDbContext;
using CP6.Core.Services.Wf;
using CP6.Entity.DomainModels.Wf;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace CP6.Tests.Wf;

/// <summary>退回三规则 × 网关类型 × 三目标矩阵（hardening spec §5.2/§9）。
/// 线性流零 diff 由既有 AdvancedFlowTests/SerialSendBackTests 锁定（断言零改），本文件只测网关场景。</summary>
public class SendBackThreeRuleTests
{
    private static CP6Context NewDb() => new(new DbContextOptionsBuilder<CP6Context>()
        .UseInMemoryDatabase(Guid.NewGuid().ToString())
        .ConfigureWarnings(w => w.Ignore(InMemoryEventId.TransactionIgnoredWarning)).Options);
    private static FlowEngine Engine(CP6Context db) => new(db, new ApproverResolver(db));

    // s → n0 → split → ( a1 → a2 , b1 ) → join → end
    private static FlowSchema P(Guid u0, Guid ua1, Guid ua2, Guid ub) => new()
    {
        Start = "s",
        Nodes =
        {
            new FlowNode { Id = "s", Type = "start" },
            new FlowNode { Id = "n0", Type = "approval", ApproverStrategy = "Specified", ApproverUserId = u0 },
            new FlowNode { Id = "split", Type = "parallelSplit" },
            new FlowNode { Id = "a1", Type = "approval", ApproverStrategy = "Specified", ApproverUserId = ua1 },
            new FlowNode { Id = "a2", Type = "approval", ApproverStrategy = "Specified", ApproverUserId = ua2 },
            new FlowNode { Id = "b1", Type = "approval", ApproverStrategy = "Specified", ApproverUserId = ub },
            new FlowNode { Id = "join", Type = "parallelJoin" },
            new FlowNode { Id = "end", Type = "end" },
        },
        Edges =
        {
            new FlowEdge { From = "s", To = "n0" }, new FlowEdge { From = "n0", To = "split" },
            new FlowEdge { From = "split", To = "a1" }, new FlowEdge { From = "a1", To = "a2" },
            new FlowEdge { From = "split", To = "b1" },
            new FlowEdge { From = "a2", To = "join" }, new FlowEdge { From = "b1", To = "join" },
            new FlowEdge { From = "join", To = "end" },
        },
    };

    private static async Task<(Guid instId, Guid u0, Guid ua1, Guid ua2, Guid ub)> SetupAtA2(CP6Context db)
    {
        Guid u0 = Guid.NewGuid(), ua1 = Guid.NewGuid(), ua2 = Guid.NewGuid(), ub = Guid.NewGuid();
        db.Wf_FlowDefs.Add(new Wf_FlowDef { Id = Guid.NewGuid(), FlowKey = "tr", FlowName = "tr", FormKey = "f",
            SchemaJson = JsonSerializer.Serialize(P(u0, ua1, ua2, ub)), Version = 1, Enable = true });
        await db.SaveChangesAsync();
        var instId = await Engine(db).SubmitAsync("tr", Guid.NewGuid(), "{}");
        var t0 = await db.Wf_FlowTasks.SingleAsync(t => t.AssigneeId == u0 && t.Status == FlowTaskStatus.Pending);
        await Engine(db).ActAsync(t0.Id, u0, approve: true);              // 进 split → a1/b1
        var t1 = await db.Wf_FlowTasks.SingleAsync(t => t.AssigneeId == ua1 && t.Status == FlowTaskStatus.Pending);
        await Engine(db).ActAsync(t1.Id, ua1, approve: true);             // a 支推进到 a2
        return (instId, u0, ua1, ua2, ub);
    }

    [Fact]
    public async Task SameBranch_StripsOwnBranchOnly_SiblingSurvives_JoinStillRecognizesKin()
    {
        using var db = NewDb();
        var (instId, _, ua1, ua2, ub) = await SetupAtA2(db);
        var branchFork = (await db.Wf_FlowTokens.SingleAsync(t => t.NodeId == "a2" && t.Status == FlowTokenStatus.Active)).ForkId;

        var t2 = await db.Wf_FlowTasks.SingleAsync(t => t.AssigneeId == ua2 && t.Status == FlowTaskStatus.Pending);
        await Engine(db).SendBackAsync(t2.Id, ua2, new SendBackTarget("node", "a1"), "分支内退回");

        // ★ 兄弟支 b1 不倒
        Assert.True(await db.Wf_FlowTokens.AnyAsync(t => t.NodeId == "b1" && t.Status == FlowTokenStatus.Active));
        Assert.True(await db.Wf_FlowTasks.AnyAsync(t => t.AssigneeId == ub && t.Status == FlowTaskStatus.Pending));
        // ★ 重生 token 在 a1、携剥离层血缘（同 ForkId，外层 join 认亲不破坏）
        var reborn = await db.Wf_FlowTokens.SingleAsync(t => t.NodeId == "a1" && t.Status == FlowTokenStatus.Active);
        Assert.Equal(branchFork, reborn.ForkId);
        Assert.Equal(FlowInstanceStatus.Running, (await db.Wf_FlowInstances.SingleAsync()).Status);
        Assert.Equal(1, await db.Wf_FlowHistories.CountAsync(h => h.Action == "sendback"));

        // 重走 a 支到底 + b 支过 → join 齐批 → Approved（认亲回归）
        var r1 = await db.Wf_FlowTasks.SingleAsync(t => t.AssigneeId == ua1 && t.Status == FlowTaskStatus.Pending);
        await Engine(db).ActAsync(r1.Id, ua1, approve: true);
        var r2 = await db.Wf_FlowTasks.SingleAsync(t => t.AssigneeId == ua2 && t.Status == FlowTaskStatus.Pending);
        await Engine(db).ActAsync(r2.Id, ua2, approve: true);
        var tb = await db.Wf_FlowTasks.SingleAsync(t => t.AssigneeId == ub && t.Status == FlowTaskStatus.Pending);
        await Engine(db).ActAsync(tb.Id, ub, approve: true);
        Assert.Equal(FlowInstanceStatus.Approved, (await db.Wf_FlowInstances.SingleAsync()).Status);
    }

    [Fact]
    public async Task BeforeSplit_FullClear_SingleRootRebornAtTarget()
    {
        using var db = NewDb();
        var (instId, u0, _, ua2, ub) = await SetupAtA2(db);

        var t2 = await db.Wf_FlowTasks.SingleAsync(t => t.AssigneeId == ua2 && t.Status == FlowTaskStatus.Pending);
        await Engine(db).SendBackAsync(t2.Id, ua2, new SendBackTarget("node", "n0"), "整块重来");

        // 现行全清场语义：全部旧 token Cancelled、b 支任务 Cancelled、单根重生在 n0（血缘归零）
        var actives = await db.Wf_FlowTokens.Where(t => t.InstanceId == instId && t.Status == FlowTokenStatus.Active).ToListAsync();
        Assert.Single(actives);
        Assert.Equal("n0", actives[0].NodeId);
        Assert.Null(actives[0].ParentTokenId);
        Assert.Null(actives[0].ForkId);
        Assert.False(await db.Wf_FlowTasks.AnyAsync(t => t.AssigneeId == ub && t.Status == FlowTaskStatus.Pending));
        Assert.False(await db.Wf_FlowFormTos.AnyAsync(f => f.NodeId == "b1" && f.Status == FlowFormToStatus.Pending));
        Assert.True(await db.Wf_FlowTasks.AnyAsync(t => t.AssigneeId == u0 && t.Status == FlowTaskStatus.Pending));
    }

    [Fact]
    public async Task SiblingBranch_Throws_E_WF_019_NothingMutated()
    {
        using var db = NewDb();
        var (_, _, _, ua2, ub) = await SetupAtA2(db);

        var t2 = await db.Wf_FlowTasks.SingleAsync(t => t.AssigneeId == ua2 && t.Status == FlowTaskStatus.Pending);
        var ex = await Assert.ThrowsAsync<InvalidOperationException>(
            () => Engine(db).SendBackAsync(t2.Id, ua2, new SendBackTarget("node", "b1")));
        Assert.Contains("E-WF-019", ex.Message);

        // 先校验后写：拒绝发生在任何状态突变之前
        Assert.True(await db.Wf_FlowTasks.AnyAsync(t => t.Id == t2.Id && t.Status == FlowTaskStatus.Pending));
        Assert.True(await db.Wf_FlowTokens.AnyAsync(t => t.NodeId == "a2" && t.Status == FlowTokenStatus.Active));
        Assert.True(await db.Wf_FlowTokens.AnyAsync(t => t.NodeId == "b1" && t.Status == FlowTokenStatus.Active));
        Assert.Equal(0, await db.Wf_FlowHistories.CountAsync(h => h.Action == "sendback"));
    }

    [Fact]
    public async Task Starter_FromParallelBranch_FullClear_BackToDraft()
    {
        using var db = NewDb();
        var (instId, _, _, ua2, _) = await SetupAtA2(db);
        var t2 = await db.Wf_FlowTasks.SingleAsync(t => t.AssigneeId == ua2 && t.Status == FlowTaskStatus.Pending);
        await Engine(db).SendBackAsync(t2.Id, ua2, new SendBackTarget("starter"), "退回重填");

        // starter 天然 BeforeSplit → 现行为不变：全清场 + 回草稿
        var inst = await db.Wf_FlowInstances.SingleAsync(i => i.Id == instId);
        Assert.Equal(FlowInstanceStatus.Draft, inst.Status);
        Assert.False(await db.Wf_FlowTokens.AnyAsync(t => t.Status == FlowTokenStatus.Active));
        Assert.False(await db.Wf_FlowTasks.AnyAsync(t => t.Status == FlowTaskStatus.Pending));
    }

    // inclusive 网关 SameBranch/Sibling（网关类型矩阵另一半）
    private static FlowSchema Inc(Guid ua1, Guid ua2, Guid ub, Guid ud) => new()
    {
        Start = "s",
        Nodes =
        {
            new FlowNode { Id = "s", Type = "start" },
            new FlowNode { Id = "isplit", Type = "inclusiveSplit" },
            new FlowNode { Id = "a1", Type = "approval", ApproverStrategy = "Specified", ApproverUserId = ua1 },
            new FlowNode { Id = "a2", Type = "approval", ApproverStrategy = "Specified", ApproverUserId = ua2 },
            new FlowNode { Id = "b1", Type = "approval", ApproverStrategy = "Specified", ApproverUserId = ub },
            new FlowNode { Id = "d", Type = "approval", ApproverStrategy = "Specified", ApproverUserId = ud },
            new FlowNode { Id = "ijoin", Type = "inclusiveJoin" },
            new FlowNode { Id = "end", Type = "end" },
        },
        Edges =
        {
            new FlowEdge { From = "s", To = "isplit" },
            new FlowEdge { From = "isplit", To = "a1", Condition = "goA > 0" },
            new FlowEdge { From = "a1", To = "a2" },
            new FlowEdge { From = "isplit", To = "b1", Condition = "goB > 0" },
            new FlowEdge { From = "isplit", To = "d" },
            new FlowEdge { From = "a2", To = "ijoin" }, new FlowEdge { From = "b1", To = "ijoin" },
            new FlowEdge { From = "d", To = "ijoin" },
            new FlowEdge { From = "ijoin", To = "end" },
        },
    };

    [Fact]
    public async Task Inclusive_SameBranch_SiblingSurvives_And_SiblingTarget_E_WF_019()
    {
        using var db = NewDb();
        Guid ua1 = Guid.NewGuid(), ua2 = Guid.NewGuid(), ub = Guid.NewGuid(), ud = Guid.NewGuid();
        db.Wf_FlowDefs.Add(new Wf_FlowDef { Id = Guid.NewGuid(), FlowKey = "itr", FlowName = "x", FormKey = "f",
            SchemaJson = JsonSerializer.Serialize(Inc(ua1, ua2, ub, ud)), Version = 1, Enable = true });
        await db.SaveChangesAsync();
        await Engine(db).SubmitAsync("itr", Guid.NewGuid(), "{\"goA\":1,\"goB\":1}");   // a/b 两支激活

        var t1 = await db.Wf_FlowTasks.SingleAsync(t => t.AssigneeId == ua1 && t.Status == FlowTaskStatus.Pending);
        await Engine(db).ActAsync(t1.Id, ua1, approve: true);              // a 支到 a2

        // 兄弟支目标 → E-WF-019
        var t2 = await db.Wf_FlowTasks.SingleAsync(t => t.AssigneeId == ua2 && t.Status == FlowTaskStatus.Pending);
        var ex = await Assert.ThrowsAsync<InvalidOperationException>(
            () => Engine(db).SendBackAsync(t2.Id, ua2, new SendBackTarget("node", "b1")));
        Assert.Contains("E-WF-019", ex.Message);

        // 同支退回 → 兄弟 b1 不倒、重走后齐批
        await Engine(db).SendBackAsync(t2.Id, ua2, new SendBackTarget("node", "a1"), "同支退");
        Assert.True(await db.Wf_FlowTasks.AnyAsync(t => t.AssigneeId == ub && t.Status == FlowTaskStatus.Pending));

        var r1 = await db.Wf_FlowTasks.SingleAsync(t => t.AssigneeId == ua1 && t.Status == FlowTaskStatus.Pending);
        await Engine(db).ActAsync(r1.Id, ua1, approve: true);
        var r2 = await db.Wf_FlowTasks.SingleAsync(t => t.AssigneeId == ua2 && t.Status == FlowTaskStatus.Pending);
        await Engine(db).ActAsync(r2.Id, ua2, approve: true);
        var tb = await db.Wf_FlowTasks.SingleAsync(t => t.AssigneeId == ub && t.Status == FlowTaskStatus.Pending);
        await Engine(db).ActAsync(tb.Id, ub, approve: true);
        Assert.Equal(FlowInstanceStatus.Approved, (await db.Wf_FlowInstances.SingleAsync()).Status);
    }

    [Fact]
    public async Task Nested_SameBranch_OuterStripLayer_InnerSiblingsKilled_OuterSiblingSurvives()
    {
        // s → outer → ( h1 → inner → (x1,x2) → ij , b ) → oj → end；x1 上退回 h1 → 剥离外层支（含 x2）、b 不倒
        using var db = NewDb();
        Guid uh = Guid.NewGuid(), u1 = Guid.NewGuid(), u2 = Guid.NewGuid(), ub = Guid.NewGuid();
        var schema = new FlowSchema
        {
            Start = "s",
            Nodes =
            {
                new FlowNode { Id = "s", Type = "start" },
                new FlowNode { Id = "outer", Type = "parallelSplit" },
                new FlowNode { Id = "h1", Type = "approval", ApproverStrategy = "Specified", ApproverUserId = uh },
                new FlowNode { Id = "inner", Type = "parallelSplit" },
                new FlowNode { Id = "x1", Type = "approval", ApproverStrategy = "Specified", ApproverUserId = u1 },
                new FlowNode { Id = "x2", Type = "approval", ApproverStrategy = "Specified", ApproverUserId = u2 },
                new FlowNode { Id = "ij", Type = "parallelJoin" },
                new FlowNode { Id = "b", Type = "approval", ApproverStrategy = "Specified", ApproverUserId = ub },
                new FlowNode { Id = "oj", Type = "parallelJoin" },
                new FlowNode { Id = "end", Type = "end" },
            },
            Edges =
            {
                new FlowEdge { From = "s", To = "outer" },
                new FlowEdge { From = "outer", To = "h1" }, new FlowEdge { From = "h1", To = "inner" },
                new FlowEdge { From = "inner", To = "x1" }, new FlowEdge { From = "inner", To = "x2" },
                new FlowEdge { From = "x1", To = "ij" }, new FlowEdge { From = "x2", To = "ij" },
                new FlowEdge { From = "ij", To = "oj" },
                new FlowEdge { From = "outer", To = "b" }, new FlowEdge { From = "b", To = "oj" },
                new FlowEdge { From = "oj", To = "end" },
            },
        };
        db.Wf_FlowDefs.Add(new Wf_FlowDef { Id = Guid.NewGuid(), FlowKey = "nsb", FlowName = "x", FormKey = "f",
            SchemaJson = JsonSerializer.Serialize(schema), Version = 1, Enable = true });
        await db.SaveChangesAsync();
        await Engine(db).SubmitAsync("nsb", Guid.NewGuid(), "{}");

        var th = await db.Wf_FlowTasks.SingleAsync(t => t.AssigneeId == uh && t.Status == FlowTaskStatus.Pending);
        await Engine(db).ActAsync(th.Id, uh, approve: true);               // h1 过 → 进 inner → x1/x2
        var outerFork = (await db.Wf_FlowTokens.SingleAsync(t => t.NodeId == "inner")).ForkId;

        var tx1 = await db.Wf_FlowTasks.SingleAsync(t => t.AssigneeId == u1 && t.Status == FlowTaskStatus.Pending);
        await Engine(db).SendBackAsync(tx1.Id, u1, new SendBackTarget("node", "h1"), "退到内层 split 之前");

        // 内层兄弟 x2 连带剥离；外层兄弟 b 不倒；重生在 h1 携外层血缘
        Assert.False(await db.Wf_FlowTasks.AnyAsync(t => t.AssigneeId == u2 && t.Status == FlowTaskStatus.Pending));
        Assert.False(await db.Wf_FlowTokens.AnyAsync(t =>
            (t.NodeId == "x1" || t.NodeId == "x2") && t.Status == FlowTokenStatus.Active));
        Assert.True(await db.Wf_FlowTasks.AnyAsync(t => t.AssigneeId == ub && t.Status == FlowTaskStatus.Pending));
        var reborn = await db.Wf_FlowTokens.SingleAsync(t => t.NodeId == "h1" && t.Status == FlowTokenStatus.Active);
        Assert.Equal(outerFork, reborn.ForkId);

        // 重走全程 → Approved（外层 join 认亲齐批）
        var rh = await db.Wf_FlowTasks.SingleAsync(t => t.AssigneeId == uh && t.Status == FlowTaskStatus.Pending);
        await Engine(db).ActAsync(rh.Id, uh, approve: true);
        var rx1 = await db.Wf_FlowTasks.SingleAsync(t => t.AssigneeId == u1 && t.Status == FlowTaskStatus.Pending);
        await Engine(db).ActAsync(rx1.Id, u1, approve: true);
        var rx2 = await db.Wf_FlowTasks.SingleAsync(t => t.AssigneeId == u2 && t.Status == FlowTaskStatus.Pending);
        await Engine(db).ActAsync(rx2.Id, u2, approve: true);
        var tb = await db.Wf_FlowTasks.SingleAsync(t => t.AssigneeId == ub && t.Status == FlowTaskStatus.Pending);
        await Engine(db).ActAsync(tb.Id, ub, approve: true);
        Assert.Equal(FlowInstanceStatus.Approved, (await db.Wf_FlowInstances.SingleAsync()).Status);
    }
}
```

- [ ] **Step 2: 跑验证 FAIL** — `--filter SendBackThreeRuleTests`：SameBranch/BeforeSplit 场景现状抛 E-WF-012（CrossesParallelBlock 禁令）→ 失败；Sibling 场景现状也抛 E-WF-012 而非 019 → 失败。

- [ ] **Step 3: 实现** — `AdvancedFlow.cs` 的 `SendBackToNodeAsync` 整体替换为（并删除 `CrossesParallelBlock`/`NodesBetween` 两个私有方法）：

```csharp
    private async Task SendBackToNodeAsync(Wf_FlowInstance inst, FlowSchema schema, Wf_FlowTask task,
        Guid actorId, string? targetNodeId, string? comment)
    {
        var target = FindNode(schema, targetNodeId ?? "") ?? throw new InvalidOperationException("E-WF-012");
        if (IsType(target, "end") || target.Id == task.NodeId) throw new InvalidOperationException("E-WF-012");
        var tt = (target.Type ?? "approval").Trim().ToLowerInvariant();
        if (tt != "approval" && tt != "start") throw new InvalidOperationException("E-WF-012");
        if (!IsUpstreamReachable(schema, target.Id, task.NodeId)) throw new InvalidOperationException("E-WF-012");

        // hardening C-T3 三规则（spec §5）：作用域判定取代旧 CrossesParallelBlock 一刀切禁令。
        // 先校验后写：SiblingBranch 拒绝发生在任何状态突变之前。旧数据无 token 维度 → 现行为全清场。
        var all = SnapshotTokens(inst.Id);
        var curTok = task.TokenId is Guid tid ? all.FirstOrDefault(t => t.Id == tid) : null;
        var (scope, strip) = curTok is null
            ? (SendBackScope.BeforeSplit, (Wf_FlowToken?)null)
            : SendBackScopeAnalyzer.Analyze(schema, all, curTok, task.NodeId, target.Id);
        if (scope == SendBackScope.SiblingBranch) throw new InvalidOperationException("E-WF-019");

        if (scope == SendBackScope.SameBranch && strip is not null)
        {
            // 分支内剥离：只清剥离层子树（含内层 fork 兄弟），外层兄弟分支零扰动；
            // 重生 token 接管剥离层血缘 → 外层 join 认亲不破坏（spec §5.2 ★）
            CancelTokenSubtree(inst.Id, strip.Id);
            AddHistory(inst.Id, task.NodeId, actorId, "sendback", comment ?? $"退回至 {target.Id}");
            var reborn = SpawnToken(inst, target, parent: strip.ParentTokenId, fork: strip.ForkId);
            await EnterNodeAsync(inst, schema, target, reborn);
            return;
        }

        // BeforeSplit（含线性流 fork 栈为空）：既有全清场路径，逐字保留
        var live = await _db.Wf_FlowTasks
            .Where(t => t.InstanceId == inst.Id && (t.Status == FlowTaskStatus.Pending || t.Status == FlowTaskStatus.Suspended))
            .ToListAsync();
        foreach (var t in live) t.Status = FlowTaskStatus.Cancelled;
        AddHistory(inst.Id, task.NodeId, actorId, "sendback", comment ?? $"退回至 {target.Id}");
        CancelAllActiveTokens(inst.Id);
        VoidPendingFormTos(inst.Id);
        var sbToken = SpawnToken(inst, target, parent: null, fork: null);
        await EnterNodeAsync(inst, schema, target, sbToken);
    }
```

（`SendBackToPrevStageAsync` / `SendBackToStarterAsync` **零改**：prevStage 天然同支且现行为已是收窄清场；starter 天然 BeforeSplit。）

- [ ] **Step 4: 跑验证 PASS** — `--filter SendBackThreeRuleTests` 全绿。
- [ ] **Step 5: 线性零 diff 铁闸 + commit** — `--filter AdvancedFlowTests` + `--filter SerialSendBackTests` 断言零改全绿（线性流 fork 栈为空 → BeforeSplit → 走原全清场代码块）。

```bash
dotnet test CP6.Tests/CP6.Tests.csproj --filter Wf
git add -A && git commit -m "feat(wfs-hardening): C-T3 退回三规则接线(SameBranch剥离/BeforeSplit全清场/E-WF-019)+网关矩阵测试"
```

---

## Wave H-D — 设计器镜像（依赖 H-A/H-B 的 schema 契约；H-C 无前端面）

> 纪律：视图全用 `t()` 运行时键（键值 E-T1 落 seed，键名本波即定稿）；每 Task 跑 `npm run test` + `npm run type-check`；零硬编码色。

### Task D-T1: designerModel — palette 2 入口 + onBranchReject round-trip + validateClient 镜像 + notification 类型镜像

**Files:**
- Modify: `cp6.web/src/views/oa/designer/designerModel.ts`
- Modify: `cp6.web/src/views/oa/designer/designerModel.test.ts`（仅 :45 palette 类型清单断言随扩展更新——该断言即「palette 全集清单」，非行为不变量）
- Modify: `cp6.web/src/types/oa/notification.ts`
- Test: `cp6.web/src/views/oa/designer/designerModel.hardening.test.ts`

- [ ] **Step 1: 写失败 vitest**

```ts
// cp6.web/src/views/oa/designer/designerModel.hardening.test.ts
import { describe, it, expect } from 'vitest'
import { schemaToGraph, graphToSchema, validateClient, NODE_PALETTE } from './designerModel'
import type { FlowSchemaDto } from './designerModel'

describe('inclusive gateway & branch-reject (kernel hardening)', () => {
  it('palette has inclusiveSplit/inclusiveJoin entries', () => {
    expect(NODE_PALETTE.some(p => p.type === 'inclusiveSplit')).toBe(true)
    expect(NODE_PALETTE.some(p => p.type === 'inclusiveJoin')).toBe(true)
  })

  it('onBranchReject round-trips through graph conversion', () => {
    const schema: FlowSchemaDto = {
      start: 's',
      nodes: [
        { id: 's', type: 'start' },
        { id: 'g', type: 'parallelSplit', onBranchReject: 'prune' },
      ],
      edges: [{ from: 's', to: 'g' }],
    }
    const back = graphToSchema(schemaToGraph(schema))
    expect(back.nodes.find(n => n.id === 'g')?.onBranchReject).toBe('prune')
  })

  const incBase = (): FlowSchemaDto => ({
    start: 's',
    nodes: [
      { id: 's', type: 'start' },
      { id: 'g', type: 'inclusiveSplit' },
      { id: 'a', type: 'approval', approverStrategy: 'Starter' },
      { id: 'd', type: 'approval', approverStrategy: 'Starter' },
      { id: 'j', type: 'inclusiveJoin' },
      { id: 'e', type: 'end' },
    ],
    edges: [
      { from: 's', to: 'g' },
      { from: 'g', to: 'a', condition: 'x > 0' },
      { from: 'g', to: 'd' },                          // default
      { from: 'a', to: 'j' }, { from: 'd', to: 'j' },
      { from: 'j', to: 'e' },
    ],
  })

  it('valid inclusive pair passes', () => {
    expect(validateClient(incBase())).toEqual([])
  })

  it('missing default edge -> errInclusiveDefault (E-WF-020 mirror)', () => {
    const s = incBase()
    s.edges.find(e => e.from === 'g' && e.to === 'd')!.condition = 'y > 0'
    expect(validateClient(s)).toContain('oa.designer.errInclusiveDefault')
  })

  it('two default edges -> errInclusiveDefault', () => {
    const s = incBase()
    s.edges.find(e => e.from === 'g' && e.to === 'a')!.condition = undefined
    expect(validateClient(s)).toContain('oa.designer.errInclusiveDefault')
  })

  it('paired with parallelJoin -> errInclusivePair (E-WF-021 mirror)', () => {
    const s = incBase()
    s.nodes.find(n => n.id === 'j')!.type = 'parallelJoin'
    expect(validateClient(s)).toContain('oa.designer.errInclusivePair')
  })

  it('orphan inclusiveJoin -> errInclusivePair', () => {
    const s: FlowSchemaDto = {
      start: 's',
      nodes: [
        { id: 's', type: 'start' },
        { id: 'a', type: 'approval', approverStrategy: 'Starter' },
        { id: 'j', type: 'inclusiveJoin' },
        { id: 'e', type: 'end' },
      ],
      edges: [
        { from: 's', to: 'a' }, { from: 'a', to: 'j' }, { from: 's', to: 'j' },
        { from: 'j', to: 'e' },
      ],
    }
    expect(validateClient(s)).toContain('oa.designer.errInclusivePair')
  })

  it('onBranchReject bad value / wrong node -> errBranchReject (E-WF-021c mirror)', () => {
    const s1 = incBase()
    s1.nodes.find(n => n.id === 'g')!.onBranchReject = 'explode' as any
    expect(validateClient(s1)).toContain('oa.designer.errBranchReject')
    const s2 = incBase()
    s2.nodes.find(n => n.id === 'a')!.onBranchReject = 'prune'
    expect(validateClient(s2)).toContain('oa.designer.errBranchReject')
  })
})
```

- [ ] **Step 2: 跑验证 FAIL** — `cd cp6.web && npm run test -- designerModel.hardening`。

- [ ] **Step 3: 实现** — `designerModel.ts`：

`SchemaNode` 接口服务任务字段块后加：

```ts
  // 内核 hardening：分支驳回策略（仅 parallelSplit/inclusiveSplit；镜像后端 FlowNode.OnBranchReject，camelCase 契约）
  onBranchReject?: 'cascade' | 'prune'
```

`NODE_PALETTE` 在 `parallelJoin` 条目后加两行（沿既有条目风格；色彩由组件层 `.dot-<type>` token 决定，不带 color 字段——对齐 serviceTask 先例注释）：

```ts
  { type: 'inclusiveSplit', label: '包容分叉' },
  { type: 'inclusiveJoin',  label: '包容汇聚' },
```

`validateClient` 末尾（serviceTask 校验块之后、`return errs` 之前）加镜像块——**与后端逐条对齐，不多不少**（E-WF-020 ↔ errInclusiveDefault；E-WF-021a/b ↔ errInclusivePair；E-WF-021c ↔ errBranchReject）：

```ts
  // ── inclusive 网关镜像（后端 E-WF-020/021，kernel hardening）──
  const nodeType = (id: string) => nodes.find(n => n.id === id)?.type
  const isJoinType = (ty?: string) => ty === 'parallelJoin' || ty === 'inclusiveJoin'
  const bfsDepths = (from: string): Map<string, number> => {
    const depth = new Map<string, number>([[from, 0]])
    const q = [from]
    while (q.length) {
      const cur = q.shift()!
      for (const e of edges.filter(e => e.from === cur))
        if (!depth.has(e.to)) { depth.set(e.to, depth.get(cur)! + 1); q.push(e.to) }
    }
    return depth
  }
  const nearestCommonJoin = (splitId: string): string | undefined => {
    const outs = edges.filter(e => e.from === splitId && !e.isError)
    if (!outs.length) return undefined
    const sets = outs.map(e => new Set(bfsDepths(e.to).keys()))
    const common = [...sets[0]!].filter(id => sets.every(s => s.has(id)))
    const depths = bfsDepths(splitId)
    let best: string | undefined
    let bestD = Infinity
    for (const id of common) {
      const d = depths.get(id) ?? Infinity
      if (isJoinType(nodeType(id)) && d < bestD) { best = id; bestD = d }
    }
    return best
  }
  // E-WF-020 镜像：inclusiveSplit 出边 ≥2 且恰好一条无条件 default 边
  for (const n of nodes) {
    if (n.type !== 'inclusiveSplit') continue
    const outs = edges.filter(e => e.from === n.id && !e.isError)
    const dflt = outs.filter(e => !e.condition?.trim())
    if (outs.length < 2 || dflt.length !== 1) { errs.push('oa.designer.errInclusiveDefault'); break }
  }
  // E-WF-021a/b 镜像：split 最近公共汇聚须为 inclusiveJoin；inclusiveJoin 入边≥2 且被配对
  const pairedJoins = new Set<string>()
  let pairBad = false
  for (const n of nodes) {
    if (n.type !== 'inclusiveSplit') continue
    const j = nearestCommonJoin(n.id)
    if (!j || nodeType(j) !== 'inclusiveJoin') { pairBad = true; continue }
    pairedJoins.add(j)
  }
  for (const n of nodes) {
    if (n.type !== 'inclusiveJoin') continue
    if (edges.filter(e => e.to === n.id).length < 2 || !pairedJoins.has(n.id)) pairBad = true
  }
  if (pairBad) errs.push('oa.designer.errInclusivePair')
  // E-WF-021c 镜像：onBranchReject 值域 + 只许写在 split 型节点
  for (const n of nodes) {
    if (n.onBranchReject == null) continue
    const ok = (n.type === 'parallelSplit' || n.type === 'inclusiveSplit')
      && (n.onBranchReject === 'cascade' || n.onBranchReject === 'prune')
    if (!ok) { errs.push('oa.designer.errBranchReject'); break }
  }
```

`designerModel.test.ts` :45 断言更新为（**本计划唯一允许的既有测试改动**，随 palette 全集扩展）：

```ts
    expect([...new Set(NODE_PALETTE.map(p => p.type))].sort())
      .toEqual(['approval', 'end', 'inclusiveJoin', 'inclusiveSplit', 'parallelJoin', 'parallelSplit', 'serviceTask', 'start'])
```

`types/oa/notification.ts` 的 `NotificationType` 常量加：

```ts
  BranchPruned: 5,
```

- [ ] **Step 4: 跑验证 PASS** — `npm run test`（320+N 全绿）+ `npm run type-check`。
- [ ] **Step 5: commit**

```bash
git add -A && git commit -m "feat(wfs-hardening): D-T1 designerModel palette两入口+onBranchReject round-trip+validateClient E-WF-020/021镜像+通知类型镜像"
```

### Task D-T2: InclusiveGatewayNode 节点组件 + 画布接线 + NodePropertyPanel「分支驳回策略」段

**Files:**
- Create: `cp6.web/src/views/oa/designer/nodes/InclusiveGatewayNode.vue`
- Modify: `cp6.web/src/views/oa/designer/DesignerCanvas.vue`（import + 2 个 `#node-*` 模板 + 2 个 `.dot-*` 样式）
- Modify: `cp6.web/src/views/oa/designer/NodePropertyPanel.vue`
- **不碰** `EdgePropertyPanel.vue`（spec §7.3：inclusive 出边复用既有条件边编辑，零改）

- [ ] **Step 1: 实现 InclusiveGatewayNode.vue**（BPMN 惯例：菱形内空心圆，区别 parallel 实心菱形；全 token 无硬编码色；文案 `t()`）：

```vue
<script setup lang="ts">
import { computed } from 'vue'
import { Handle, Position } from '@vue-flow/core'
import type { NodeProps } from '@vue-flow/core'
import { useI18n } from 'vue-i18n'

const props = defineProps<NodeProps>()
const { t } = useI18n()

type NodeData = { type?: string }
const isJoin = computed(() => (props.data as NodeData)?.type === 'inclusiveJoin')
</script>

<template>
  <!-- Inclusive gateway: 菱形 + 内嵌空心圆（BPMN inclusive 记号），区别 GatewayNode 的实心菱形 -->
  <div :class="['vf-node-inclusive-wrap', { 'vf-node--selected': props.selected }]">
    <Handle type="target" :position="Position.Top" />
    <div class="vf-node-inclusive">
      <span class="inc-circle" />
      <span class="inc-label">{{ isJoin ? t('oa.designer.gw.inclusiveJoin') : t('oa.designer.gw.inclusiveSplit') }}</span>
    </div>
    <Handle type="source" :position="Position.Bottom" />
  </div>
</template>

<style scoped>
.vf-node-inclusive-wrap {
  display: flex;
  flex-direction: column;
  align-items: center;
  background: transparent;
  cursor: default;
}
.vf-node-inclusive-wrap.vf-node--selected .vf-node-inclusive {
  box-shadow: 0 0 0 2px color-mix(in srgb, var(--cp-warn) 50%, transparent);
}
.vf-node-inclusive {
  position: relative;
  width: 60px;
  height: 60px;
  background: var(--cp-warn-bg);
  border: 2px solid var(--cp-warn);
  transform: rotate(45deg);
  display: flex;
  align-items: center;
  justify-content: center;
}
.inc-circle {
  position: absolute;
  inset: 8px;
  border: 2px solid var(--cp-warn);
  border-radius: 50%;
}
.inc-label {
  transform: rotate(-45deg);
  font-size: 10px;
  font-weight: 500;
  color: var(--cp-warn);
  white-space: nowrap;
  z-index: 1;
}
</style>
```

- [ ] **Step 2: DesignerCanvas.vue 接线**
  - :18 `GatewayNode` import 后加 `import InclusiveGatewayNode from './nodes/InclusiveGatewayNode.vue'`。
  - :390-392 `#node-parallelJoin` 模板后加：

```html
          <template #node-inclusiveSplit="nodeProps">
            <InclusiveGatewayNode v-bind="nodeProps" />
          </template>
          <template #node-inclusiveJoin="nodeProps">
            <InclusiveGatewayNode v-bind="nodeProps" />
          </template>
```

  - :516-517 `.dot-parallelSplit/.dot-parallelJoin` 样式后加（palette 空心 dot，与节点「空心圆」身份一致，零硬编码色）：

```css
/* inclusive 网关：空心 dot 区分 parallel 实心（BPMN 记号一致性）。 */
.dot-inclusiveSplit,
.dot-inclusiveJoin { background: transparent; border: 2px solid var(--cp-warn); }
```

- [ ] **Step 3: NodePropertyPanel.vue「分支驳回策略」段**
  - script 内 :79 `isServiceTask` 之后加：

```ts
const isSplitGateway = computed(
  () => local.value.type === 'parallelSplit' || local.value.type === 'inclusiveSplit',
)

// 分支驳回策略：默认 cascade 不落 schema（旧流程零污染，与后端 null=cascade 语义一致）
const branchReject = computed({
  get: () => local.value.onBranchReject ?? 'cascade',
  set: (v: 'cascade' | 'prune') => {
    local.value.onBranchReject = v === 'cascade' ? undefined : v
  },
})
```

  - template「基本參數」collapse-item 内 `nodeType` 表单项（:226-228）之后加：

```html
          <!-- ── 分支驳回策略（parallelSplit/inclusiveSplit 专属，hardening D-T2）────── -->
          <template v-if="isSplitGateway">
            <el-form-item :label="t('oa.designer.gw.branchReject')">
              <el-select v-model="branchReject" style="width: 100%">
                <el-option value="cascade" :label="t('oa.designer.gw.branchReject.cascade')" />
                <el-option value="prune"   :label="t('oa.designer.gw.branchReject.prune')" />
              </el-select>
              <span class="gw-hint">{{ t('oa.designer.gw.branchRejectHint') }}</span>
            </el-form-item>
          </template>
```

  - scoped style 加（若面板已有等价 hint 类则复用之，勿重复造类）：

```css
.gw-hint {
  display: block;
  margin-top: 4px;
  font-size: 11px;
  line-height: 1.5;
  color: var(--cp-muted);
}
```

- [ ] **Step 4: 验证** — `npm run type-check` + `npm run test` + `npm run build`（Vue Flow 渲染 smoke 留 QA harness）。
- [ ] **Step 5: commit**

```bash
git add -A && git commit -m "feat(wfs-hardening): D-T2 InclusiveGatewayNode空心圆菱形+画布接线+属性面板分支驳回策略段"
```

---

## Wave H-E — i18n + QA + 验收（紧跟 D 波，不留窗口）

### Task E-T1: i18n 五语 seed（12 键）

**Files:**
- Create: `CP6.WebApi/Seed/I18nOaKernelHardeningScreenSeed.cs`
- Modify: `CP6.WebApi/Program.cs`（:1819 `I18nOaServiceTaskScreenSeed` concat 行后追加）

- [ ] **Step 1: 实现 seed**（仿 `I18nOaServiceTaskScreenSeed` 静态 `Sys_Lang[] Items` 模式；**先 grep 既有 seed 确认 12 键零重复**：`grep -rn "gw.inclusive\|errInclusive\|errBranchReject\|E-WF-019\|E-WF-020\|E-WF-021" CP6.WebApi/Seed/`）：

```csharp
using CP6.Entity.DomainModels.Sys;

namespace CP6.WebApi.Seed;

/// <summary>内核 hardening 画面词条：inclusive 网关（palette/节点/面板）+ 分支驳回策略 + 前端校验镜像
/// + 后端错误码 E-WF-019/020/021。键面以 cp6.web/src/views/oa/designer 实际引用为权威
/// （InclusiveGatewayNode.vue / NodePropertyPanel.vue / designerModel.ts validateClient）。
/// 去重：12 键在既有 I18nOa* seed 中均无重复（落地前 grep 复核）。</summary>
public static class I18nOaKernelHardeningScreenSeed
{
    public static readonly Sys_Lang[] Items =
    {
        // ── 节点名（InclusiveGatewayNode.vue）──
        new() { LangKey = "oa.designer.gw.inclusiveSplit",          ZhCN = "包容分叉",         ZhTW = "包容分叉",         En = "Inclusive Split",                    Ja = "包含分岐",                     Ko = "포괄 분기" },
        new() { LangKey = "oa.designer.gw.inclusiveJoin",           ZhCN = "包容汇聚",         ZhTW = "包容匯聚",         En = "Inclusive Join",                     Ja = "包含合流",                     Ko = "포괄 합류" },

        // ── 分支驳回策略（NodePropertyPanel.vue）──
        new() { LangKey = "oa.designer.gw.branchReject",            ZhCN = "分支驳回策略",     ZhTW = "分支駁回策略",     En = "Branch Reject Policy",               Ja = "分岐却下ポリシー",             Ko = "분기 반려 정책" },
        new() { LangKey = "oa.designer.gw.branchReject.cascade",    ZhCN = "整单驳回（默认）", ZhTW = "整單駁回（預設）", En = "Reject whole instance (default)",    Ja = "全体却下（既定）",             Ko = "전체 반려(기본)" },
        new() { LangKey = "oa.designer.gw.branchReject.prune",      ZhCN = "仅剪除本分支",     ZhTW = "僅剪除本分支",     En = "Prune this branch only",             Ja = "この分岐のみ剪定",             Ko = "해당 분기만 제거" },
        new() { LangKey = "oa.designer.gw.branchRejectHint",        ZhCN = "剪枝：驳回只终止本分支，兄弟分支继续；全部分支被剪时按上一层策略处理", ZhTW = "剪枝：駁回只終止本分支，兄弟分支繼續；全部分支被剪時按上一層策略處理", En = "Prune: rejection ends only this branch; siblings continue. If every branch is pruned, the parent policy applies.", Ja = "剪定：却下は当該分岐のみ終了し、兄弟分岐は継続します。全分岐が剪定された場合は上位ポリシーを適用します。", Ko = "가지치기: 반려 시 해당 분기만 종료되고 형제 분기는 계속 진행됩니다. 모든 분기가 제거되면 상위 정책이 적용됩니다." },

        // ── 前端校验消息（designerModel.ts validateClient 镜像）──
        new() { LangKey = "oa.designer.errInclusiveDefault",        ZhCN = "包容分叉需至少2条出边且恰好一条无条件默认边", ZhTW = "包容分叉需至少2條出邊且恰好一條無條件預設邊", En = "Inclusive split needs >=2 outgoing edges with exactly one unconditional default edge", Ja = "包含分岐には2本以上の出力エッジと、条件なしのデフォルトエッジがちょうど1本必要です", Ko = "포괄 분기는 2개 이상의 출력 엣지와 정확히 1개의 무조건 기본 엣지가 필요합니다" },
        new() { LangKey = "oa.designer.errInclusivePair",           ZhCN = "包容分叉/汇聚未正确成对",                     ZhTW = "包容分叉/匯聚未正確成對",                     En = "Inclusive split/join are not correctly paired",  Ja = "包含分岐/合流が正しく対になっていません",        Ko = "포괄 분기/합류가 올바르게 짝지어지지 않았습니다" },
        new() { LangKey = "oa.designer.errBranchReject",            ZhCN = "分支驳回策略配置非法",                       ZhTW = "分支駁回策略配置非法",                       En = "Invalid branch reject policy configuration",     Ja = "分岐却下ポリシーの設定が不正です",              Ko = "분기 반려 정책 설정이 잘못되었습니다" },

        // ── 后端错误码（FlowSchemaValidator / SendBackAsync）──
        new() { LangKey = "E-WF-019", ZhCN = "不能退回到兄弟分支内部",             ZhTW = "不能退回到兄弟分支內部",             En = "Cannot send back into a sibling branch",                       Ja = "兄弟分岐内への差し戻しはできません",             Ko = "형제 분기 내부로 반려할 수 없습니다" },
        new() { LangKey = "E-WF-020", ZhCN = "包容分叉出边配置非法（需恰好一条默认边）", ZhTW = "包容分叉出邊配置非法（需恰好一條預設邊）", En = "Invalid inclusive split edges (exactly one default edge required)", Ja = "包含分岐の出力エッジ設定が不正です（デフォルトエッジがちょうど1本必要）", Ko = "포괄 분기 출력 엣지 설정이 잘못되었습니다(기본 엣지 1개 필요)" },
        new() { LangKey = "E-WF-021", ZhCN = "包容网关配对或驳回策略配置非法",     ZhTW = "包容網關配對或駁回策略配置非法",     En = "Invalid inclusive gateway pairing or branch-reject policy",    Ja = "包含ゲートウェイの対応関係または却下ポリシーの設定が不正です", Ko = "포괄 게이트웨이 페어링 또는 반려 정책 설정이 잘못되었습니다" },
    };
}
```

- [ ] **Step 2: Program.cs concat** — :1819 `.Concat(CP6.WebApi.Seed.I18nOaServiceTaskScreenSeed.Items)` 行后加：

```csharp
            .Concat(CP6.WebApi.Seed.I18nOaKernelHardeningScreenSeed.Items)  // 内核 hardening oa.designer.gw.* + errInclusive*/errBranchReject + E-WF-019/020/021
```

- [ ] **Step 3: build 验证 + commit** — `dotnet build CP6.WebApi/CP6.WebApi.csproj`（SeedLangs 运行期幂等去重）。

```bash
git add -A && git commit -m "feat(wfs-hardening): E-T1 I18nOaKernelHardeningScreenSeed 五语12键+concat"
```

---

### Task E-T2: gstack QA harness（只写不跑）

**Files:**
- Create: `docs/superpowers/qa/wfs-kernel-hardening/README.md`（剧本）
- Create: `docs/superpowers/qa/wfs-kernel-hardening/seed.sql`
- Create: `docs/superpowers/qa/wfs-kernel-hardening/qa_kernel_hardening.ps1`（HTTP e2e，ASCII 数据）

- [ ] **Step 1: 写 harness**（参 `docs/superpowers/qa/wfs-service-task/` E-T3 先例：README 剧本 + seed.sql + ps1 三件套；seed.sql 对 OA 表用单数表名 `Wf_FlowDef`/`Wf_FormDef`、`SET QUOTED_IDENTIFIER ON`；隔离库 `CP6DB_OA`）。剧本 7 条：
  1. **inclusive 2/3 真边**：seed 一张 inclusiveSplit（3 条件边 + 1 default）流程；提交 vars 令 2 边为真 → 恰 2 个待办、default 审批人无待办；两支办结 → 实例 Approved。
  2. **全假 default 兜底**：vars 全假 → 仅 default 支收待办 → 办结即 Approved。
  3. **prune 单支剪**：parallelSplit(onBranchReject=prune) 双支；A 支驳回 → 实例仍 Running、B 支待办健在、发起人收到 BranchPruned 站内通知（`Wf_Notification.Type=5`）；B 支同意 → Approved。
  4. **cascade 默认整单驳**：同拓扑不配 onBranchReject；A 支驳回 → 实例 Rejected、B 支待办作废（与现状全等）。
  5. **SameBranch 分支内退回**：A 支两节点，第二节点退回第一节点 → B 支待办不受扰；重走 A 支 + B 支办结 → Approved。
  6. **SiblingBranch 拒绝**：A 支退回到 B 支节点 → HTTP 报错含 `E-WF-019`，五语切换验证文案。
  7. **设计器真浏览器**（gstack browse）：palette 拖 inclusiveSplit/inclusiveJoin（空心圆菱形渲染）→ 属性面板配「分支驳回策略」→ 删 default 边保存 → 校验报错 `oa.designer.errInclusiveDefault`（E-WF-020 镜像）i18n 显示。
- [ ] **Step 2: commit**

```bash
git add -A && git commit -m "test(wfs-hardening): E-T2 gstack QA harness(7剧本+seed+e2e脚本,只写不跑)"
```

- [ ] **Step 3: 末期 live QA（用户在场）** — 隔离库 `CP6DB_OA` 起后端 + 前端 → 跑 ps1 HTTP e2e + gstack 真浏览器走剧本 7。**抓 bug 当场 TDD 修**（对应回归测试补进 CP6.Tests/Wf）。

---

### Task E-T3: DoD 验收（主代理执行）

- [ ] 后端 `dotnet test CP6.Tests/CP6.Tests.csproj` 全绿：**1509+N 通过（5 skip 不变）**；`--filter Wf` 既有 27 不变量测试**断言零改**（`git diff main -- CP6.Tests/Wf` 复核：既有文件只增不改）。
- [ ] `dotnet ef migrations has-pending-model-changes --project CP6.Core/CP6.Core.csproj --startup-project CP6.WebApi/CP6.WebApi.csproj --context CP6Context` **clean**（零迁移自证）。
- [ ] 前端 `npm run test` **320+N 全绿**（唯一既有改动 = designerModel.test.ts palette 清单断言）；`npm run type-check` / `npm run build` 过。
- [ ] **零 Space 污染**：`git diff --stat main..HEAD` 无 `views/space` / `*Space*` / Space 迁移。
- [ ] **零硬编码色**：新增前端文件 grep 无 `#[0-9a-fA-F]{3,6}` 字面色（NODE_PALETTE 既有条目除外）。
- [ ] i18n 12 键五语齐 + LangKey 与既有 seed 零重复（grep 复核）。
- [ ] 错误码齐备：E-WF-019（运行时测试 SendBackThreeRuleTests）/ E-WF-020、E-WF-021（InclusiveValidatorTests + validateClient 镜像测试）。
- [ ] QA harness 三件套齐（7 剧本）；live QA 留待用户在场（记入 memory 待办）。
- [ ] `git log` 提交信息全部 `feat(wfs-hardening):` / `test(wfs-hardening):` 中文风格；**只本地 commit 不 push**。

### 覆盖核对（spec §9 测试策略 → 任务）

| spec §9 条目 | 落点 |
|---|---|
| inclusive 2/3 真边 / 全真（default 不走）/ 全假仅 default | A-T3 `InclusiveGatewayTests` 前三测 + QA 剧本 1/2 |
| 嵌套 parallel⊂inclusive 与 inclusive⊂parallel | A-T3 `InclusiveInsideParallel_*`（inclusive⊂parallel）+ `ParallelInsideInclusive_*`（parallel⊂inclusive）双向齐 |
| 动态计票与静态等价回归（旧场景全等） | A-T2 Step 2 先绿后改 + 既有 `ParallelGatewayTests` 5 测断言零改 |
| **嵌套在途防提前放行定点**（A 到场、B 在内层子 fork 在途） | A-T2 `NestedInFlight_OuterJoinWaits_UntilInnerSubtreeDone` |
| 剪枝：单支剪 / 多支剪 / 剪后 join 补放行 / 全剪光→Rejected | B-T2 `BranchPruneTests` 四测 |
| 嵌套递归上弹（内层全剪光 × 外层 prune/cascade 两态） | B-T3 `BranchPruneNestedTests` 前两测 |
| cascade 默认零 diff 回归 | B-T2 `Cascade_Default_ZeroDiff_*`（null/"cascade" Theory）+ `Parallel_RejectTerminates` 不变量 |
| FormTo 履历状态矩阵（Pruned 支 Voided、兄弟不受扰） | B-T2 `Prune_SingleBranch_*` / B-T3 `InnerAllPruned_OuterCascade_*` 内嵌断言 |
| 退回三规则 ×（parallel/inclusive）×（node/prevStage/starter） | C-T3 `SendBackThreeRuleTests`（parallel×node 三分 + inclusive×node 两分 + starter）；prevStage 现行为由既有 `SerialSendBackTests` 锁定（天然同支收窄，零改零测缺口） |
| SameBranch 退回后血缘保持、join 仍认亲齐批 | C-T3 `SameBranch_Strips*_JoinStillRecognizesKin` + 嵌套版 |
| E-WF-019 拒绝路径 | C-T3 `SiblingBranch_Throws_E_WF_019_NothingMutated` |
| default 边全假才走 / 有真不走 | A-T3 `TwoOfThreeTrue_*` / `AllConditionsTrue_*` / `AllFalse_*` |
| QA harness（设计器拖拽/配 prune/校验报错/i18n） | E-T2 剧本 7 条 |

### 执行顺序与依赖

```
H-A: A-T1 → A-T2 → A-T3 → A-T4
H-B: B-T1 → B-T2 → B-T3        （依赖 H-A：剪枝依赖动态计票 + Pruned + 血缘辅助 + OnBranchReject POCO）
H-C: C-T1 → C-T2 → C-T3        （依赖 H-A：作用域/剥离层依赖 TokenLineage/FlowGraph/SnapshotTokens）
H-B ‖ H-C 可并行（不同 executor 各占一支时注意 FlowEngine.Tokens.cs 的 CancelPendingTasksOfToken 以先落者为准）
H-D: D-T1 → D-T2               （依赖 A-T4 的 OnBranchReject POCO/校验语义 + B 波语义定稿）
H-E: E-T1 → E-T2 → E-T3        （紧跟 H-D，不留窗口）
```









---

## 波② 完成记录（2026-07-13, 主控追记）

15任务全过逐任务审查 + E-T3 DoD验收 + fable全支终审（With fixes→修复022d525→复核Ready=Yes）。终审唯一Critical=A-T2动态计票×split单相spawn接缝：split出边直连join的合法schema下join提前放行（审批旁路）+双重放行+实例永久Running（终审者双拓扑独立repro实证，属波前parallel行为回归）→修复=两阶段spawn（先全部SpawnToken再逐一EnterNode，ParallelSplit/InclusiveSplit两handler，join侧零触碰）+direct-edge×2回归测试+E-WF-012重排pin测试+doc孤儿归位。复核确认：双直连极端Consume幂等成立（ConsumeToken Active守卫+arrivedCount==0早退）、无新缝隙、sync serviceTask同类提前放行被隐式一并修复（正向副产品）。最终门禁：后端1890绿/5skip（1887+3）+EF clean+前端420/type-check 0/build过+27不变量零改+零迁移零Space污染。

### 跟踪票（波②遗留）

1. **生产库存量扫描（部署清单项）**：对各租户库 `Wf_FlowDef.SchemaJson` 扫一次「split出边直连join」形状——存量流程有此形状即修复前就会双放行的高危单，需人工核对履历。
2. **QA harness 第8剧本**：direct-edge拓扑（parallel+inclusive）+ sync serviceTask冲join同类形态，入 docs/superpowers/qa/wfs-kernel-hardening 剧本池（终审建议）。
3. **B-T3 pre-existing**：默认cascade驳回留兄弟Pending孤儿待办（status gate使其无害；坍缩路径现比默认路径干净的有意不对称）——引擎清洁度票。
4. **B-T1 UX措辞**：剪枝通知文案「被驳回」与整单驳回易混——信箱spec时统一措辞。
5. **D-T1 021c**：前端onBranchReject值比较大小写敏感未trim（后端归一化）——设计器恒发精确值今日无害，JSON导入硬化票一并收。
6. **复核备忘（不记票级）**：双直连极端下第二次Enter回写inst.CurrentNode漂移——既有展示层小瑕疵非本波引入。
7. **live QA**：E-T2 harness 7剧本 written-not-run，待用户在场执行（隔离库CP6DB_OA）。
