# Task A-T1 Report — Pruned 常量 + SnapshotTokens 抽取 + TokenLineage 血缘辅助

**Status: DONE** — commit `dbececb`（已 push feat/wfs-kernel-hardening）

## TDD 红绿

- **RED**：`dotnet test --filter TokenLineageTests` → 编译失败（CS0117 `FlowTokenStatus.Pruned` 不存在 + CS0103 `TokenLineage` 不存在，8 处）。符合预期。
- **GREEN**：最小实现后 `--filter TokenLineageTests` → 5/5 passed（43ms）。
- **Wf 闸**：`--filter Wf` → 210/210 passed（含 27 既有不变量测试全绿，零断言改动）。
- **全量**：`dotnet test CP6.Tests` → **1848 passed / 5 skipped**（基线 1843 + 本波新增 5，5 skip = SQLite 既知）。
- **EF clean**：`ef migrations has-pending-model-changes` → "No changes have been made to the model since the last migration."（零迁移达成）。

## 落码清单（4 文件 / +123 行）

| 文件 | 改动 |
|---|---|
| `CP6.Core/Services/Wf/WfStatus.cs` | `FlowTokenStatus` 加 `public const int Pruned = 3;`（+2 行） |
| `CP6.Core/Services/Wf/FlowEngine.Tokens.cs` | 新增 `internal IReadOnlyList<Wf_FlowToken> SnapshotTokens(Guid)`，置于 `CancelAllActiveTokens` 之前（+8 行） |
| `CP6.Core/Services/Wf/TokenLineage.cs` | 新建，`internal static class TokenLineage` 四纯函数（+44 行） |
| `CP6.Tests/Wf/TokenLineageTests.cs` | 新建，5 测试（+69 行） |

## 契约签名清单（后续 14 任务照此，不许漂移）

```csharp
// WfStatus.cs — FlowTokenStatus
public const int Pruned = 3;

// FlowEngine.Tokens.cs — partial FlowEngine
internal IReadOnlyList<Wf_FlowToken> SnapshotTokens(Guid instanceId);
//   口径 = Local(本实例) ∪ DB(本实例).AsEnumerable() → Distinct().ToList()
//   与 ParallelJoinNodeHandler.AllTokens 逐字同口径（引用去重，EF 身份映射保证同实体同引用）

// TokenLineage.cs — internal static class TokenLineage（全纯函数）
public static IEnumerable<Wf_FlowToken> AncestorChain(IReadOnlyList<Wf_FlowToken> all, Wf_FlowToken t);
//   t 自身 + ParentTokenId 上溯全部祖先（自内向外，yield），visited 环路防御
public static bool CrossesFork(IReadOnlyList<Wf_FlowToken> all, Wf_FlowToken t, Guid forkId);
//   ⇔ AncestorChain 上任一 token.ForkId == forkId
public static Wf_FlowToken? ForkParent(IReadOnlyList<Wf_FlowToken> all, Wf_FlowToken t);
//   Id == t.ParentTokenId 的 token；其 NodeId 即该 fork 批次的 split 节点（§4.1 定案）；t 无父 → null
public static List<(Wf_FlowToken BranchToken, Guid ForkId, string SplitNodeId)> ForkStack(
    IReadOnlyList<Wf_FlowToken> all, Wf_FlowToken t);
//   内→外；祖先链每个 ForkId 非空 token 贡献一层，同 forkId 取最靠 t 者；血缘断裂层跳过
```

## 漂移适配说明

- 波①动过 Wf 域，但本 Task 所触方法/常量位置与计划一致，行号无实质漂移。`SnapshotTokens` 插入点（`CancelAllActiveTokens` 之前）按锚点匹配，非行号硬编码。
- 全部代码照 brief「Step 3 最小实现」逐字落码，无自由发挥。
- 侦察定案表 §4.1（ForkParent(t).NodeId == split 节点 id）与 §5.3（Local ∪ DB 去重口径）已由 `ParallelJoinNodeHandler.AllTokens` 现存实现交叉核实一致。

## 疑虑

无。零跨模块污染（4 文件全在 Wf 域 + Wf 测试）。工作树 commit 后 clean。
