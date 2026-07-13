# 波5 Task 5 报告：SpaceLocateController 裸 BadRequest → BizException 化

**Status: DONE** — commit `02e30a7` on `feat/space-wave5`（已 push）。

## 改动
- `CP6.WebApi/Controllers/Space/SpaceLocateController.cs`
  - 新增 `using CP6.WebApi.Localization;`
  - `Locate` 未命中：`return BadRequest(new { code=400, message="E-SPACE-601" })` → `throw new BizException("E-SPACE-601")`
  - `Detail` 未命中：`return BadRequest(new { code=400, message="E-SPACE-004" })` → `throw new BizException("E-SPACE-004")`
- 两处现走 BizExceptionMiddleware 按 culture 翻译；词条已在 I18nSpaceScreenSeed，零新增词条。

## TDD
- 新建 `CP6.Tests/Space/SpaceLocateControllerTests.cs`（3 用例，直构 controller 绕过 [Authorize]）：
  - `Locate_NotFound_ThrowsBizException_601` — 断言抛 BizException 且 Code=="E-SPACE-601"
  - `Detail_NotFound_ThrowsBizException_004` — 断言抛 BizException 且 Code=="E-SPACE-004"
  - `Locate_Found_ReturnsOkEnvelope` — happy path 仍返 OkObjectResult
- 先红（2 fail：No exception was thrown）→ 实现 → 绿。
- 既有 `SpaceLocateServiceTests` 为 service 层（断言 service 返 null），无裸 400 信封断言，无需改。

## 测试
全量 `dotnet test`：**1819 passed / 5 skipped / 0 failed**（基线 1816 + 本次新增 3 用例；未减数）。

## 疑虑
- 无。BizException 默认 HttpStatus=400，与原裸 400 状态码一致，仅信封由中间件统一为译文，行为对齐 M-* 波既有约定。

## Self-review
- 两处 return BadRequest 已确认全消（grep controller 无 BadRequest 残留）。
- BizException namespace = CP6.WebApi.Localization（类物理在 CP6.Core），using 正确。
- 仅 3 文件变更（brief + 新测试 + 控制器），未扫入无关文件。
