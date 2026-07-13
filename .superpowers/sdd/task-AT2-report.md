# Task A-T2 报告：GatewayJoinHelper 动态计票 + parallelJoin 改造（D4，回归锁定）

**Status:** ✅ 完成并 push
**Commit:** `e9da125`（分支 `feat/wfs-kernel-hardening`，前置 A-T1=dbececb/81a92a5）
**依赖契约:** A-T1 的 `FlowEngine.SnapshotTokens` / `TokenLineage.CrossesFork` 逐字引用，零改。

## 落码清单（3 文件，与计划 File Structure 精确一致）
- Create `CP6.Core/Services/Wf/NodeHandlers/GatewayJoinHelper.cs` — 双 join 共享动态计票放行（D4）
- Modify `CP6.Core/Services/Wf/NodeHandlers/ParallelJoinNodeHandler.cs` — 改为纯委托（删私有 AllTokens + 静态计票，-29/+3 行）
- Create `CP6.Tests/Wf/DynamicJoinCountTests.cs` — 2 定点回归测试
- `git diff --stat` 复核：仅上述 3 文件，零跨模块污染（未碰 Space/其他模块）。

## GatewayJoinHelper 签名（A-T3 InclusiveJoinNodeHandler 直接复用）
```csharp
internal static class GatewayJoinHelper
{
    public static Task TryReleaseAsync(NodeContext ctx, string historyAction);
}
```
- inclusive join 接线：`InclusiveJoinNodeHandler.OnEnterAsync(ctx) => GatewayJoinHelper.TryReleaseAsync(ctx, "inclusiveJoin")`（与 parallelJoin 唯一差异是 historyAction 字符串，计票逻辑共用）。
- 放行判据（同 ForkId 分支）：到场 `arrivedCount≥1` 且 不存在「穿过本 forkId 批次」的其他在途 Active token（停在本 join 的到场 token 除外）→ 血缘感知，防「A 到场、B 内层子 fork 在途」误放行。
- 退化护栏（`ctx.Token.ForkId is not Guid`，即 null）：沿用旧静态入边计票 `nullArrived < inEdges → return`，与旧 ParallelJoinNodeHandler 字节等价。
- 放行机制：消费同批到场 token + 「上弹一层」血缘续 token（parent=祖父、fork=父 ForkId）沿 join 单出边 `AdvanceToken` 继续 —— 原机制原样保留。

## 红绿证据
| 阶段 | 命令 | 结果 |
|---|---|---|
| Step2 基线（等价测试须旧实现下先绿） | `--filter "DynamicJoinCountTests\|ParallelGatewayTests"` | **7 passed**（5 ParallelGateway + 2 新，旧 impl 下即绿 → 确证等价性） |
| Step4 改后 PASS | `--filter "DynamicJoinCountTests\|ParallelGatewayTests\|FlowConcurrencyTests"` | **10 passed / 0 failed**（2 动态 + 5 并行语义铁闸零改动全绿 + 3 并发重试复算 join 计数） |
| Step5 全量 Wf 闸 | `--filter Wf` | **212 passed / 0 failed**（既有不变量测试零断言改动全绿） |
| 全量回归 | 全套 | **1850 passed / 5 skipped**（基线 1848 → +2 新测，5 skip=SQLite 既知） |
| EF clean | `ef migrations has-pending-model-changes` | **No changes**（零迁移） |

## 退化护栏专测证据
`DynamicJoinCountTests.NullFork_LinearTokenAtJoin_KeepsLegacyStaticCount_ParksForever`：
- 怪异 schema——join 有 2 条入边，但 token 沿线性路径（无 split）到达，`ForkId==null`。
- 审 a 后 join 到场 1 < 入边 2 → 断言实例仍 `Running` + join 处存 Active token（永停泊）。
- 若朴素动态判据（不做 null 退化）会在此立即放行 → 行为漂移；本测试 + `ForkId is not Guid` 退化分支联合锁死 bit 级等价。

## 嵌套在途防提前放行证据
`DynamicJoinCountTests.NestedInFlight_OuterJoinWaits_UntilInnerSubtreeDone`：
- 先审外层 b 支 → 外层 A 支此刻在内层子 fork 在途（同外层 ForkId 无 Active，只有血缘链穿过）。
- 断言外层 join 到场 b 后仍 `Running`（血缘感知 `CrossesFork` 挡住）；直至内层 a1/a2 齐 → innerJoin 上弹 → 外层齐 → `Approved` 且无 Active token 残留。

## 疑虑
无。ParallelGatewayTests 5 个并行语义铁闸零改动全绿（等价性确证），全量 +2 无回归，EF clean，diff 仅 3 在册文件。
