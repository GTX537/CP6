# Task T5 Report — FlowSchemaValidator 补 ServiceMode 值域校验（sync|async）

**Status: DONE**  ·  commit `614ef4e`（已 push）  ·  分支 `feat/wfs-cleanup-tickets`

## 缺陷核实（证据）

- `FlowSchemaValidator.cs` serviceTask 分支（改前 :85-97）的 `bool bad` 只校验 `ServiceKind`、各 kind 的必填字段、票4 下标扫描、P2-3 成功出边——**从不引用 `n.ServiceMode`**。
- 全仓 `grep ServiceMode` 命中仅 4 处：`WfStatus.cs`（常量定义 Sync="sync"/Async="async"）、`ServiceTaskNodeHandler.cs:40-41,51`（运行期解析）、`FlowSchema.cs:72`（`string? ServiceMode` 属性，注释 `"sync" | "async"`）。**校验层零命中** → 缺陷坐实。
- 运行期语义（`ServiceTaskNodeHandler.cs:41,51`）：`mode = node.ServiceMode ?? (webApi→async / 其余→sync)`，随后 `if (mode == ServiceMode.Sync)` **序数精确匹配**——任何非 `"sync"` 值（含 `"batch"`、`"Sync"`）都静默落入 async 分支，行为不可预期。故用户手填 `"batch"` 能保存且运行期无声降级。这正是 brief 所述风险。
- 值比较口径：运行期用 `==`（Ordinal），故校验层同用 `StringComparer.Ordinal` 精确对齐（大写 `"Sync"` 运行期也不认，校验层同样应拒）。与 brief 一致。

## TDD 红绿

- **RED**：`dotnet test --filter ServiceTaskValidatorTests` →
  `ServiceMode_Invalid_E_WF_016 [FAIL] Assert.Contains() Item not found. Collection: []`（Failed 1, Passed 14）。合法/null 用例已绿（证明未误伤既有行为）。
- **GREEN**（实现后同命令）：`Passed! Failed 0, Passed 15`。

## 改动文件（仅 2，零跨模块污染）

- `CP6.Core/Services/Wf/FlowSchemaValidator.cs`
  - 新增 `KnownServiceModes = { Sync, Async }`（`StringComparer.Ordinal`）。
  - serviceTask `bool bad` 追加一项（置于 kind 检查后、票4 下标扫描前）：
    `|| (!string.IsNullOrWhiteSpace(n.ServiceMode) && !KnownServiceModes.Contains(n.ServiceMode.Trim()))`
    —— 仅在**非空**时校验值域（null=不填，按 kind 默认，合法）。
- `CP6.Tests/Wf/ServiceTaskValidatorTests.cs`
  - `ServiceMode_Invalid_E_WF_016`（mode="batch" → E-WF-016）。
  - `ServiceMode_SyncOrAsync_Or_Null_Passes`（null/"sync"/"async" 三值均不触发 E-WF-016）。

`git show --stat`：2 files, +41。仅 Wf 域 + 对应测试，未触碰 Space/迁移/DbSet。

## 测试结果

- `--filter ServiceTaskValidatorTests`：15 passed。
- `--filter Wf`：**195 passed, 0 failed**（Wf 门绿）。
- 全量 `CP6.Tests.csproj`：**1833 passed / 5 skipped / 0 failed**（基线 1831 + 本波 2 新测 = 1833，SQLite 既知 5 skip 不变）。
- 零迁移（无实体/DbSet 改动）。

## 自审

- **完整性**：值域覆盖 sync|async；null（不填）放行，符合「按 kind 默认」语义；Trim() 容忍前后空白，与 T4 分支既有 `kind.Trim()` 风格一致。
- **YAGNI**：单行谓词 + 一常量集，无多余抽象；复用既有 E-WF-016 错误码（brief 指定），未新增错误码。
- **测试真验证行为**：非法值断言 `Contains(E-WF-016)`（不是仅断言非空——因该 schema 其余部分完全合法，唯一致错项即 mode，故 E-WF-016 的出现精确归因于本改动）；合法用例断言 `DoesNotContain`，防误伤。
- **与 T4 共存**：改动插在 T4 的 `ContainsUnsupportedSubscript` 两行之前，`||` 短路链，零回退 T4；`--filter Wf` 195 全绿含 T4 用例。

## 疑虑

- 无阻断性疑虑。一点观察（非本票范畴，仅记档）：校验层用 Ordinal 精确对齐运行期 `==`，故大写 `"Sync"` 会被判非法。这是**正确**取舍——因运行期同样不认大写 `"Sync"`（会静默降级 async），校验层拒之即防患。此与 KnownServiceKinds 的 Ordinal 口径一致；连接器名那条既知 validator(Ordinal)/runtime(OrdinalIgnoreCase) 不一致票与本票无关（mode 运行期本就是 Ordinal `==`，无镜像偏差）。
