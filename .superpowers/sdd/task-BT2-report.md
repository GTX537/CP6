# Task B-T2 报告：驳回剪枝路径（触发分流 + PruneToken + join 补放行；cascade 零 diff）

## Status: DONE — commit `5c904e8`（已 push feat/wfs-kernel-hardening）

## 落码清单（4 文件，314 insert / 3 delete，零跨模块）
- **Create** `CP6.Core/Services/Wf/FlowEngine.Prune.cs`（99 行）— `TryPruneBranchAsync`/`FindSplitNode`/`IsPrune`/`IsJoinType`/`PruneTokenAsync`/`ReleaseOrCollapseAsync`。
- **Modify** `CP6.Core/Services/Wf/FlowEngine.Tokens.cs`（+13）— 新增 `CancelPendingTasksOfToken`（Local + localIds-exclusion 惯用法，镜像 CancelAllActiveTokens）。
- **Modify** `CP6.Core/Services/Wf/FlowEngine.cs`（ActOnceAsync 驳回 else 分支）— 包一层 prune 分流，cascade 三行原文保留。
- **Create** `CP6.Tests/Wf/BranchPruneTests.cs`（187 行，6 用例）。

## 红→绿证据
- **RED**（实现前 `--filter BranchPruneTests`）：`Failed: 3, Passed: 3`。3 个 prune 场景失败（现状连坐把实例打成 Rejected：SingleBranch 期望 Running 实得 Rejected / PrunedCount 期望 1/2 实得 0/2 等），cascade(Theory×2) + 线性 3 个等价基准测试在旧实现下即绿。
- **GREEN**（实现后）：`--filter BranchPruneTests` → `Passed: 6`；`--filter Wf` → `Passed: 233`（27 不变量含 ParallelGatewayTests 全绿）；全量 `Passed: 1871, Skipped: 5`（基线 1865 +6）。
- **EF clean**：`has-pending-model-changes` → "No changes have been made to the model since the last migration."（本波零迁移，只碰已落地 POCO/常量）。

## cascade 零 diff 证据
`Cascade_Default_ZeroDiff_RejectTerminatesWholeInstance` Theory 两态（`onBranchReject=null` 与 `"cascade"`）均断言：`inst.Status==Rejected` / Active token 数 0 / **Pruned token 数 0** / `PrunedCount==0` / **branchPruned 履历 0 条** —— 与 `ParallelGatewayTests.Parallel_RejectTerminates` 逐字等价终态。机制上：
- `ForkId==null`（线性/无 fork）→ 分流内 `pruned` 恒 false、**不 LoadSchema**，落回原三行（CancelAllActiveTokens + VoidPendingFormTos），字节等价（`Prune_LinearFlow_NoFork_FallsBackToCascade` 锁定）。
- `ForkId!=null` 但 split 为 cascade/null → `TryPruneBranchAsync` 在 `IsPrune(split)` 判假处返回 false，**未产生任何状态突变**（无 Pruned/无履历/无通知），同样落回原三行。
- ParallelGatewayTests 4+1 动态计票等价铁闸全绿 → 无剪枝旧场景 bit 级回归锁定。

## 补放行探测机制说明（B-T3 复用要点）
`ReleaseOrCollapseAsync(inst, schema, deadBranchToken, actorId, comment)` 剪本支后按 spec §4.2.3/§4.2.4 **顺序敏感**两步收束，`forkId = deadBranchToken.ForkId`：

1. **① join 补放行探测（必须先于剪光判定）**：`SnapshotTokens` 取同 `ForkId==forkId` 且 `Active` 的 token，按 `NodeId` 分组；对落在 join 型节点（`FlowGraph.IsJoinType` = parallelJoin/inclusiveJoin）的组，取一枚停泊 token `probe` **重入 `EnterNodeAsync`**（即 `GatewayJoinHelper.TryReleaseAsync`，计数幂等、重入安全——剪枝令 join 动态计票凑齐则齐批放行、消费停泊 token、续生上弹一层血缘 token 沿单出边继续）。**若探测中任一 `probe.Status` 变为 `Consumed`（join 已放行）→ 置 `released=true` 并立即 `return`**，绝不进入剪光判定。
   - 护栏理由（计划期新发现）：join 齐批放行后，同批 token 全 Consumed、续 token 属**上层**批次，故「无 Active 穿过本 fork 批次」也成立；若不加此 `released` 短路，会把正常收束误判为全剪光而递归驳回。`Prune_JoinBackfill_ParkedSiblingReleases_NoFalseCollapse` 锁定（先 b 过停泊 join、后驳 a 剪枝触发补放行 → Approved，`PrunedCount==1` / `RejectedCount==0`）。
2. **② 全剪光递归上弹（血缘感知，§3.3 同款判据）**：重取快照，若存在任一 `Active` 且 `TokenLineage.CrossesFork(all, t, forkId)` 的 token（含内层子 fork 在途）→ 本批次仍有活支，`return` 继续等。否则视同该 fork 续 token 被驳回，取 `forkParent = ForkParent(deadBranchToken)`：
   - `forkParent` 存在且其本层 `outerSplit` 配 prune → 记 `branchPruned` 履历 + 通知（以 `forkParent.NodeId` 为剪点），**递归 `ReleaseOrCollapseAsync(forkParent)`**（嵌套网关天然覆盖）。
   - 外层 cascade / 无外层 → `inst.Status=Rejected` + `CancelAllActiveTokens` + `VoidPendingFormTos`（既有终态；`DispatchIfFinished` 仍由 ActOnceAsync 尾部统一在 SaveChanges 前做，FlowRejectedAsync 照发）。`Prune_AllBranches_CollapsesToInstanceRejected` 锁定（`PrunedCount==2` / `RejectedCount==1`）。

**B-T3 接口约定**：补放行探测入口即 `EnterNodeAsync` 重入（无新 API），`GatewayJoinHelper.TryReleaseAsync` 的幂等计数是补放行地基；B-T3 若在 join 补放行后接续下游（含 serviceJob 停泊清场），`released` 短路点与 `②` 剪光递归上弹是唯一两条出口，勿在其间插入会二次改 `inst.Status` 的逻辑。

## 铁律遵守
- 引擎写路径三律：先校验（`IsPrune`/`ForkId` 判定在任何状态突变前）→ 幂等（join 重入计数安全、`Pruned` 幂等经 Active 守卫路径）→ handler/内部方法**绝不自行 SaveChanges**（全部改动随 ActOnceAsync 尾部既有 `_db.SaveChangesAsync()` 落库）。
- 剪枝绝不改 `inst.Status`（仅全剪光递归到顶无外层时走既有 Rejected 路径）；`DispatchIfFinished` 原子接缝未动。

## 疑虑
无。侦察定案（ParentTokenId 上溯定位 split、补放行≠全剪光护栏）全部按计划落码并经用例验证；27 不变量零改断言全绿。
