# F1 财务油路接通 Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development 逐任务执行（编码 Opus 4.8）。每任务出详细 brief 时引用本计划的 API 事实 + spec `docs/superpowers/specs/2026-07-07-fin-integration-loop-design.md`。步骤用 `- [ ]` 复选框跟踪。

**Goal:** 让"完整 ERP"在会计正确性上成立——库存移动实时过账、出货自动确认收入、成本闭合、年度可锁。

**Architecture:** 复用既有"规则即数据"记账机床（`AutoVoucherEngine.GenerateAsync(FinBizEvent)` + `Fin_PostingRule` + `IJournalEntryService.AutoPostAsync`）。新增各业务事件→GL 的**触发点火器**（桥/直调），不改凭证引擎本体。过账归属分工避免双记：**B=出货收入+COGS / C=生产料工费→WIP→FG / A=其余库存移动（采购入库·盘盈亏·报废）/ D=年结损益结转**。

**Tech Stack:** ASP.NET Core + EF Core（SQL Server；测试 InMemory）；xUnit；既有 Fin 服务族。

## Global Constraints

- 基线不许跌：后端全量测试（当前 1589 绿）+ 前端 type-check 0；**每 commit 立即 push**（用户硬性纪律）。
- 记账一律走 `IJournalEntryService.AutoPostAsync`（自动凭证）或 `AutoVoucherEngine.GenerateAsync`；**禁手工 new 凭证绕过借贷平衡/锁期校验**（AutoPostAsync 内已兜底 E-FIN-112/113 + ValidateBalance）。
- 幂等：所有自动凭证用 `Source + SourceDocNo` 幂等（引擎已按 `Source+SourceDocNo+Posted` 查重）；`SourceDocNo` 用业务单号（TxnNo/出库号/工单号/期间）。
- 科目按 `GlAccount.Role`（`a.Role==role && a.IsActive`）解析，缺失 `Fail("E-FIN-141", role)`。可用 Role：`INVENTORY/WIP/FG/COGS/DIRECT_MATERIAL/DIRECT_LABOR/MFG_OVERHEAD/REVENUE/TAX_OUTPUT/AR_CONTROL/RETAINED_EARNINGS`；本年利润科目 COA 已有 `3103 本年利润`（Equity/Credit，无 Role）+ `3104 未分配利润`（Role=`RETAINED_EARNINGS`）。
- 横切（权限/审计/i18n/错误码）按 `docs/00-横切接线规范.md`：新写端点贴 `[RequirePermission("fin-xxx","action")]`（连字符键）+ 逐租户 MenuAction/RoleAction 种子；E-FIN 错误码水位对照现有码表续号（现有到 E-FIN-150）。
- **两拍板（已固化 spec §5，commit 542bb61）**：①反冲负库存不足=允许负库存+告警（非拒绝报工）②成本差异月末=结转 COGS（科目月末清零）。

---

## 波 0：地基（凭证来源 + 库存过账规则种子 + WMS→Fin 桥骨架）

### Task 0.1：新增 VoucherSource.Inventory + 库存过账 PostingRule 种子

**Files:**
- Modify: `CP6.Entity/DomainModels/Fin/JournalEntry.cs`（`VoucherSource` enum 加 `Inventory = 10`）
- Modify: `CP6.Core/Services/Fin/PostingRuleSeed.cs`（加库存移动规则）
- Test: `CP6.Tests/Fin/InventoryPostingRuleSeedTests.cs`

**Interfaces:**
- Produces: EventType 常量 `Inventory.Received`（采购入库）/`Inventory.AdjustGain`（盘盈）/`Inventory.AdjustLoss`（盘亏）/`Inventory.Scrapped`（报废）；各规则的借贷科目 Role 映射。

**规则设计（借/贷，金额字段 `Amount`）：**
- `Inventory.Received`：借 `INVENTORY` / 贷 Role `GRNI`（暂估应付/待勾稽——若无此 Role 则新增 COA 行 `2202 暂估应付账款` Role=`GRNI`，见 Task 0.1b）。金额=入库 `Qty×UnitPrice`。
- `Inventory.AdjustGain`：借 `INVENTORY` / 贷 `NON_OP_INCOME`（盘盈利得）。
- `Inventory.AdjustLoss`：借 `PENDING_PROPERTY_LOSS`（待处理财产损溢）/ 贷 `INVENTORY`。
- `Inventory.Scrapped`：借 `NON_OP_EXPENSE`（营业外支出/报废损失）/ 贷 `INVENTORY`。
- 用 `PostingRuleSeed` 现有工厂 `Role(no,side,role,amountField,...)`；VoucherSource=`Inventory`。

- [ ] Step: 写失败测试（种子后 `PostingRules` 含上述 4 个 EventType，各 Lines 借贷 Role 正确、`ValidateBalance` 意义上单边金额字段一致）→ 跑失败 → 实现 → 跑过 → commit+push。

### Task 0.1b：确认/补 GRNI 科目

**Files:** Modify `CP6.Core/Services/Fin/FinCoaTemplate.cs`（若 CN-GAAP/INTL 模板无 `GRNI` Role 的暂估应付科目则补一行；否则复用现有）。
- [ ] Step: 读 `FinCoaTemplate` 确认有无 GRNI/暂估应付；无则补 `2202 暂估应付账款`(Liability,Credit,Role=`GRNI`) + INTL `2150 Goods Received Not Invoiced`；测试断言 `Get(scheme)` 含该 Role。commit+push。

### Task 0.2：WMS→Fin 库存过账桥骨架（IStockFinBridge + NoOp + DI）

**Files:**
- Create: `CP6.Core/Services/Integration/IStockFinBridge.cs`、`CP6.Core/Services/Fin/StockFinBridge.cs`（继承 `BridgeHookBase`）
- Modify: `CP6.WebApi/Program.cs`（DI 注册，`StockFinBridge:Enabled` 开关→NoOp）
- Test: `CP6.Tests/Fin/StockFinBridgeTests.cs`

**Interfaces:**
- Produces: `Task<FinBridgeResult> OnStockMovedAsync(StockTransaction txn, string relatedType, string? userName)`。内部：按 `txn.TxnType + relatedType` 决定 EventType（见过账归属表），构造 `FinBizEvent{EventType, Source=Inventory, SourceDocNo=txn.TxnNo, BizDate, HeaderAmounts["Amount"]=txn.Qty*txn.UnitPrice}` → `IAutoVoucherEngine.GenerateAsync`；末尾 `PersistEventAsync("WMS","FIN",nameof(OnStockMovedAsync),txn.TxnNo,...)`。best-effort try/catch。
- **过账归属过滤（关键，避免与 B/C 双记）**：仅对 `{TxnType=IN 且 relatedType∈采购入库} / {ADJ 盘盈亏} / {relatedType=SCRAP}` 生成凭证；`relatedType∈{生产领料 ISSUE, 生产完工 FG, 销售出库 SHIP}` 一律 `Skipped`（这些由 C/B 过账）。relatedType 判据用 `StockMovementRequest.RelatedType` 现值（Task A 前先 grep 出实际 RelatedType 取值集合逐一映射）。

- [ ] Step: 写失败测试（喂一条 IN/采购 txn → 生成 `Inventory.Received` 凭证借 INVENTORY 贷 GRNI；喂一条 SHIP txn → Skipped 无凭证）→ 失败 → 实现 → 过 → commit+push。

---

## 波 B：出货 → 开票 → 红冲油路（最高价值，最干净，先做）

### Task B.1：ShipAsync 点火 FIN 开票桥

**Files:**
- Modify: `CP6.Core/Services/Wms/OutboundService.cs`（构造器注入 `IFinBridgeHook`+NoOp 回退；`ShipAsync` :527 `_erpBridge` 调用旁，仅当 `OutboundType.Shipping` 组装 `FinShipmentInvoiceRequest` 调 `_finBridge.OnShipmentConfirmedAsync`）
- Modify: `CP6.WebApi/Program.cs`（确认 OutboundService 能拿到 IFinBridgeHook）
- Test: `CP6.Tests/Wms/OutboundShipFinBridgeTests.cs`

**Interfaces:**
- Consumes: `IFinBridgeHook.OnShipmentConfirmedAsync(FinShipmentInvoiceRequest, string?)`；`FinShipmentInvoiceRequest{ShipmentId,OrderId?,WorkOrderNo?,CustomerId,InvoiceDate,DueDate,CurrencyCd?,FxRate?,EstimatedCost,Lines:List<FinShipmentInvoiceLine{ItemId?,Qty,UnitPrice,TaxCodeId?,RevenueAccountId?,CostCenterId?}>}`（定义在 `CP6.Core/Services/Fin/IArInvoiceService.cs`）。
- **组装来源**：ShipmentId=出库号（幂等键）；CustomerId/OrderId 从出库单关联受注取；Lines 从出库明细（ProductCd→ItemId、Qty、售价 UnitPrice 从受注/价表取）；WorkOrderNo 若关联工单则填（ArInvoice 据此切真实 FgUnitCost）；EstimatedCost 回退。**售价来源需 grep OutboundOrder/受注关联确认字段**。
- best-effort：try/catch 包裹，FIN 失败不阻断出货（已 SaveChanges）。

- [ ] Step: 写失败测试（mock/真 IFinBridgeHook，ShipAsync 后断言 OnShipmentConfirmedAsync 被调、request.ShipmentId=出库号、Lines 数=出库明细数）→ 失败 → 实现组装+调用 → 过 → commit+push。

### Task B.2：出货取消 → AR 红冲点火

**Files:** Modify `OutboundService.cs`（`CancelOrderAsync` :254 里，若该出库已开票则调 `_finBridge.OnShipmentCancelledAsync(出库号, user)`）；Test `CP6.Tests/Wms/OutboundCancelFinReverseTests.cs`。
- [ ] Step: 写失败测试（已确认出货取消 → OnShipmentCancelledAsync 被调）→ 失败 → 实现 → 过 → commit+push。注：Completed 状态不可取消（:260 现有），红冲路径覆盖 Cancel 前的已开票场景；若产品要求完工后红冲需另开票据红冲入口（记 follow-up）。

---

## 波 A：库存移动 GL（采购入库·盘盈亏·报废，Option Y 归属）

### Task A.1：ApplyAsync 后置点火 StockFinBridge

**Files:**
- Modify: `CP6.Core/Services/Wms/StockMovementService.cs`（`ApplyAsync` :124 `SaveChangesAsync` 之后、通知之前，注入 `IStockFinBridge?`（可空+NoOp），调 `OnStockMovedAsync(txn, req.RelatedType, req.OperatorCd)`，best-effort）
- Test: `CP6.Tests/Wms/StockMovementGlPostingTests.cs`

**Interfaces:** Consumes `IStockFinBridge.OnStockMovedAsync`（波0.2）。
- **不改 ApplyAsync 库存计算逻辑**，只在末尾加桥调用；桥内部按归属过滤决定是否真过账。
- [ ] Step: 写失败测试（采购 IN 移动 → 产生 `Inventory.Received` 凭证；生产 ISSUE 移动 → 无凭证[C 负责]；盘盈 ADJ+ → `Inventory.AdjustGain`）→ 失败 → 实现点火 → 过。跑全量确认 StockMovement 既有测试不破 → commit+push。

### Task A.2：盘点差异 ADJ 过账验证 + 报废入口

**Files:** 复核 `StockTakeService.ApproveAndApplyAsync`（差异走 ADJ→ApplyAsync→A.1 自动过账盘盈亏）；确认报废走 ApplyAsync 时 relatedType=SCRAP 触发 `Inventory.Scrapped`。Test `CP6.Tests/Fin/StockTakeAdjustGlTests.cs`。
- [ ] Step: 写测试（盘亏承认 → INVENTORY 贷记 + 待处理损溢借记）→ 实现（若 relatedType 未透传则补）→ 过 → commit+push。

---

## 波 C：完工反冲 + 成本归集 + 差异结转

### Task C.1：完工点生成原料反冲 OUT 移动（负库存允许+告警）

**Files:**
- Modify: `CP6.Core/Services/Mes/ProductionResultService.cs`（`WriteAsync` case 4 `justCompleted` 分支 :257 旁，全工序完工后按 BOM 定额反冲）
- Modify/Create: 反冲服务 `CP6.Core/Services/Mes/BackflushService.cs`（封装反冲逻辑，避免污染报工核心）
- Test: `CP6.Tests/Mes/BackflushTests.cs`

**Interfaces:**
- 反冲量 = `ProductMaterial.UnitUsage`（按 `ProductCd+ProcessCd+MaterialCd`，UsageType=2 定额）× `wo.CompletedQty`；对 UsageType=1（尺寸驱动）的料按现有尺寸算法（grep 复用 CostCollect/尺寸逻辑）。
- 每原料生成 `StockMovementService.ApplyAsync(StockMovementRequest{TxnType=WmsTxnType.OUT, RelatedType="ISSUE", RelatedNo=workOrderNo, ProductCd=料, Qty=反冲量, WarehouseCd=原料仓})`。
- **负库存守卫（拍板①）**：反冲仓 `Warehouse.AllowNegative` 对反冲路径视为 true——若不足，允许负库存记账 + 发告警（`IWmsNotifier` 或新告警通道 `IBackflushNotifier`），**不抛异常阻断报工**。实现：反冲专用 warehouse 设 AllowNegative 或反冲调用前临时放行 + 告警；具体机制 brief 时定（倾向：反冲 request 带 `AllowNegativeOverride=true` 标志，ApplyAsync 负库存守卫尊重该标志）。
- best-effort：反冲失败记录+告警，不回滚已提交报工（账实差异后续盘点吸收，符合 spec）。

- [ ] Step: 写失败测试（工单完工 CompletedQty=10、BOM 定额 2/件 → 反冲 OUT 20；库存不足 → 允许负库存+发告警不抛错）→ 失败 → 实现 BackflushService+接线 → 过 → commit+push。

### Task C.2：完工触发成本归集 + 结转

**Files:** Modify `ProductionResultService`/`FinBridgeHook`（完工 justCompleted 调 `IFinBridgeHook.OnWorkOrderCompletedAsync(workOrderNo, user)` → CollectAsync；再触发 `CostSettleService.SettleAsync`）；Test `CP6.Tests/Fin/WorkOrderCompleteCostFlowTests.cs`。
- **注意**：CostSettle 现有 借WIP/贷INVENTORY（料）已过账料的存货减少——与 C.1 反冲 OUT 移动的存货减少**须择一过账**（避免双记）。归属决定：**反冲 OUT 移动由 A 归属过滤 Skipped（relatedType=ISSUE 不过账），料的存货→WIP 由 CostSettle 过账**。C.1 的反冲只做物理库存扣减（账实），不产 GL；GL 的料→WIP 归 CostSettle。二者协同：反冲扣物理量，CostSettle 按 MaterialActual 过账金额。**brief 时确认 CostSettle 的 MaterialActual 是否等于反冲消耗**（应对齐，否则记差异票）。
- [ ] Step: 写测试（完工 → CollectAsync 落 CostSheet → SettleAsync 产 料工费→WIP + WIP→FG 两凭证）→ 实现接线 → 过 → commit+push。

### Task C.3：成本差异结转 COGS（拍板②）

**Files:** Modify `CP6.Core/Services/Fin/CostSettleService.cs`（`SettleAsync` 扩展：TotalActual 与 Standard 的差异生成差异凭证）；Test `CP6.Tests/Fin/CostVarianceSettleTests.cs`。
- 差异 = `sheet.TotalActual - sheet.StandardCost`（Material/Labor/Overhead 三行差异合计，一科目起步）。生成差异凭证：差异>0（实际>标准，超支）借 `COGS` / 贷 `WIP`（或差异科目）；差异<0 反向。**拍板②=月结时差异科目余额结转 COGS**——一科目起步可直接在结转时借/贷 COGS 对 WIP 差额，科目月末自然清零（不设独立留存差异科目）。
- [ ] Step: 写失败测试（实际>标准 → 差异凭证借 COGS 贷 WIP 差额；实际=标准 → 无差异凭证）→ 失败 → 实现 → 过 → commit+push。

---

## 波 F：盘点冻结（过账基线正确性前置）

### Task F.1：盘点单开启冻结所涉库位出入库

**Files:**
- Modify: `CP6.Core/Services/Wms/StockTakeService.cs`（`StartCountAsync` 冻结所涉库位、`ApproveAndApplyAsync`/`CancelAsync` 解冻）
- Modify: `CP6.Entity/DomainModels/Wms/StockTake.cs` 或复用 `Location.IsBlocked`（推荐：盘点冻结用独立 `StockTakeDetail` 关联库位标志或盘点期活跃单查询，避免与 Location.IsBlocked 手工冻结语义混淆）
- Modify: `CP6.Core/Services/Wms/StockMovementService.cs`（`ApplyAsync` 加盘点冻结校验：OUT/MOVE 若目标库位在活跃盘点中则拒绝 `E-WM-xxx`，**放行 ADJ**[盘点承认自身走 ADJ]）
- Modify: `OutboundService.AllocateAsync`（`FindCandidateStockAsync` :325 排除盘点冻结库位）
- Test: `CP6.Tests/Wms/StockTakeFreezeTests.cs`

**Interfaces:** 冻结判定方法 `Task<bool> IsLocationFrozenAsync(warehouseCd, locationCd)`（查是否有 Status∈{Counting,DiffReview,AwaitingApproval} 的盘点单覆盖该库位）。
- [ ] Step: 写失败测试（库位盘点中 → 该库位 OUT 被拒；盘点承认的 ADJ 放行；盘点完成/取消后解冻可 OUT）→ 失败 → 实现 → 过 → commit+push。

---

## 波 D：年度结账

### Task D.1：YearCloseAsync 损益结转 + 期初 + 锁年

**Files:**
- Modify: `CP6.Core/Services/Fin/PeriodCloseService.cs`（加 `YearCloseAsync(int fiscalYear, string userId)`）
- Modify: `CP6.Entity/DomainModels/Fin/FiscalPeriod.cs`（`PeriodStatus` 加 `YearClosed=2`？或年度锁用独立标志——brief 定；至少锁年后拒绝该年凭证）
- Modify: `CP6.Core/Services/Fin/IJournalEntryService`（AutoPost 锁年校验）
- Test: `CP6.Tests/Fin/YearCloseTests.cs`

**Interfaces:**
- ① 校验该财年 12 期全 Closed（否则 E-FIN-xxx）。
- ② 损益结转凭证（`Source=VoucherSource.Carryover`）：查所有 `GlAccount.Type∈{Revenue,Expense}` 科目的年末余额，逐一反向清零，净额转入 `3103 本年利润`（收入借记冲平/费用贷记冲平，差额=净利入 3103 贷方）。再一张：`3103 本年利润 → 3104 未分配利润`（Role=RETAINED_EARNINGS）。均 `AutoPostAsync`。
- ③ 资产负债类（Asset/Liability/Equity）科目生成下年期初余额（结转分录或期初快照——brief 定，倾向期初快照表避免污染凭证）。
- ④ 锁年：已锁财年任何凭证（含手工）拒绝 E-FIN-112 扩展。
- ⑤ `ReopenYearAsync`（高危独立权限 action，红冲年结凭证 `ReverseAsync`，审计留痕）。

- [ ] Step: 写失败测试（造收入 1000/费用 600 两期已结 → YearClose → 3103 本年利润贷 400 → 3104 未分配利润贷 400；收入费用科目余额清零）→ 失败 → 实现 → 过 → commit+push。

### Task D.2：BalanceSheet 本年利润改期间口径

**Files:** Modify `BalanceSheetService.BuildAsync`（本年利润=本财年内损益，年结后损益科目已清零自然归正；跨年前用"本财年起累计"而非建账累计）；Test `CP6.Tests/Fin/BalanceSheetCurrentProfitTests.cs`。
- 实现：`TrialBalanceService` 的期初口径改为按"本财年 PeriodStart"截断（而非建账），或 BalanceSheet 单独算本财年损益。brief 定最小改动路径。
- [ ] Step: 写失败测试（跨年后本年利润=本财年损益非累计）→ 失败 → 实现 → 过 → commit+push。

---

## 波 E：油路探测器（跨模块闭环 E2E）

### Task E.1-E.4：四条端到端

**Files:** Create `CP6.Tests/Fin/OilRouteE2ETests.cs`（InMemory 全链）。
- E1 采购入库 → `Inventory.Received` GL（借 INVENTORY 贷 GRNI）。
- E2 出货确定 → AR 收入凭证（借 AR 贷 REVENUE+TAX）+ COGS 凭证（借 COGS 贷 FG）。
- E3 工单完工 → 反冲扣料（物理）+ 料工费→WIP→FG（借WIP/贷INV/…、借FG/贷WIP）+ 差异结转 COGS。
- E4 盘点冻结 → 差异承认 → 盘盈亏 GL（`Inventory.AdjustGain/Loss`）。
- [ ] Step: 逐条写 E2E（每条独立 Fact，跑通全链断言凭证科目+金额）→ 全绿 → commit+push。

---

## 波 G：横切收口（权限/审计/i18n/错误码）

### Task G.1：新增 Fin 端点权限 + 年结高危 action + E-FIN 词条

**Files:** 若本包新增控制器端点（年结 YearClose/ReopenYear、反冲手工触发等）贴 `[RequirePermission("fin-period","year-close"/"reopen-year")]` 等 + 逐租户 MenuAction/RoleAction 种子（照 M-WMS T3b 模式）；E-FIN 新错误码五语词条入 `I18n*Seed`。反射测试纳入 Fin 命名空间（若已有则扩展）。
- [ ] Step: 权限键清单→贴点→种子→反射测试→词条；全量绿 → commit+push。

---

## Self-Review 记录

1. **Spec 覆盖**：§3 存货过账→波0+波A；§4 出货开票红冲→波B；§5 反冲+成本+差异→波C（负库存拍板①/差异 COGS 拍板②）；§6 年结→波D；§7 探测器→波E；盘点冻结（§3 尾）→波F；§8 横切→波G。四条 T1 断裂全覆盖（①=B、②=A、③=C.3、④=D）+ T4 反冲=C.1。
2. **过账归属零双记**：料→WIP 归 CostSettle（波C），A 对生产 ISSUE/FG/SHIP 移动 Skipped；A 只过采购入库/盘盈亏/报废；B 过收入+COGS。三方分工在波0.2 桥过滤 + 波A 测试锁定。
3. **拍板落点**：①负库存 allow+warn→C.1 的 AllowNegativeOverride+告警；②差异→COGS→C.3。
4. **开工前待 grep 确认（brief 时）**：StockMovementRequest.RelatedType 实际取值集（映射归属过滤）；出货售价字段来源（B.1 组装 Lines）；ProductMaterial UsageType=1 尺寸算法复用点（C.1）；CostSettle.MaterialActual 是否等于反冲消耗（C.2 协同）；年结期初实现路径（D.1 快照 vs 结转分录）。这些是技术定位非用户决策。
5. **风险**：波A/C 的过账归属是最易双记处，波E E2E 是终验闸；波D 年结涉报表口径改动，D.2 需回归既有财务报表测试。
