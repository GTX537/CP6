# C-T1 报告：SubFlowCascade 三处挂钩（父终止递归 / 第五清 / 撤回级联）

- **分支/提交**：`feat/wfs-subflow` @ `ddff21ae`（已 push）
- **测试**：RED 实录 3/3 FAIL（钩子未挂，撤回/连坐后子实例仍 Running）→ GREEN 3/3；SubFlow 全家 45 绿；Wf 闸 389 绿（含二期 `TokenSubtreeCancelTests`/`BranchPruneTests`/既有 `WithdrawCleanupTests` 照绿）；**全量 2155 绿 / 5 skip**（基线 2152 + 本任务 3）。drift clean（`has-pending-model-changes` = No changes）。零迁移零实体改动，既有 Wf 测试零改写。

## 1. SubFlowCascade.cs 现状

B-T2 预落体与 brief Step 3 **逐字一致**（`CancelChildrenOfToken` + `CancelInstanceTree` + 类注释含互指段），无增量方法需补——本任务对该文件**零改动**，纯做三处消费接线。

## 2. 三挂钩落点

| # | 路径（spec §3.3） | 文件与落点 | 形态 |
|---|---|---|---|
| ① | 实例终止/全清场/剪枝坍缩 | `CP6.Core/Services/Wf/FlowEngine.Tokens.cs` `CancelAllActiveTokens`：两段 token 置 Cancelled 循环改为「置 Cancelled + 收集 id」（`cancelledTokenIds`，既有行为 bit 级不变只多收集），方法末尾（B-T3 job 清场块**之后**）`foreach (var id in cancelledTokenIds) SubFlowCascade.CancelChildrenOfToken(_db, id);` | 驳回连坐 / 退回全清场 / Prune 递归上弹坍缩（`ReleaseOrCollapseAsync` 尾部调本方法）全覆盖 |
| ② | SameBranch 剥离（第五清） | 同文件 `CancelTokenSubtree`：per-token 清理循环体第四清后追加 `SubFlowCascade.CancelChildrenOfToken(_db, id);`；XML 注释四清→五清 + 互指「见 SubFlowCascade 类注释」 | 停泊 subFlow token 被退回剥离时子实例组级联；兄弟支零扰动（测试 3 pin 住） |
| ③ | 撤回 | `CP6.Core/Services/Wf/TaskCenterService.cs` `WithdrawAsync`：activeTokens 置 Cancelled 循环之后 `foreach (var t in activeTokens) SubFlowCascade.CancelChildrenOfToken(_db, t.Id);` | 撤回=terminate 就地循环不经 CancelAllActiveTokens（侦察结论10）→ 此处补级联 |

互指注释第四点：`CP6.Core/Services/Wf/FlowEngine.Prune.cs` `PruneTokenAsync` 方法头追加两行注释（零逻辑改动）——prune 只剪「被驳任务的 token」，停泊 subFlow token 无任务不流入此处，其级联由三钩子负责（侦察结论 11a 闭合论证落档）。

## 3. 递归终止论证

- `CancelInstanceTree` 收集本实例被取消 token → 逐 token `CancelChildrenOfToken` 递归孙代；`CancelChildrenOfToken` 只对 `Status ∈ {Running, Suspended, Draft}` 的子实例递归，进入即置 `Withdrawn`（终态）→ 同一实例不可能二次进入（回边即命中终态守卫短路）。
- 实例树是按 `ParentTokenId` 外键的有向结构，B 波 `SubFlowRefValidator` 防环 DFS 保证 FlowKey 引用无环 → 运行期实例树深度有限（`SubFlowLimits` 封顶），递归必终止。
- 测试 1 以三层树（top→mid→leaf）实证：撤回 top 后 mid/leaf 全 Withdrawn、零 Active token、零 Pending 待办、leaf 有 `subFlowCascadeCancelled` 履历。

**不回注保证**：级联走 `CancelInstanceTree` 不调 `SubFlowResume.EnqueueIfChild` → 测试 1 断言零 `subFlowResumed` 履历 + 零 Pending `subFlowResume` 凭据成立（父已在终态，回注无意义且有害；`CheckSubFlowGroupAsync` 状态闸双保险）。

## 4. 与 B-T3 既挂钩子的相对顺序（WithdrawAsync 内，自上而下）

1. token 置 Cancelled 循环（既有）
2. **★ C-T1 级联钩子（本任务新增）**——用同一 `activeTokens` 列表逐 token 级联
3. FormTo Voided（既有）
4. B-T3 pendingJobs 清理（Pending job → Cancelled）
5. B-T3 `SubFlowResume.EnqueueIfChild`（本实例若是子实例 → 给父投凭据）
6. SaveChangesAsync → B-T3 fast path（`FastPathSubFlowResumeAsync`）

顺序安全性：级联钩子在 pendingJobs 清理**之前**执行，级联对**子实例**的 Pending job 清理在 `CancelInstanceTree` 内部自带（按子实例 InstanceId 过滤，与步骤 4 按本实例过滤不重叠）；步骤 5 的凭据 InstanceId=父实例，不被 4 误杀（B-T3 既有注释论证保持成立）。级联产生的 Withdrawn 不入队 → 与 5 的「本实例作为子」入队互不干扰。B-T3 两钩子零触碰。

`CancelAllActiveTokens` 内相对顺序：级联钩子在 B-T3 job 清场块**之后**（brief 明示），级联递归内部再按子实例清 job——同款不重叠。

## 5. 默认路径等价

无子实例 token 的 `CancelChildrenOfToken` = Local 空 + 一次 `ParentInstanceId==parentTokenId` 无命中查询 → 零行为（Global Constraints 口径）。`CancelAllActiveTokens` 的收集改动 bit 级等价（只多一个 List）。既有 `WithdrawCleanupTests`/`TokenSubtreeCancelTests`/`BranchPruneTests` 全绿实证。

## 6. C-T2 交接

- 三钩子已闭合全部 token 死亡路径（终止/剥离/撤回）；prune 单 token 路径经注释论证无缺口（停泊 subFlow token 无任务永不被直接剪）。
- `SubFlowCascadeTests` 已 pin：三层递归+不回注、驳回连坐级联、第五清兄弟零扰动。C-T2（退回重生防双批组合语义）可直接消费 `ParallelParent` 私有 schema 形态（如需可提为 harness 公共方法）。
- 若 C-T2 引入新 token 清场路径，必须按 `SubFlowCascade` 类注释末条同步审视接缝。

改动面：`FlowEngine.Tokens.cs`(+11/-4) / `TaskCenterService.cs`(+3) / `FlowEngine.Prune.cs`(+2 注释) / `SubFlowCascadeTests.cs`(+112 新)。
