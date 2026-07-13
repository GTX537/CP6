# Task C-T3 报告：SendBackToNodeAsync 三规则接线 + E-WF-019 + 集成矩阵

Status: DONE / commit eedb054（push 完成，feat/wfs-kernel-hardening）
（注：本文件覆盖了同名的 WFS-service-task 旧波 C-T3 报告——那是另一波次同编号任务，已过时。）

## 红绿证据
- 红（实现前，`--filter SendBackThreeRuleTests`）：Failed 5 / Passed 1。SameBranch/BeforeSplit 场景现状抛 `E-WF-012`（CrossesParallelBlock 一刀切禁令）；Sibling 场景抛 `E-WF-012` 而非 `019`；Starter（天然 BeforeSplit）已过。
- 绿（实现后，同 filter）：Passed 6 / Failed 0。
- 全 Wf：Passed 249 / 0。
- 全量：**Passed 1887 / Skipped 5**（基线 1881 + 本任务 6 新用例）。
- EF：`has-pending-model-changes` → "No changes have been made to the model since the last migration."（clean，零迁移）。

## 三规则行为矩阵（parallel + inclusive + nested 全覆盖）
| 场景 | 目标 | 结果 |
|---|---|---|
| SameBranch（a2→a1，parallel） | 分支内上游 | 仅剥离本支子树重生，携剥离层血缘（ForkId 保留）；兄弟 b1 零扰动；重走后 join 认亲 → Approved |
| BeforeSplit（a2→n0，parallel） | split 之前 | 全清场，单根重生 n0（ParentTokenId/ForkId=null）；b 支任务/FormTo 全清 |
| SiblingBranch（a2→b1） | 兄弟支 | 抛 E-WF-019，先校验后写（任务/token/履历零突变） |
| Starter（parallel 支上退回发起人） | starter | 天然 BeforeSplit：全清场 + 回 Draft |
| Inclusive SameBranch + Sibling 目标 | — | 同支剥离兄弟存活+齐批 Approved；兄弟目标 E-WF-019 |
| Nested（x1→h1，剥离外层支） | 内层 split 之前 | 内层兄弟 x2 连带剥离、外层兄弟 b 不倒、重生 h1 携外层 ForkId → Approved |

## 线性现状铁闸证据（零 diff）
`--filter "AdvancedFlowTests|SerialSendBackTests|ParallelGatewayTests"` → Passed 23 / 0，既有断言一个未改。线性流 `task.TokenId` 对应 token 的 fork 栈为空 → Analyze 返回 BeforeSplit → 走原全清场代码块（逐字保留）。ParallelGatewayTests 动态计票等价性铁闸绿。

## 实现要点
- `AdvancedFlow.cs` `SendBackToNodeAsync` 按计划三规则接线；删除死方法 `CrossesParallelBlock` / `NodesBetween`。
- SameBranch：`CancelTokenSubtree(strip.Id)` 局部清场 + `SpawnToken(parent: strip.ParentTokenId, fork: strip.ForkId)` 携血缘重生。
- BeforeSplit/线性：既有全清场（parent/fork=null 归零重生）逐字保留。
- `SendBackToPrevStageAsync` / `SendBackToStarterAsync` 零改。

## 与计划的一处必要偏差（评审须知）
计划 Step 3 给出的代码把 `IsUpstreamReachable` 闸留在作用域分析**之前**。实测该顺序对**兄弟支目标**会先抛 E-WF-012（兄弟支天然不在 current 上游，IsUpstreamReachable=false），永远到不了 E-WF-019，两个 Sibling 用例因此失败。
修正：作用域分析 + `SiblingBranch → E-WF-019` 抢先命中，`IsUpstreamReachable → E-WF-012` 下移一行，兜住 BeforeSplit/SameBranch 的伪（非上游）目标。语义完全等价于计划意图（先校验后写不变、E-WF-012 对真正非法目标仍抛），仅调整两行判定顺序，已加注释说明。

## 疑虑
- 无。CancelTokenSubtree 对 strip 根置 Cancelled（非 Consumed），C-T2 审查备忘曾提「strip 应 consumed」；实测 join 认亲（SameBranch/Nested/Inclusive 三例重走均达 Approved）不受影响，从行为面证伪该顾虑，按计划 CancelTokenSubtree 口径落码。
- 提交仅含 `AdvancedFlow.cs` + 新测试文件；工作区另有 CT2/CT3 md 的 LF→CRLF 换行 churn（非本任务改动）未纳入提交。
