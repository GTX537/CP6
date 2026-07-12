# M-ERP T3a 执行报告：15 控制器贴 [RequirePermission]

## 实施内容

对真相源 `docs/seeds/erp-permission-keys.md` §一 列出的 35 个真写端点逐方法贴 `[RequirePermission("menu-key","action")]`，键值逐字取自真相源。纯注解叠加：每个受影响文件加 `using CP6.Core.Auth;`，每个写方法在其 `[HttpXxx]` 属性之后加一行 `[RequirePermission]`。零方法体改动，类级 `[Authorize]` 全部保留。

11 个只读 POST 豁免**未贴**（按简报要求，与 WMS T3a 给 Summary 贴 view 的做法不同——ERP 简报明确豁免不贴）。

## 逐控制器对账表（35 = 35）

| 控制器 | 真写端点数 | 贴点数 | 端点→键 |
|---|---|---|---|
| OrderController | 5 | 5 | Create→erp-order:add / Update→erp-order:edit / Delete→erp-order:del / BatchUpdatePrice→erp-order-price-correction:correct / Cancel→erp-order:cancel |
| BackorderController | 2 | 2 | CloseRemaining→erp-backorder:close / SplitToNewOrder→erp-backorder:split |
| FxRateController | 3 | 3 | Create→erp-fx-rate:add / Update→erp-fx-rate:edit / Delete→erp-fx-rate:del |
| EstimateCalcController | 4 | 4 | Create→erp-estimate-calc:add / Update→erp-estimate-calc:edit / Delete→erp-estimate-calc:del / Copy→erp-estimate-calc:add |
| QuotationController | 7 | 7 | Create→add / Update→edit / Delete→del / Copy→add / Confirm→confirm / CancelConfirm→confirm / Issue→issue（均 erp-quotation） |
| ProductController | 4 | 4 | Create→add / Update→edit / Delete→del / Copy→add（均 erp-product） |
| BusinessPartnerController | 3 | 3 | Create→erp-business-partner:add / Update→edit / Delete→del（Delete 保留既有 [Authorize(Roles="1,Admin")] 双闸） |
| FscChecklistController | 1 | 1 | Issue→erp-fsc-checklist:issue |
| SheetUnitPriceController | 2 | 2 | Import→erp-sheet-unit-price:import / BatchUpdate→erp-sheet-unit-price:edit |
| PlateMoldController | 4 | 4 | Create→add / Revise→edit / Update→edit / Delete→del（均 erp-plate-mold） |
| **合计** | **35** | **35** | ✅ |

### 未贴（豁免/无写端点）控制器
- CreditNoteController：唯一端点 Search 为只读 POST 豁免 → 0 贴点
- OtdReportController：Summary/ExportCsv 均只读 POST 豁免 → 0 贴点
- UnshippedOrderController：Search/ExportCsv 均只读 POST 豁免 → 0 贴点
- MasterDataController / OrderTraceController：GET-only，无写端点 → 0 贴点

### 11 个只读 POST 豁免（确认未贴）
lead-time / calc-product-category / calc-materials / report（OrderController 4 个），calculate（EstimateCalc），label（PlateMold），credit-note/search，otd summary，otd export-csv，unshipped search，unshipped export-csv。

## 裁决点确认
`EstimateCalcController.Calculate`（:135-141）现挂 `[AllowAnonymous]`，**未被触碰**——本任务不贴不删，保持原状。T4 反射测试将显式豁免。

## 验证与提交
- `dotnet build CP6.WebApi/CP6.WebApi.csproj -c Debug`：**Build succeeded，0 Error**（1 个既有 InboundService.cs 警告，与本任务无关）。
- `dotnet test CP6.Tests`：**Passed! Failed: 0, Passed: 1689, Skipped: 5, Total: 1694**——与基线 1689 绿完全一致，零跌落。
- grep 校验：`RequirePermission("erp` 在 Erp 目录命中 **35** 行。

## 变更文件（10 个控制器）
- CP6.WebApi/Controllers/Erp/OrderController.cs
- CP6.WebApi/Controllers/Erp/BackorderController.cs
- CP6.WebApi/Controllers/Erp/FxRateController.cs
- CP6.WebApi/Controllers/Erp/EstimateCalcController.cs
- CP6.WebApi/Controllers/Erp/QuotationController.cs
- CP6.WebApi/Controllers/Erp/ProductController.cs
- CP6.WebApi/Controllers/Erp/BusinessPartnerController.cs
- CP6.WebApi/Controllers/Erp/FscChecklistController.cs
- CP6.WebApi/Controllers/Erp/SheetUnitPriceController.cs
- CP6.WebApi/Controllers/Erp/PlateMoldController.cs

## 自查结论
- Completeness：35/35 全贴，键值逐字与真相源一致（连字符，无下划线，无降级）。高危键 erp-order:cancel、erp-order-price-correction:correct 独立成键未降级 edit；状态键 confirm/issue/import/close/split 独立成键。11 豁免确未贴。
- Discipline：零方法体改动；GET 端点无贴点；未越界改其他模块；类级 [Authorize] 与 BusinessPartner.Delete 的 [Authorize(Roles)] 双闸均保留；EstimateCalc.Calculate 的 [AllowAnonymous] 未动。
- Concern（非本任务，供 T3b/T4）：5 个 menu-key（erp-order-price-correction 除外，实为菜单209存在……实际是 erp-backorder/credit-note/otd-report/fx-rate/order-trace）对应孤儿路由菜单缺失，T3b 种子与 T4 补菜单前，这些键会 fail-closed 403（真相源 §六已记为 T2/T4 硬前置）。本任务仅贴点，符合预期。
