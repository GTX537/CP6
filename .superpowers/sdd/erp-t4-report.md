# M-ERP T4 执行报告：fail-closed 反射测试

## 一、实现内容

新增单一测试文件 `CP6.Tests/ErpPermissionAttributeTests.cs`（namespace `CP6.Tests`，与兄弟 `ErpMenuSeedTests`/`ErpPermissionSeedTests` 同置根目录）。**零生产代码改动**。照 WMS 先例（commit 0efb717 `WmsPermissionAttributeTests`）三件套，ERP 化并按真相源扩两处。

5 个 `[Fact]`：

1. **`ErpControllers_AreDiscovered`（discovery 守卫）**：断言 `CP6.WebApi.Controllers.Erp` 下继承 `ControllerBase` 的非抽象类 == **15**（含 MasterData/OrderTrace 两个 GET-only）。防命名空间/程序集移动后「空扫假绿」。实测程序集反射得 15，与真相源 §七扫描面吻合。
2. **`EveryMutatingAction_IsGuarded_WithConventionalKeyOrExemption`（fail-closed 核心闸）**：遍历全部变更端点（HttpPost/HttpPut/HttpDelete），每个要么贴 `[RequirePermission]`、要么在 11 条只读 POST 豁免清单内，二者皆非即 offender 红。并收口：贴点数精确 `== 35`、豁免命中数精确 `== 11`（46 = 35 + 11）。键约定：menu 匹配 `^erp-[a-z0-9-]+$`（连字符禁下划线），action 逐词相等落在真相源 action 集。既贴键又列豁免亦报冲突。
3. **`ReadOnlyPostExemptions_AreAllStillUntaggedMutatingEndpoints`（豁免防腐）**：清单每条须实存、确为变更端点、且当前未贴权限键。防豁免清单陈旧（端点改名/被贴/被删）却仍白名单遮蔽某真·写端点丢键。并断言清单条数 `== 11`。
4. **`Calculate_RetainsAllowAnonymous`（AllowAnonymous 锁）**：断言 `EstimateCalcController.Calculate` 确实挂 `[AllowAnonymous]`。见「AllowAnonymous 断言的锁死路径」。
5. **`NoReadOnlyGetAction_HasRequirePermission`（只读误贴防护，WMS 先例第四件）**：纯 GET 端点不应贴 `[RequirePermission]`（本波未给 GET 贴）。

### action 词汇集（逐词相等，非宽松包含）
从实际贴的 35 个属性 grep 出真实集：`add, edit, del, cancel, correct, confirm, issue, import, close, split`（10 词）。**刻意不含 `view`**——只读 POST 归 view 但不贴键（未打属性），故 view 不在 tagged action 集内。多一词/少一词即红。

## 二、豁免清单对账表（11 条，与真相源 §四逐条对齐）

| # | 豁免条目（Controller.Method） | 真相源 §四 | HTTP | 依据 |
|---|---|---|---|---|
| 1 | OrderController.CalcLeadTime | #1 | POST /api/orders/lead-time | 纯营业日逆算，无 _db 触碰 |
| 2 | OrderController.CalcProductCategory | #2 | POST calc-product-category | ProductMasters.AsNoTracking 读 |
| 3 | OrderController.CalcMaterials | #3 | POST calc-materials | BOM 展开纯读投影 |
| 4 | OrderController.ExportReport | #4 | POST report | 受注伝票导出，读拼 bytes |
| 5 | EstimateCalcController.Calculate | #5 | POST calculate | 计算引擎仅写内存 DTO；**另挂 [AllowAnonymous]** |
| 6 | PlateMoldController.Label | #6 | POST label | ラベル CSV，AsNoTracking 读 |
| 7 | CreditNoteController.Search | #7 | POST search | CreditNoteService 全类无写 |
| 8 | OtdReportController.Summary | #8 | POST summary | OtdReportService 全类无写 |
| 9 | OtdReportController.ExportCsv | #9 | POST export-csv | 同上，纯读导出 |
| 10 | UnshippedOrderController.Search | #10 | POST search | UnshippedOrderService 全类无写 |
| 11 | UnshippedOrderController.ExportCsv | #11 | POST export-csv | 同上，纯读导出 |

收口：15 控制器 / 46 变更端点 = **35 贴点 + 11 豁免**，与真相源 §七逐数吻合。RequirePermission grep 实测 35（BackorderController 2 + BusinessPartner 3 + EstimateCalc 4 + FscChecklist 1 + FxRate 3 + Order 5 + PlateMold 4 + Product 4 + Quotation 7 + SheetUnitPrice 2 = 35）。

### AllowAnonymous 断言的锁死路径
Calculate 在豁免清单内，核心闸不会拦其「缺键」。若有人删掉 `[AllowAnonymous]` 却不贴 `[RequirePermission]`，核心闸仍放行（因它在豁免集）——这是漏洞。`Calculate_RetainsAllowAnonymous` 独立断言其挂着 AllowAnonymous：一旦被删即红，逼开发者重新决策（复原匿名 或 移出豁免 + 贴 RequirePermission）。这条路被真正锁住。

## 三、反向验证证据

**删哪个贴点**：临时删 `OrderController.Cancel`（高危键 `erp-order:cancel`）的 `[RequirePermission("erp-order", "cancel")]`。

**红的输出**（`dotnet test --filter ErpPermissionAttributeTests`）：
```
变更端点权限点缺失/键不合约定/豁免冲突:
OrderController.Cancel：变更端点缺 [RequirePermission] 且不在只读 POST 豁免清单
Failed!  - Failed:     1, Passed:     4, Skipped:     0, Total:     5
```
核心闸（Test 2）精确捕获漏贴端点，其余 4 绿。

**恢复后绿**：改回属性 → `git status --short` 仅剩 `?? CP6.Tests/ErpPermissionAttributeTests.cs`（生产代码工作树干净，OrderController 无残留 diff）→ 重跑 `Passed! Failed: 0, Passed: 5`。

## 四、Files changed
- 新增：`CP6.Tests/ErpPermissionAttributeTests.cs`（唯一改动，测试文件）
- 生产代码：**零改动**（反向验证的临时删除已恢复，工作树验证干净）

## 五、Self-review 结论
- discovery 守卫 15 = 程序集实扫 15，吻合（防空扫）。✅
- fail-closed 闸 35=35 精确（taggedCount 断言）；豁免 11 逐条带真相源编号 + 依据注释，与 §四对齐。✅
- AllowAnonymous 断言独立锁死「删 AllowAnonymous 忘贴权限」路径（Calculate 在豁免集，唯此断言拦得住）。✅
- 键正则 `^erp-[a-z0-9-]+$` + action 集**逐词相等**（HashSet.Contains，非子串包含），刻意排除未贴的 view。✅
- 反向验证做了（删 Cancel→红→恢复→绿），工作树已恢复干净（仅新测试文件未跟踪）。✅
- 全量：**1699 passed / 0 failed / 5 skipped**（基线 1694 + 新 5，无回归）。✅

## 六、Concerns
- **豁免清单为文档判定的静态镜像**：11 条豁免依据真相源 §四「读 Service 实现证得无写」。若某只读 POST 的 Service 未来被改成有写副作用，本测试不会自动发现（它只校验「未贴键 = 豁免」的结构，不重跑 Service 无写证明）。缓解：Test 3 至少保证豁免条目仍是「未贴键的变更端点」，一旦被贴键即报冲突强制复核。语义层无写证明仍靠人工/Service 单测守。
- **Calculate 的 [AllowAnonymous] 是已记终审票的遗留裁决**（真相源 §六#3）。本测试把现状锁成「必须挂 AllowAnonymous」——这是 fail-closed 意义上锁死回潮，但也意味着「撤销匿名开放」这一未来正当变更会触发红灯，届时须同步移出豁免 + 贴键 + 更新本断言。此为预期行为，非缺陷。
