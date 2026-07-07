# Task 3 Report: Space 服务 BizException 化

## Status
DONE — commit `446b7a2` on `feat/space-wave4-crosscutting`.

## Implemented
1. **8 服务 61 处 throw** `InvalidOperationException("E-SPACE-xxx[: 中文]")` → `BizException("E-SPACE-xxx")`，内联中文删除（词条已入种子）。每文件补 `using CP6.WebApi.Localization;`。E-SPACE-009 两处并发转译走二参构造 `BizException("E-SPACE-009", 409)`（SceneService:328 + SpaceMasterService:401）。
2. **CodeEngineService.PrecheckAsync 内部 catch 联动改型**：PickRule 抛型由 InvalidOperationException 变 BizException 后，PrecheckAsync 的 `catch (InvalidOperationException ex){ Add(ex.Message) }` → `catch (BizException ex){ Add(ex.Code) }`（否则 E-SPACE-301/302 无规则/多规则场景异常穿透，5 测试红）。
3. **6 控制器删 36 处 catch(InvalidOperationException)**：单行 try/catch 塌为裸语句（29 处）；LocationPublish 3 处删 InvalidOperationException catch、**保留** 3 处 DbUpdateConcurrency 409；SceneController.BindCodes 多行 try 手工塌壳。
4. **7 测试文件**：30 处 `ThrowsAsync<InvalidOperationException>` → `ThrowsAsync<BizException>`；29 处异常码断言（15 StartsWith + 14 Equal-with-Message）`ex.Message` → `Assert.Equal("E-SPACE-xxx", ex.Code)`；补 `using CP6.WebApi.Localization;`。
5. **按计划不动**：CodeEngineService:168/366 两处 `throw new InvalidOperationException(errs[0]/preErrs[0])`（CodePrecheck 返回的 E-SPACE-303/305/306 动态串）；WmsBin/非 Space 服务；前端。

## 替换计数对账（实测 vs 探查预估）
| 项 | 探查预估 | 实测 | 说明 |
|---|---|---|---|
| 服务 throw | 56 | **61** | 探查只 grep 单行；漏 5 处跨行/内插：SpaceMaster:325($"E-SPACE-402"内插)+492, Scene:126+238, LocationPublish:309 |
| E-SPACE-009 → 409 | 2 | 2 | ✓ |
| 控制器 catch 删 | 36 | 36 | ✓（33 单行塌壳 + 3 LocationPublish InvalidOperationException catch；保留 3 DbUpdateConcurrency）|
| 测试 ThrowsAsync | 32 | **30** | 探查略高；实测 SpaceMaster13/Scene6/LocPub5/CodeEngine1/Template1/BindCodes2/Connector2 |
| 测试 StartsWith→Equal(Code) | 15 | 15 | ✓ |
| 测试 Equal(...Message)→Code | (其余同步) | 14 | 额外把已有 Equal-with-Message 也统一到 ex.Code |
| 内部 catch 改型 | 未列 | 1 | CodeEngine PrecheckAsync（必要联动）|

## 收尾 grep 证据
- `grep -rn 'InvalidOperationException("E-SPACE\|InvalidOperationException("W-SPACE' CP6.Core/` → **ZERO**（含跨行/内插复核）
- `new BizException(` in Space services → **61**；`BizException("E-SPACE-009", 409)` → **2**
- 残留 `InvalidOperationException` in Space services → **2**（CodeEngine:168/366，precheck 动态串，按计划保留）
- 全量 `dotnet test CP6.slnx` → **Passed 1559 / Skipped 5 / Failed 0**（数量吻合目标）
- `dotnet build CP6.slnx` → **0 error**
- 前端 `npm run test`（cp6.web）→ **364 passed / 57 files**

## Files changed (21)
服务(8): CodeEngineService, ConnectorService, LocationGeometryService, LocationPublishService, SceneIoService, SceneService, SpaceMasterService, TemplateService
控制器(6): CodeRuleController, ConnectorController, LocationPublishController, SceneController, SpaceMasterController, TemplateController
测试(7): SpaceMasterServiceTests, SceneServiceTests, LocationPublishServiceTests, CodeEngineServiceTests, TemplateServiceTests, BindCodesTests, Space/ConnectorServiceTests

## Self-review / Concerns
- **⚠ CodeEngine:168/366 precheck-throw 路径行为变化**：这两处仍抛 InvalidOperationException（内容为 E-SPACE-303/305/306 裸码）。控制器 catch 已删除后，GenerateAsync/GenSingleAsync 命中不合法规则时，异常不再被 middleware（只认 BizException）接住 → 由 500 回落而非旧的 400。按 brief item 4 + 记忆观察 2297「Correctly Scoped for Non-Conversion」明确保留，未改。若后续要修，可将这两处改为 `throw new BizException(errs[0])`（errs[0] 已是裸码 "E-SPACE-303"，middleware 可译，恢复 400 语义）。**留待用户/后续任务裁决。**
- 探查基数 56/32 偏差已在对账列明；真实口径 61 throw / 30 ThrowsAsync。
- CRLF：perl -pi 写入 LF，git 按 repo 规范提交时归一化为 CRLF（diff stat 干净、无行尾 churn，已核 ConnectorService/LocationPublish diff 确认仅目标改动）。

## Fix Round 1（仲裁修复，commit `7a4e093`）
仲裁结论：CodeEngine:168/366 判 Important 须补转——豁免本义是 Validate 的**返回值列表**（进 PrecheckErrors 由前端展示），throw 站点不在豁免内；同方法 E-304 已转而 E-303 族落 500 的不一致坐实为遗漏。

修复内容：
- `CodeEngineService.cs:168`（GenerateAsync 预检失败）`throw new InvalidOperationException(errs[0])` → `throw new BizException(errs[0])`
- `CodeEngineService.cs:366`（GenSingleAsync 同路径）`throw new InvalidOperationException(preErrs[0])` → `throw new BizException(preErrs[0])`
- errs[0]/preErrs[0] 为裸码串（E-SPACE-303/305/306），三码已在词条种子内，middleware 可译 → 恢复 400 语义（Round 0 删 catch 后此路径曾回退为 500）
- 测试核查：`grep InvalidOperationException CP6.Tests/CodeEngineServiceTests.cs` 零命中，全库无断言 GenerateAsync/GenSingleAsync 预检失败异常型的测试 → 无测试改动

验证：`dotnet build CP6.slnx` 0 err；全量 `dotnet test` **1559 passed / 5 skipped / 0 failed**。
收尾 grep：`InvalidOperationException` in `CP6.Core/Services/Space/` → **ZERO**（Space 服务层全数 BizException 化，throw 总口径 61+2=63）。
