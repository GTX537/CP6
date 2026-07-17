# X-SWEEP T1 报告：HttpPatch 反射谓词 sweep（八文件齐补）

日期：2026-07-17 ｜ 分支：feat/xwave-authz-test-sweep ｜ 实现者：T1 subagent（Fable 5）
票源：M-PUR 完成记录票#2（plans/2026-07-07-module-waves-crosscutting.md 文末）+ M-PLAN/PUB T3 NoPatchEndpoints_InScope 注释钉票。

## 0. 前置实证：全仓零 PATCH 端点

`grep HttpPatch CP6.WebApi/Controllers/**` → **No files found**（改动前实测）。
即：谓词补齐属纯防未来（fail-open 预防），对现存扫描面零语义影响——全量测试应持平绿（§3 已证）。

## 1. 八文件逐个改动点

八个文件的 mutating 谓词**同为一个形态**（`GetCustomAttributes<HttpXxxAttribute>().Any()` OR 链，六波先例
一脉相承，无 attribute 类型集合/helper 变体）。故八处改动一致：在 `HttpPutAttribute` 与 `HttpDeleteAttribute`
两行之间插入一行：

```csharp
|| m.GetCustomAttributes<HttpPatchAttribute>().Any()   // X-SWEEP T1：补 PATCH，杜绝未来 [HttpPatch] 写端点静默逃出扫描面
```

`HttpPatchAttribute` 属 `Microsoft.AspNetCore.Mvc`，八文件均已 using，零新增引用。

| # | 文件 | 谓词改动 | 注释/自检改动 |
|---|------|---------|--------------|
| 1 | CP6.Tests/Wms/WmsPermissionAttributeTests.cs | IsMutating +1 行（L73-77） | 无（原文件无 PATCH 相关注释） |
| 2 | CP6.Tests/ErpPermissionAttributeTests.cs | IsMutating +1 行（L85-89） | 无 |
| 3 | CP6.Tests/MesPermissionAttributeTests.cs | IsMutating +1 行（L75-79） | 无 |
| 4 | CP6.Tests/OawfPermissionAttributeTests.cs | IsMutating +1 行（L109-113） | 无 |
| 5 | CP6.Tests/PurPermissionAttributeTests.cs | IsMutating +1 行（L90-94） | 无 |
| 6 | CP6.Tests/PlanPubPermissionAttributeTests.cs | IsMutating +1 行 | ①谓词头注释改写：「HttpPatch 未含…已立跨波票」→「含 HttpPatch——票已于本 sweep 落地」；②NoPatchEndpoints_InScope 注释语义从『钉票』改『现状 pin』（断言体不动，仍 pin 扫描面 0 PATCH） |
| 7 | CP6.Tests/Fin/FinPeriodPermissionAttributeTests.cs | IsMutating +1 行（L31-35） | 无 |
| 8 | CP6.Tests/Space/SpacePermissionAttributeTests.cs | IsMutating +1 行（L51-55） | 无 |

同型自检/注释排查：仅 PlanPub 有 PATCH 相关注释与自检测试（简报预判一致）；其余七文件 grep 零 PATCH 提及，
无需对齐。PlanPub 的 `HttpPut_Endpoint_IsScannedAndGuarded`（PUT 显式钉死）不受影响，保留原样。

## 2. RED 实证（谓词有牙证明）

抽 2 个不同风格文件实弹：**Erp**（豁免清单+精确计数收口风格）与 **Space**（逐字白名单风格）。
临时在 `Erp/OrderController.cs` 与 `Space/SpaceMasterController.cs` 各加一个裸探针（不 commit）：

```csharp
[HttpPatch("xsweep-red-proof")]
public IActionResult XsweepRedProof() => Ok();   // 无 [RequirePermission]
```

`dotnet test --filter "FullyQualifiedName~PermissionAttributeTests"` 实录（节选）：

```
[xUnit.net]     CP6.Tests.Space.SpacePermissionAttributeTests.EveryMutatingAction_HasRequirePermission_InWhitelist [FAIL]
[xUnit.net]     CP6.Tests.ErpPermissionAttributeTests.EveryMutatingAction_IsGuarded_WithConventionalKeyOrExemption [FAIL]
  Failed ... Error Message:
   变更端点权限点缺失/越界:
SpaceMasterController.XsweepRedProof：变更端点缺 [RequirePermission]
  Failed ... Error Message:
   变更端点权限点缺失/键不合约定/豁免冲突:
OrderController.XsweepRedProof：变更端点缺 [RequirePermission] 且不在只读 POST 豁免清单
Failed!  - Failed:     2, Passed:    30, Skipped:     0, Total:    32, Duration: 411 ms
```

两种风格均精确报出漏贴 offender（且 32 用例中仅这 2 个红——探针未误伤兄弟模块）。改动前同样的裸 PATCH
会静默逃出扫描面（fail-open），现在 fail-closed 报红——谓词真有牙。
其余六文件谓词 diff 与这两文件逐字一致（§1），以一致性论证覆盖。

## 3. 还原实证 + 全量绿

探针还原后 `git status --porcelain`（commit 前实录）：

```
 M CP6.Tests/ErpPermissionAttributeTests.cs
 M CP6.Tests/Fin/FinPeriodPermissionAttributeTests.cs
 M CP6.Tests/MesPermissionAttributeTests.cs
 M CP6.Tests/OawfPermissionAttributeTests.cs
 M CP6.Tests/PlanPubPermissionAttributeTests.cs
 M CP6.Tests/PurPermissionAttributeTests.cs
 M CP6.Tests/Space/SpacePermissionAttributeTests.cs
 M CP6.Tests/Wms/WmsPermissionAttributeTests.cs
```

零生产代码改动（两控制器已还原干净，`CP6.WebApi/` 无任何 M 行）。

全量（前台串行）：**Passed! - Failed: 0, Passed: 2190, Skipped: 5, Total: 2195, Duration: 2 m 6 s**
——与基线 2190 绿/5 skip 逐字持平（谓词扩 PATCH 对现存零 PATCH 扫描面零语义影响，符合预期）。

## 4. 自审

- 最小侵入：每文件仅谓词 +1 行（PlanPub 另加两处注释语义更新，断言体零改动）；未统一重构、未动
  helper/结构/断言/计数，八个已过终审文件的形态原样保留。
- 未动范围确认：真相源文档（docs/seeds/*-permission-keys.md）、种子（*PermissionSeed.cs）、控制器
  （探针已还原）均零改动。
- 谓词插入位置统一在 Put 与 Delete 之间（HTTP 动词语义序 POST/PUT/PATCH/DELETE），八文件一致便于日后 diff。
- PlanPub `NoPatchEndpoints_InScope` 保留为现状 pin：未来 Plan/Pub 引入 PATCH 时该断言红，提示更新快照
  （谓词已就位，核心闸会接管漏贴检查）。
- 中断恢复说明：会话曾因 API 529 中断，恢复后经主控核实现场一致，从 RED 确认步继续，全流程无重做遗漏。

## 5. Concerns

1. **非阻塞**：八文件中仅 PlanPub 有「扫描面 0 PATCH」现状 pin 自检；其余七模块若未来引入 PATCH 端点，
   无对应现状 pin（但核心闸已含 PATCH，漏贴必红——pin 缺失只影响「提示更新快照」的文档性，不影响 fail-closed）。
2. **非阻塞**：RED 实弹仅覆盖 Erp/Space 两风格（简报明示抽 2 即可）；其余六文件以谓词 diff 逐字一致论证。
   Fin 文件扫描面仅 PeriodController 单类（G.1 范围约束），谓词补齐后该约束不变。
