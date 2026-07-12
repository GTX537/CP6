# M-ERP T5 执行报告：IAuditable 贴点(BusinessPartner/Product/价表/Order)

## 结论
钱与主数据关键 ERP 实体接入字段级审计（IAuditable 纯标记接口单行追加）。10 实体新贴 + BusinessPartner 早已贴（T1 f09af0f），共 11 实体覆盖。零 schema 改动、零迁移、零业务逻辑改动。新增 ErpAuditTests 7 用例（6 正测 + 1 负测），全绿；全量 1706 通过（1699 基线 + 7 新增），0 失败。

## 逐实体裁决表（CP6.Entity/DomainModels/Erp/ 全 21 类实查）

### 纳入（贴 IAuditable）
| 实体 | 表 | 类别 | 理由 |
|---|---|---|---|
| BusinessPartner | T_WebBusinessPartner | 取引先主档 | 系统最复杂主数据；**T1(f09af0f) 已贴**，本任务无改动，纳入计数确认 |
| ProductMaster | T_ProductMaster | 製品主档 | 核心主数据，受注快照源头 |
| Order | T_Order | 受注头 | 交易钱路头（CurrencyCd/FxRate 冻结） |
| OrderDetail | T_OrderDetail | 受注明细 | ~125 项含单价/金额快照——行级钱所在（brief 明列「头/明细」） |
| FxRate | T_FxRate | 為替定价主数据 | 改它直接影响算价/换算额 |
| SheetUnitPrice | T_SheetUnitPrice | シート単価表 | 定价主数据（价表核心） |
| SheetUnitPriceEstimate | T_SheetUnitPriceEstimate | 見積用単価表 | 同结构价表，同「改它影响算价」原则 |
| ProductLotPrice | T_ProductLotPrice | ロット別単価表 | 製品定价主数据 |
| EstimateCalc | T_EstimateCalc | 見積計算書头 | 报价计算钱路头（brief 邀「Estimate 头」判断→纳入） |
| Quotation | T_Quotation | 御見積書头 | 报价书钱路头（brief 邀「Quotation 头」判断→纳入） |
| CreditNote | T_CreditNote | 信用票据 | 返金/交換 Amount 钱路 |

### 豁免（不贴 IAuditable）
| 实体 | 类别 | 豁免理由 | 留痕方式 |
|---|---|---|---|
| FscChecklist | 追加型 FSC 発行履歴 | 発行ごと 1 行追加不可变，字段审计无意义（照 WMS StockTransaction 先例） | **源码注释坐实 + 负测试坐实零审计行** |
| OrderProcess | 受注加工工程子明细 | 头 Order/OrderDetail 承载钱，工程行高基数「头为主」 | 报告 + commit msg |
| OrderMaterial | 受注加工材料子明细 | 同上，材料行高基数「头为主」 | 报告 + commit msg |
| OrderProcessNote | 受注工程備考 | 备考文本子行，「头为主」 | 报告 + commit msg |
| EstimateCalcProcess | 見積计算子工程明细 | 头 EstimateCalc 为主 | 报告 + commit msg |
| QuotationCalc | 御見積-計算書中间关联表 | 头 Quotation 为主 | 报告 + commit msg |
| QuotationDetail | 御見積印字用明细 | 头 Quotation 为主 | 报告 + commit msg |
| ProductProcess | 製品加工工程子明细 | 头 ProductMaster 为主 | 报告 + commit msg |
| ProductMaterial | 製品加工材料子明细 | 头 ProductMaster 为主 | 报告 + commit msg |
| ProductCoProduct | 製品連産品子明细 | 头 ProductMaster 为主 | 报告 + commit msg |
| PlateMold | 木型・版型管理マスタ | 主档但**非定价**、改定走 Rev 追加型历史（新增记录非原地改），超出本任务「钱与定价主数据」圈定范围；可后续波按主档审计需求单独评估 | 报告（borderline，记票） |

**豁免注释策略**：brief req 3 将源码注释限定于「高频写入/追加型日志类」豁免（照 WMS Stock/StockTransaction 先例）。ERP 中唯一该类=FscChecklist（追加型履历），已加源码 `[审计豁免]` 注释 + 负测试。其余为「头为主」明细豁免（不同类别，WMS 先例亦仅 commit-msg 留痕，未逐文件注释），坐实于本表 + commit message，避免 11 文件注释churn（遵 Code Organization「零其他改动」）。

## TDD Evidence
- **RED**（贴点前跑 ErpAuditTests）：`Failed: 6, Passed: 1`——6 正测 `Assert.Single() Failure: collection was empty`（无审计行），负测试 FscChecklist 已绿。
- **GREEN**（10 实体贴 IAuditable 后）：`Passed: 7, Failed: 0`。
- **全量回归**：`Passed: 1706, Failed: 0, Skipped: 5`（基线 1699 + 7 新增，无退化）。

## 测试内容（真实断言 Sys_FieldAuditLog 行内容）
- Create_Order → op1 行，断言 EntityName=`Order` + EntityKey=Id
- Update_Order_fxRate → op2 diff，断言 Field=`FxRate` Old=`1` New=`150`
- Update_OrderDetail_amount → op2 diff，断言 Field=`Amount` New=`1200`（明细钱）
- Create_ProductMaster → op1 行，EntityName + EntityKey
- Update_FxRate_rate → op2 diff，Field=`Rate` Old=`150` New=`155`（定价主数据）
- Create_SheetUnitPrice → op1 行（价表 master）
- **负测试** Append_FscChecklist → `Assert.Empty` 零审计行（豁免坐实）

## Files Changed
- 实体单行追加接口（10 文件）：CreditNote / EstimateCalc / FxRate / Order / OrderDetail / ProductLotPrice / ProductMaster / Quotation / SheetUnitPrice(含 SheetUnitPriceEstimate 同文件)
- FscChecklist.cs：新增 2 行 `[审计豁免]` 注释（零 schema/逻辑改动）
- 新增测试：CP6.Tests/Erp/ErpAuditTests.cs（7 用例）

## Self-Review
- 逐实体裁决表完整（21 类全覆盖，纳入 11/豁免 11，BusinessPartner 计入纳入）✓
- 豁免理由坐实：追加型 FscChecklist 源码注释 + 负测试；头为主明细报告/commit 留痕 ✓
- 测试真实断言实体名/字段/新旧值，含负测试 ✓
- 零业务逻辑改动；`git status` 无 Migrations 新文件 ✓
- IAuditable 空标记接口不映射列 → EF 模型无漂移；全量 1706 绿含既有 EF 模型/迁移相关测试无退化 ✓

## Concerns
- **PlateMold（borderline 记票）**：木型・版型管理マスタ为主数据但非定价，本任务按「钱与定价主数据」圈定豁免。若后续以「全主档审计」为口径，应单独评估纳入（改定走 Rev 追加历史，字段审计价值主要在「訂正」原地改场景）。
- OrderDetail 纳入是相对 WMS T6「单据明细头为主」豁免的**有据departure**：brief 明列「Order 头/明细」，且 OrderDetail 承载行级单价/金额快照（钱所在）；与 WMS 纳入 StockTakeDetail（主明细）一致。生产写审计行开销：受注为低频交易文档，非高频台账，开销可接受。
