# A-T4 报告：FlowGraph 配对辅助 + 校验 E-WF-020/021

**Status**: DONE ✅  commit `5e2e769`（已 push feat/wfs-kernel-hardening）

## 红绿证据
- RED：写 `InclusiveValidatorTests.cs`（9 用例）后 `dotnet build` → 4× CS1061「FlowNode 无 OnBranchReject」编译失败（预期红）。
- GREEN：实现后 `--filter InclusiveValidatorTests` → 9 passed / 0 failed。
- Wf 闸：`--filter Wf` → **226 passed / 0 failed**。
- 全量：`dotnet test` → **1864 passed / 5 skipped**（基线 1855 + 本 Task 9 新 = 1864，只增不减）。
- EF：`has-pending-model-changes` → "No changes have been made to the model"（clean，零迁移，只增 POCO 可空字段）。
- 跨模块污染：`git show --stat` = 4 文件全在 `CP6.Core/Services/Wf` + `CP6.Tests/Wf`，零 Space/其他模块触碰。

## 落码清单
1. `FlowSchema.cs`：`FlowNode` 服务任务字段块后加 `public string? OnBranchReject { get; set; }`（spec §2.1 逐字，可空向后兼容）。
2. `FlowGraph.cs`（新建，internal static）：`IsJoinType` / `ReachableFrom` / `NearestCommonJoin` / `BranchDomain`，全 BFS 环路安全。
3. `FlowSchemaValidator.cs`：⑨ 错误出边规则之后、④ 可达性之前插入 ⑩ inclusive 网关段（E-WF-020 + E-WF-021a/b/c）。

## NearestCommonJoin 签名（C-T1 退回作用域分析须用同一口径，不许漂移）
```csharp
// FlowGraph.cs（internal static class FlowGraph）
public static FlowNode? NearestCommonJoin(FlowSchema schema, FlowNode split);
public static HashSet<string> ReachableFrom(FlowSchema schema, string startId);
public static HashSet<string> BranchDomain(FlowSchema schema, string edgeTargetId, string pairedJoinId);
internal static bool IsJoinType(FlowNode n);   // "paralleljoin" or "inclusivejoin"（ToLowerInvariant）
```
口径：split 各出边（`IsError != true`）可达集交集 ∩ join 型节点中，距 split BFS 深度最近者；无出边/无公共 join → null（校验报 E-WF-021；退回分析保守拒 E-WF-012）。

## 与波① T4/T5 校验共存证据
- 波① T4/T5 动的是 ⑧ 服务任务段（`ContainsUnsupportedSubscript` 下标校验）+ `KnownServiceModes` 值域（⑧ 内 `n.ServiceMode` 分支）。本 Task 新增 ⑩ 段落纯追加在 ⑨ 之后、④ 之前，未触碰 ⑧/`KnownServiceModes`/`KnownServiceKinds` 任一行。
- `errs.Distinct().ToList()` 收口不变；`T(n)` 小写归一化沿用既有。
- 回归证据：全量 1864 绿含既有 `ServiceTaskValidatorTests` 全数通过，⑧⑨ 值域/下标校验零回退；`ParallelGatewayTests` 等 27 不变量测试全绿。

## 疑虑
无。NearestCommonJoin 已按共享契约逐字实现，C-T1 可直接复用。E-WF-020 用 `break`（首个违规即报一次），与既有分节风格一致；多 split 场景每个独立 continue（021a）保证全覆盖。
