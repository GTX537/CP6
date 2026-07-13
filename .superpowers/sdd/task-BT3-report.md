# Task B-T3 报告：全剪光嵌套递归上弹（内层全剪 × 外层 prune/cascade 两态）

**分支**：feat/wfs-kernel-hardening ｜ **commit**：429a423（已 push）

## 落码

| 文件 | 动作 | 说明 |
|---|---|---|
| `CP6.Tests/Wf/BranchPruneNestedTests.cs` | 新增 | 嵌套矩阵 3 用例（照计划逐字） |
| `CP6.Core/Services/Wf/FlowEngine.Prune.cs` | 改（+18） | 补 `CancelAllPendingTasks` 并在坍缩 else 分支调用 |

## 红绿实况

首跑（仅新增测试，未改实现）：**2 直接绿 / 1 红**。B-T2 落地的 `ReleaseOrCollapseAsync` 递归上弹分支基本可用，只暴露一处清场缺口。

| 用例 | 首跑 | 说明 |
|---|---|---|
| `InnerAllPruned_OuterPrune_PrunesOuterBranch_SiblingCompletes` | ✅ 直接绿 | 递归上弹（外层 prune）逻辑完整无缺 |
| `InnerAllPruned_OuterCascade_InstanceRejected` | ❌ 红 → 修后绿 | 见下 |
| `InnerOnePruned_OneApproved_InnerJoinBackfills_OuterContinues` | ✅ 直接绿 | 补放行与递归边界无缺 |

### 暴露的缺陷（红 → 修）

失败断言：`Assert.False(b 支任务仍 Pending)`（Actual=True）。

根因：全剪光递归上弹到「外层 cascade / 无外层」→ 走 `ReleaseOrCollapseAsync` 的 else 坍缩分支，该分支 `CancelAllActiveTokens`（清 token + Pending ServiceJob）+ `VoidPendingFormTos`（清履历），但**未清兄弟支遗留的 Pending 待办**。嵌套场景里外层兄弟支 `b` 从未被剪（只有内层 x1/x2 各自 `CancelPendingTasksOfToken` 清了自己的待办），坍缩时 `b` 的待办成孤儿滞留 Pending。

修法（仅 `FlowEngine.Prune.cs`）：坍缩 else 分支加 `CancelAllPendingTasks(inst.Id)`（Local + localIds-exclusion 惯用法，镜像 `CancelPendingTasksOfToken`，实例级清 Pending/Suspended 待办）。**仅走剪枝坍缩路径**——既有默认 cascade（`ActOnceAsync` 的 `!pruned` 分支）一行未改，`Parallel_RejectTerminates` 等默认路径不断言兄弟待办状态，零 diff 无回归。

## 嵌套两态行为矩阵

内层 `inner`=prune 恒定；内层 x1、x2 先后驳回 → 内层全剪光 → 上弹外层：

| 外层 `outer` 策略 | 上弹处置 | 实例终态 | PrunedCount | branchPruned 履历 | 外层兄弟 b |
|---|---|---|---|---|---|
| `prune` | 剪外层 inner 支（记痕+通知+继续递归收束，b 支不倒） | Running → b 过后 Approved | 3（x1+x2+inner 支） | 3 | 存活，可续办 → 外 join 只等活支 |
| `null`（=cascade） | 整单驳回，连坐清场 | Rejected（RejectedCount=1，终态分发照走 FlowRejectedAsync） | 2（x1+x2） | 2 | 连坐清场：token Cancelled / **任务 Cancelled（本次补 fix）** / Pending 履历 Voided |

补放行边界回归（第 3 用例）：内层剪一支 + 另一支已到场 ij 停泊 → join 补放行（同批 token Consumed）→ 上弹属上层批次，**不误判全剪光**（PrunedCount=1，无递归记痕），外层继续等 b → b 过 → Approved。

## 验证

- `--filter BranchPruneNestedTests`：3/3 绿
- `--filter Wf`：236/236 绿
- 全量：**1874 passed / 5 skipped**（基线 1871 +3 新增，零回归）
- `ef migrations has-pending-model-changes`：**clean**（No changes / 零迁移）
- `git diff --cached --stat`：仅 2 文件，无 Space/跨模块污染

## 疑虑

无。缺陷即修，修面严格锁在剪枝坍缩路径，默认 cascade 零 diff。补 fix 使「剪枝坍缩」比既有默认 cascade 的兄弟待办清场更彻底（默认 cascade 留 Pending 孤儿待办，实例已 Rejected 故 `ActOnceAsync` 状态闸拦截无害）——这是计划测试所期望的更干净语义，非行为漂移。
