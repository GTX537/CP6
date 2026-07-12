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
| OrderProcess | T_OrderProcess | 受注加工工程明细 | **含 PurchaseUnitPrice(採購単価)/FixedPrice(固定単価)**——行级定价，改它影响算价（复审纳入） |
| OrderMaterial | T_OrderMaterial | 受注加工材料明细 | **含 SupplyUnitPrice(支給単価)**——行级定价，改它影响算价（复审纳入） |
| QuotationDetail | T_QuotationDetail | 御見積書印字明细 | **含 UnitPrice(単価)/Amount(金額=数量×単価)**——打印到正式对客报价书 PDF 的行级金额（复审纳入，与 OrderDetail 同口径） |
| ProductProcess | T_ProductProcess | 製品加工工程明细 | **含 PurchasePrice(仕入単価)/FixedPrice(指値)**——行级定价，改它影响算价（复审纳入） |
| ProductMaterial | T_ProductMaterial | 製品加工材料明细 | **含 SupplyPrice(受給単価)**——行级定价，改它影响算价（复审纳入） |

### 豁免（不贴 IAuditable）— 全部经字段级实查
| 实体 | 类别 | 豁免理由（字段级实查结论） | 留痕方式 |
|---|---|---|---|
| FscChecklist | 追加型 FSC 発行履歴 | 発行ごと 1 行追加不可变，**实查无货币字段**（FscManagementNo/QtnNo/QtnCalcNo 等标识+履历字段），照 WMS StockTransaction 先例 | **源码注释坐实 + 负测试坐实零审计行** |
| OrderProcessNote | 受注工程備考 | **实查仅 Note1/Note2 文本备考**（+ WebOrderNo/DetailNo/ProductCd/OperationCd 主键），无货币字段 | 报告 + commit msg |
| EstimateCalcProcess | 見積计算子工程明细 | **实查无货币字段**（工程名/作業名/仕様1-7 Label+Val/PlateNo/ProcNote 皆文本），单価/金額落在头 EstimateCalc 与工序汇总，此明细不承载钱 | 报告 + commit msg |
| QuotationCalc | 御見積-計算書 M:N 中间关联表 | **实查无货币字段**（QtnNo/QtnCalcNo 关联键 + EstimateCheckFlg/MasterConfirmFlg 状态标志 + 日期 + FscManagementNo），纯关联/状态表 | 报告 + commit msg |
| ProductCoProduct | 製品連産品明细 | **实查无货币字段**：CoProductName(名称)/QtyRatio(数量産出比率，非钱)/NextProcessCd(次工程CD)，定价落在 ProductProcess/ProductMaterial | 报告 + commit msg |
| PlateMold | 木型・版型管理マスタ | 主档但**实查非定价**、改定走 Rev 追加型历史（新增记录非原地改），超出本任务「钱与定价主数据」圈定范围（审查者复核确认豁免成立） | 报告（borderline，记票） |

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

---

## 复审修复（审查者字段级实查后，verdict=Needs fixes）

### 背景
审查者按字段级实查推翻了本报告首版「头为主」类豁免的裁决：5 个被豁免的明细实体实含真实货币/定价字段，豁免理由「不承载钱」与源码事实矛盾，且与 OrderDetail「行级钱所在→纳入」的标准不一致。本次修复接受该裁决并统一口径为「实体实含货币/定价字段即纳入」，不再按实体类别（头/明细）套用先例。

### 修复动作
1. **新增 5 实体贴 IAuditable**（纯标记单行，零其他改动、零迁移）：
   - `OrderProcess.cs:72-73` — PurchaseUnitPrice(採購単価)/FixedPrice(固定単価)
   - `OrderMaterial.cs:43-44` — SupplyUnitPrice(支給単価)
   - `QuotationDetail.cs:37-38,44-46` — UnitPrice(単価)/Amount(金額=数量×単価，打印到正式对客报价书 PDF)
   - `ProductProcess.cs:75,79` — PurchasePrice(仕入単価)/FixedPrice(指値)
   - `ProductMaterial.cs:43` — SupplyPrice(受給単価)
2. **剩余豁免逐一字段级复核**（不再套先例，见上方修订后裁决表）：
   - OrderProcessNote：仅 Note1/Note2 文本 → 豁免维持
   - EstimateCalcProcess：工程/作業/仕様 皆文本，无単価金額 → 豁免维持
   - QuotationCalc：M:N 关联键 + 状态 FLG + 日期，无货币 → 豁免维持
   - ProductCoProduct：名称 + QtyRatio(产出比率，非钱) + 次工程CD → 豁免维持
   - FscChecklist：追加型履历，无货币字段 → 豁免维持（审查者确认）
   - PlateMold：主档非定价，Rev 追加历史 → 豁免维持（审查者确认）
3. **修正首版误述**：OrderProcess/OrderMaterial「头承载钱、明细不承载钱」的表述与源码矛盾，已更正为「明细行级承载定价字段」。
4. **测试补充**：为新贴实体补 2 真值断言用例（照既有形状，货币字段旧值→新值被捕获），既有 7 用例不动：
   - `Update_OrderProcess_purchaseUnitPrice_writes_op2_diff` → op2 diff，Field=`PurchaseUnitPrice` Old=`100` New=`120`
   - `Update_QuotationDetail_amount_writes_op2_diff` → op2 diff，Field=`Amount` Old=`500` New=`600`

### 复审后覆盖计数
- **纳入 16 实体**（首版 11 + 复审新增 5）；**豁免 6 实体**（全部经字段级实查，均无货币/定价字段）。
- 圈定原则统一为：**实体实含货币/定价字段 → 纳入**（与 OrderDetail 口径一致），不再按头/明细类别套用先例。

### 测试命令与输出
```
$ dotnet test --filter ErpAuditTests
Passed!  - Failed: 0, Passed: 9, Skipped: 0, Total: 9 - CP6.Tests.dll (net8.0)

$ dotnet test
Passed!  - Failed: 0, Passed: 1708, Skipped: 5, Total: 1713 - CP6.Tests.dll (net8.0)
```
- ErpAuditTests：9 绿（7 既有 + 2 新补）。
- 全量：1708 绿（基线 1706 + 2 新增），0 失败，无退化。
- `git status`：仅 5 实体 + 测试文件改动，**无 Migrations 新文件**（IAuditable 空标记不映射列，EF 模型无漂移）。

### Files Changed（复审）
- 5 实体单行追加 `IAuditable`：OrderProcess / OrderMaterial / QuotationDetail / ProductProcess / ProductMaterial
- 测试新增 2 用例：CP6.Tests/Erp/ErpAuditTests.cs（7→9）
- 本报告裁决表修订 + 复审段追加
