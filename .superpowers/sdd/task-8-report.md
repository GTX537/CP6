# Task 8 报告：场景保存状态机护栏（H1）

**Status:** DONE

## Implemented
`SceneService.SaveSceneAsync` 的 Locations 差量块加护栏：
- 已存在库位：移除 `existing.Status = ld.Status` / `existing.CodeOrigin = ld.CodeOrigin` 两行——不再接受场景保存 DTO 覆盖。
- 新建库位：`Status`/`CodeOrigin` 硬编码为 `0` / `1`（编辑器新建恒草稿）。
- 状态流转唯一通道保留：publish / deactivate / adopt / bind-codes。

## TDD Evidence
- **RED**：新增 2 测试 `SaveScene_CannotFlipPublishedStatus_OrCodeOrigin`、`SaveScene_NewLocation_ForcedDraft` → 2 FAIL（DTO 值直通落库：CannotFlip 得 0 应 1；ForcedDraft 得 1 应 0）。
- **GREEN（护栏后）**：
  - Scene 全套 `SceneServiceTests|SceneIoServiceTests|BindCodesTests` → 9 passed / 0 failed。
  - 全量回归 → **1528 passed / 5 skipped**（基线 1526 + 本任务 2 新测试，零回归）。
- 构建 `dotnet build CP6.slnx` → 0 Error（1 既有 warning，与本改动无关）。

## SceneIoService 核实结论
**不受影响。** 导入路径（`SceneIoService.cs:162-196`）通过库位全枚举**直接构造** `Space_Location` 实体并 `_db.Space_Locations.AddRange(...)`，Status/CodeOrigin 在实体上直接写 `0`/`1`，**不经 SaveSceneAsync**，故护栏不会降级导入。无需任何改动（brief 头部担心的"导入依赖 SaveSceneAsync 保留 Status"不成立）。

## BindCodes 核实结论
`SceneService.BindCodesAsync` 走独立端点，只改 RackId/Placed，不经 Locations 差量块——不受影响，测试确认 BindCodesTests 全绿。

## 既有断言核实
`SceneServiceTests`/`SceneIoServiceTests`/`BindCodesTests` 中**无**任何"场景保存能写入 Status=1 或 CodeOrigin=2"的既有断言（若有会在护栏后转红，实际全绿）。故无需更新旧断言。

## Files changed
- `CP6.Core/Services/Space/SceneService.cs`（Locations 差量块护栏）
- `CP6.Tests/SceneServiceTests.cs`（+2 测试）

## Self-review / 偏离说明
- **DTO 偏离**：brief Step 1 测试代码用 `SceneLocationSaveDto { RackId = null }`，但 `SceneSaveDto.cs:27` 的 `RackId` 为**非空 `Guid`**（编译错误 CS0037）。按最小偏离原则，仅将两处 DTO 的 `RackId = null` 改为 `RackId = Guid.Empty`——RackId 值与 Status/CodeOrigin 断言无关，不改 DTO 定义（避免扩大改动面）。已在 commit message 记录。
- 实体（预置库位）的 `RackId = null` 无需改（`Space_Location.RackId` 本就是 `Guid?`）。

## Commit
`6af7c8b fix(space): 场景保存状态机护栏——Status/CodeOrigin 拒绝 DTO 覆盖，新建强制草稿（评审 H1）`
