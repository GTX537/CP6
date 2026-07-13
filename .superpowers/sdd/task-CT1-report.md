# Task C-T1 报告：SendBackScopeAnalyzer 作用域纯函数

**状态**: DONE
**分支**: feat/wfs-kernel-hardening
**Commit**: `5102594` feat(wfs-hardening): C-T1 SendBackScopeAnalyzer作用域纯函数(三分+最内剥离层+E-WF-012保守拒) — 已 push

---

## 交付物

| 文件 | 职责 |
|---|---|
| `CP6.Core/Services/Wf/SendBackScopeAnalyzer.cs` (新增, 47 行) | `SendBackScope` 枚举 + `Analyze` 纯函数 |
| `CP6.Tests/Wf/SendBackScopeTests.cs` (新增, 170 行) | 6 个 Fact，schema+内存 token 直测，不动库 |

零迁移、零实体列变更、零跨模块污染（`git diff --cached --stat` 仅两文件 217 插入）。

---

## TDD 红绿输出

**RED（Step 2）** — `--filter SendBackScopeTests`：编译失败
```
error CS0103: The name 'SendBackScope' does not exist in the current context
error CS0103: The name 'SendBackScopeAnalyzer' does not exist in the current context
（+ 连带 CS8130 无法推断解构变量类型）
```

**GREEN（Step 4）** — `--filter SendBackScopeTests`：
```
Passed!  - Failed: 0, Passed: 6, Skipped: 0, Total: 6, Duration: 68 ms
```

**Wf 闸（Step 5）** — `--filter Wf`：
```
Passed!  - Failed: 0, Passed: 242, Skipped: 0, Total: 242, Duration: 8 s
```

**全量** — `dotnet test CP6.Tests/CP6.Tests.csproj`：
```
Passed!  - Failed: 0, Passed: 1880, Skipped: 5, Total: 1885, Duration: 1m 5s
```
基线 1874 passed / 5 skipped → 1880 / 5，**+6 恰为本任务新增用例**，27 既有 Wf 不变量断言零改动。

**EF clean** — `has-pending-model-changes`：`No changes have been made to the model since the last migration.`

---

## Analyzer 签名（C-T3 接线照此逐字）

```csharp
public enum SendBackScope { SameBranch, BeforeSplit, SiblingBranch }

public static (SendBackScope Scope, Wf_FlowToken? StripToken) Analyze(
    FlowSchema schema, IReadOnlyList<Wf_FlowToken> all, Wf_FlowToken current,
    string currentNodeId, string targetNodeId);   // 结构不可判定 → throw InvalidOperationException("E-WF-012")
```

与共享契约节 100% 一致。签名与 A-T4 `FlowGraph.NearestCommonJoin` / A-T1 `TokenLineage.ForkStack` 共用同一配对口径（E-WF-021 单一真相源）。

---

## 三规则判定矩阵（C-T3 消费口径）

算法：由 `current` token 血缘取 `ForkStack`（内→外），逐层用 split 的配对 join 切出各分支域，逐层判定：

| 返回 | 触发条件 | StripToken | C-T3 后续动作 |
|---|---|---|---|
| **SameBranch** | 目标节点落在「含 current 节点的最内层分支域」内（首个命中层即返回） | 该层分支代表 token（`branchToken`） | 剥离层子树清场 + 携 `(parent: strip.ParentTokenId, fork: strip.ForkId)` 血缘重生 |
| **SiblingBranch** | 目标只落在本层某兄弟分支域内（非 current 所在域） | `null` | 拒绝 → E-WF-019（语义永久禁止跨兄弟支退回） |
| **BeforeSplit** | fork 栈全部层都不含目标（含线性流无 fork：ForkStack 空，直接返回） | `null` | 现状全清场整块重来（放开 CrossesParallelBlock 禁令后复用现状代码块） |
| **throw E-WF-012** | 配对不可判定（split 无公共 join：环路/直通 end 的怪异 schema） | — | 保守拒绝（现状对跨网关退回本就拒，非收紧） |

**嵌套对称性**（`Nested_*` 用例锁定）：内→外逐层扫描保证 SameBranch 命中的是「包含目标的最内层」域，剥离层随之为该层分支代表 token。target=外层支内节点(h1) → strip=外层支代表(inner token h)；target=最内层域内(x1) → strip=最内层(x)。

---

## 关键判定逐例追踪（自证正确性）

- **Linear**：`ForkStack` 空 → 直接 `BeforeSplit`/null。
- **SameBranch(单层)**：ForkStack=[(a,f,"split")]；配对 join=join；域=[{a1,a2},{b1}]；current a2∈{a1,a2}，target a1∈{a1,a2} → SameBranch, strip=a。
- **BeforeSplit**：target n0 不在任何域（在 split 上游） → 落穿全部层 → BeforeSplit。
- **SiblingBranch**：target b1∈{b1}(兄弟域)、∉ current 所在 {a1,a2} → SiblingBranch。
- **Nested→h1**：内层(inner,join=ij,域[{x1},{x2}])不含 h1 → 上探；外层(outer,join=oj,域[{h1,inner,x1,x2,ij},{b1}])，current x1 与 target h1 同域 → SameBranch, strip=外层代表 h。
- **E-WF-012**：split→a→end / split→b→end，可达交集={end}，end 非 join 型 → `NearestCommonJoin` 返 null → throw。

---

## 漂移适配说明

无漂移。计划给出的 `Analyze` 实现全文逐字落地，未做任何签名/逻辑改动：

1. 依赖 A-T1 `TokenLineage.ForkStack`（(BranchToken, ForkId, SplitNodeId) 内→外）与 A-T4 `FlowGraph.NearestCommonJoin` / `BranchDomain` 均已在分支就位，签名与本任务调用点精确吻合，无需适配层。
2. `FlowNode.OnBranchReject` / `FlowTokenStatus.Pruned` 已由前置任务落库（本任务不触碰）。
3. 测试实体字段（`Wf_FlowToken.Id/InstanceId/NodeId/ParentTokenId/ForkId/Status`、`FlowNode.Id/Type/ApproverStrategy/ApproverUserId`、`FlowEdge.From/To/IsError`）均与现有 schema POCO 对齐，编译零告警新增。

## 疑虑

无。纯函数、零副作用、零库交互；C-T3 接线时按上表消费即可，注意 SameBranch 重生须携 `strip.ParentTokenId/ForkId` 血缘（对齐侦察定案 §5.3 现状 `SpawnToken(parent:null,fork:null)` 需改点），BeforeSplit/线性保留归零重生逐字现状。
