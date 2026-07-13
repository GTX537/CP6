# Task A-T3 报告：InclusiveSplit/InclusiveJoin 两 handler + 注册（字典第 7/8 个）

**Status: DONE** — 全绿，已 push。

## Commit
- `04a28df` feat(wfs-hardening): A-T3 InclusiveSplit/Join两handler+DefaultHandlers/DI注册(字典第7/8个)
- 分支 `feat/wfs-kernel-hardening`，已 push（`7cf88c7..04a28df`）。
- 5 files changed, 258 insertions(+), 2 deletions(-)：
  - Create `CP6.Core/Services/Wf/NodeHandlers/InclusiveSplitNodeHandler.cs`（第 7 个 handler）
  - Create `CP6.Core/Services/Wf/NodeHandlers/InclusiveJoinNodeHandler.cs`（第 8 个 handler）
  - Modify `CP6.Core/Services/Wf/FlowEngine.cs`（DefaultHandlers 加 2 项，注释「五 handler」→「八 handler」）
  - Modify `CP6.WebApi/Program.cs`（ServiceTask 注册行后加 2 行 DI）
  - Create `CP6.Tests/Wf/InclusiveGatewayTests.cs`（5 用例，照计划全文）

## 红→绿证据
- **红**（Step 2，实现前）：`--filter InclusiveGatewayTests` → Failed 5 / Passed 0。异常正是计划预期 `System.InvalidOperationException : 未知节点类型：inclusiveSplit（节点 isplit）`，抛于 `FlowEngine.EnterNodeAsync:273`。
- **绿**（Step 4，实现后）：
  - `--filter InclusiveGatewayTests` → **Passed 5 / Failed 0**。
  - `--filter Wf` → **Passed 217 / Failed 0**（含新增 5，27 既有不变量断言零改动全过）。
  - 全量 `dotnet test` → **Passed 1855 / Skipped 5 / Failed 0**（基线 1850+5 新用例=1855，只增不减）。
  - `dotnet build CP6.WebApi` → 0 Warning / 0 Error。
  - `ef migrations has-pending-model-changes` → **No changes / clean**（零迁移）。

## Handler 注册证据
- `FlowEngine.DefaultHandlers()`（单测 fallback 路径）：数组尾追加 `new InclusiveSplitNodeHandler(), new InclusiveJoinNodeHandler()`，字典第 7/8 个，`StringComparer.OrdinalIgnoreCase` 键控（Type=`inclusiveSplit`/`inclusiveJoin`）。
- `Program.cs`（生产 DI 路径）：`AddScoped<INodeHandler, InclusiveSplitNodeHandler>()` + `...InclusiveJoinNodeHandler>()`，紧随 ServiceTask 注册行。两路径分发口径一致。
- 未在 parallel handler 上加 mode 旗标（遵 D3：独立节点类型对，不合并 handler）。

## 条件求值走的哪个既有机制
- **`ExpressionEvaluator.Evaluate(edge.Condition, inst.VarsJson)`** —— 与既有条件边同口径。
- 关键落码细节：**先分组再求值**。空表达式在 `ExpressionEvaluator` 里恒真，故 inclusiveSplit 先把出边按 `string.IsNullOrWhiteSpace(Condition)` 分成 `condEdges`（条件边）与 `defaults`（唯一无条件 default 兜底边），**只对 condEdges 求值**得真边集 T；T 非空→激活 T（default 不走），T 空→取 `defaults.Take(1)`（BPMN default 语义，非恒真必走边）。这样 default 边不会因空表达式恒真而误判为真边。
- `e.IsError != true` 过滤沿用 error 边惯例，排除错误边参与激活。

## 血缘/放行机制复用
- **InclusiveSplit** 生 token 机制与 `ParallelSplitNodeHandler` 完全相同：`eng.SpawnToken(inst, target, parent: ctx.Token.Id, fork: forkId)`，同批共享一个 ForkId、ParentTokenId=当前 token。
- **InclusiveJoin** 直接委托 A-T2 契约 `GatewayJoinHelper.TryReleaseAsync(ctx, "inclusiveJoin")`，与 parallelJoin 共用 D4 血缘感知动态计票（活支==实际激活边数）。inclusive 场景天然只生成真边 token → join 只等真走的分支，标准解。
- 嵌套双向（inclusive⊂parallel、parallel⊂inclusive）两用例验证血缘感知：外层 join 正确等待内层子树在途 token，内层 join 放行后续 token 恢复外层血缘继续被追踪。

## 落码纪律核对
- 27 既有 Wf 不变量测试零改动（全量绿）。
- 零迁移、EF clean、零跨模块污染（`git diff --stat` 仅 Wf handlers/FlowEngine/Program.cs/新测试）。
- handler 内不自行 SaveChanges（沿黄金模板铁律③，落库由 ActAsync/SubmitAsync 外壳收口）。

## 疑虑
无。A-T3 独立 handler 落地，E-WF-020 静态校验（default 边存在且唯一）由后续 FlowSchemaValidator 任务落地；本 handler 内保留防御式兜底（激活集为空→抛 `E-WF-020: ... 无可激活出边`），校验漏网属 bug 时 fail-loud 不静默。
