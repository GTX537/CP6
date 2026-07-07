# Task C-T2 Report: 样例 dataWriteback executor

**Status:** DONE
**Commit:** f593f19 `feat(wfs-service-task): C-T2 样例 dataWriteback executor`
**Branch:** feat/wfs-service-task-finish (off main base 4055dd4; not pushed)

## 改动文件清单（3 个，commit 内容与之字节一致）

| 文件 | 动作 | 说明 |
|---|---|---|
| `CP6.Core/Services/Wf/Executors/SampleDataWritebackExecutor.cs` | 新增 (+79) | 黄金模板 executor：`Key="sampleWriteback"`、`Kind=ServiceKind.DataWriteback`("dataWriteback")、`VisibleInDesigner=true`、`DisplayName="样例数据回写"` |
| `CP6.Tests/Wf/SampleDataWritebackExecutorTests.cs` | 新增 (+107) | 5 个 xUnit 测试（元数据 / happy / 幂等 / 缺字段 / 非数值） |
| `CP6.WebApi/Program.cs` | 修改 (+1) | DI 注册，紧邻 C-T1 WebApiExecutor/EchoConnector 之后 |

`picture/**`、`shots/**` 为会话开始前既存的未跟踪文件（非本任务产物），未纳入 commit，零 Space 污染。`git show --stat` 确认 commit 仅含上述 3 文件。

## 执行器行为

- 读表单 `$.amount`（经既有 `ServiceVarsHelper.ResolveValue`），`decimal.TryParse`（InvariantCulture）× 1 → 写回流程变量 `writebackEcho`。
- 发出幂等键 `writebackIdempotencyKey = wf-writeback-job-{JobId}`。
- 纯计算、无 I/O。错误码 `E-WF-019`（此前最高为 C-T1 的 E-WF-018，019/020 全库未占用，已 grep 确认）。

## 测试命令与输出摘要

**Step 2 FAIL（实现前）:**
`dotnet test CP6.Tests/CP6.Tests.csproj --filter SampleDataWritebackExecutorTests`
→ 编译失败 CS0246：`SampleDataWritebackExecutor` 未定义（预期的红）。

**Step 4 新测试 PASS:**
同上命令 → `Passed! - Failed: 0, Passed: 5, Skipped: 0, Total: 5, Duration: 449 ms`

**Step 4 Wf 硬闸:**
`dotnet test CP6.Tests/CP6.Tests.csproj --filter Wf`
→ `Passed! - Failed: 0, Passed: 136, Skipped: 0, Total: 136, Duration: 7 s`
（131 既有 Wf 测试全绿 + 5 新增；既有 Wf 测试字节等价，未改引擎执行态硬闸）

## 黄金模板三铁律如何体现（类头部注释块 + 代码内联，明标「后续 dataWriteback executor 照此模板复制」）

1. **先校验、再写** — `ExecuteAsync` 顶部先解析并校验 `amount`（缺失 / 非数值均在构造任何 `OutputVars` 之前 `ServiceTaskResult.Fail("E-WF-019 …")` 直接返回），Fail 路径下 `OutputVars` 为 null，不留半截脏改。测试 T4/T5 断言 `result.OutputVars` 为 null 加以锁定。
2. **幂等** — 输出为输入的纯函数（amount × 1）且携带 `wf-writeback-job-{JobId}` 幂等键；at-least-once 重投结果字节等价。测试 T3 连跑两次断言两次 `writebackEcho`/幂等键完全相同。
3. **绝不自行 SaveChanges / 不开事务 / 不发 HTTP** — executor 只 `return Ok(OutputVars)`，落库交给引擎原子接缝（引擎经 `ServiceVarsHelper.MergeOutputVars` 合并回 `inst.VarsJson` 后统一 SaveChanges）；外呼只属 webApi kind 经 IWfConnector。注释注明如需读 DB 可注入 CP6Context（同 scoped，spec §4.5）但仍不得自行 SaveChanges。

三律以 `<summary>` doc 注释块写在类头部，并在 ExecuteAsync 内以「── 律1/律2/律3 ──」分段注释对应到具体代码。

## 自查发现

- **DI 双注册合意**：`IServiceTaskExecutor` 现有两实现（WebApiExecutor + SampleDataWritebackExecutor），均 `AddScoped`。引擎按 Key 建执行器字典（契约注释「实现按 Key 注册到引擎执行器字典」），`IEnumerable<IServiceTaskExecutor>` 注入即可区分，与 C-T1 的 `IEnumerable<IWfConnector>` 模式一致，无覆盖问题。
- **无需 CP6Context**：brief 给了两条演示路径（纯回写 inst.VarsJson 或调只读跨模块查询）。选纯计算回写，最大化幂等与「不落库」示范力度，也不引 DB 依赖到测试脚手架（无需 SqliteCP6Context）。
- **CRLF 提示**：commit 时 git 提示 LF→CRLF（Windows autocrlf），与既有 WebApiExecutor.cs 同库行为一致，非问题。
- 未触碰引擎 `ServiceTaskNodeHandler.cs` 及任何执行态代码；未碰 Space/OA。
