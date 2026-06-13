# 财务 成本+多币种+报表+集成+完整性（章06~10）Implementation Plan（初稿）

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

> **工作流（丛书模式）**：我出初稿 → 你修订 → 我评审合并定稿后再编码。**财务第三份（收官）计划**。依赖 Plan 1（GL/TrialBalance/Role/FiscalPeriod）+ Plan 2（AutoVoucherEngine/AP/AR/FinBridgeHook）。

**Goal:** 收口财务模块——章06 成本会计（★差异化卖点：吃 `PaperRoll`/`InkLot` 真实消耗算实际料成本 + 工费标准估算 + 料→WIP→FG→COGS 结转 + 差异分析，FG 单位成本回填 AR 成本结转）；章07 多币种（外币双金额 + 结算已实现汇差 + 期末重估）；章08 三大报表（从科目余额算）；章09+10 集成与完整性内控（凭证不可篡改双保险 + maker-checker 权限点 + gapless 凭证号 + 每日对账 job）。

**Architecture:** 落 `Fin` 命名空间。成本：料按工单从 PaperRoll 残米/InkLot 消耗取**真实用量×批次价**（通用 ERP 给不了的差异化），工费先**标准估算**（CP6 未采集工时），归集到 `CostSheet`，完工 FG 单位成本 = TotalActual/完工数 → 喂 AR 成本结转（Plan 2 的估算成本切真实）；凭证全由 Plan 2 `AutoVoucherEngine` 生成（料→WIP→FG→COGS）。多币种：原币追欠款、本位币记账，三时点（交易冻结/结算 realized/期末重估 unrealized）汇差入 FX_GAIN/FX_LOSS（Role 锚点）。报表：复用 Plan 1 试算表，按 `ReportLineMapping`（科目→报表行配置）重组成资产负债表（期末余额）/损益表（本期发生），永远与总账一致。内控：应用层无 Update/Delete + DB 触发器双保险、财务权限点拆分(maker-checker)、FinSequence gapless、每日 `FinReconciliationWorker` 对账（试算/AP/AR/业务勾稽）。

**Tech Stack:** .NET 8 + EF Core 8 + HostedService（对账 job）+ SQL Server 触发器（不可篡改）/ xUnit + EF Core InMemory / Vue 3.5 + element-plus（报表/差异看板）。源文档：`docs/finance/06·07·08·09·10`。

---

## 关键前置决策（待你修订时确认）

| # | 议题 | 现状/对账 | **本稿建议值** |
|---|---|---|---|
| **F3-D1** | **工时/制造费用数据源** | 06 §3：料有真实数据，工费 CP6 未采集（总纲待定项） | **料用实际(PaperRoll/InkLot)、工费用标准估算(方案a)**：工=标准工时×标准费率、费=成本中心标准分摊率。不改 MES、立刻可用、已比通用 ERP 准。MES 工时报工(方案b)后续。`OeeDaily`/`MachineDowntime` 留作方案b数据源 |
| **F3-D2** | **PaperRoll/InkLot 消耗按工单查询** | 06 §2 调 `ConsumptionByWorkOrderAsync`，但现 WMS 是否有按工单聚合消耗待确认 | ⚠️ 需确认 `PaperRoll`(残米)/`InkLot` 是否记录了"哪张工单消耗了多少"。若无工单关联，成本归集缺料数据源——**这是 F3 最大前置风险**，请确认 WMS 消耗记录是否带 WorkOrderId；无则需先补该关联（可能落 WMS 改造） |
| **F3-D3** | **AR 成本结转切真实成本** | Plan 2 用估算；06 完工 FG 单位成本是真实源 | 06 落地后，AR 成本结转（Plan 2 C-2）从估算切到 `CostSheet` 完工 FG 单位成本；本计划提供 FG 单位成本查询给 AR 用 |
| **F3-D4** | **多币种是否本期做** | MVP 本位币，07 补外币 | 外币 AP/AR + 汇差，若客户暂无外币业务可**整章延后**（标可选）；做则按 07 三时点。建议确认是否有外币供应商/客户 |
| **F3-D5** | **现金流量表** | 08 §5：MVP 延后 v2 | 本计划做**资产负债表 + 损益表**（签约底线），现金流量表 v2 不做 |
| **F3-D6** | **凭证不可篡改 DB 触发器** | 10 §2：应用层 + DB 触发器双保险 | 应用层 JournalEntryService 本就无 Update/Delete（Plan 1）；本计划补 **DB 触发器**禁改/删已过账凭证行（InMemory 测不了，`[需真库]`）；TenantId 延 OA 章10 |

> **测试基建**：xUnit + InMemory。成本归集/差异/汇差计算/报表汇总/对账逻辑可纯单测；DB 触发器、gapless 并发需 `[需真库]`。

---

## File Structure

### 章06 成本会计
- `CostSheet.cs`/`CostSheetLine.cs`（+ CostSheetStatus/CostElement 枚举）
- `ICostCollectService.cs`/`CostCollectService.cs`（CollectMaterial 吃 PaperRoll/InkLot + 工费标准估算）
- `CostSettleService.cs`（完工结转 + FG 单位成本）+ FinBridgeHook 接 WorkOrder 完工事件
- 差异分析 + `cp6.web/src/views/fin/CostSheetView.vue`

### 章07 多币种（可选 F3-D4）
- ApInvoice/ArInvoice 增量字段（OrigGrossAmount/OrigSettledAmount）
- `FxService.cs`（RealizedFxDiff 结算汇差 + RevalueAsync 期末重估）+ 月结挂重估步

### 章08 报表
- `ReportLineMapping.cs`；`IBalanceSheetService.cs`/`BalanceSheetService.cs`、`IIncomeStatementService.cs`/`IncomeStatementService.cs`（复用 Plan 1 TrialBalance）
- `cp6.web/src/views/fin/{BalanceSheetView,IncomeStatementView}.vue`

### 章09+10 集成与完整性
- DB 触发器迁移（trg_JournalLine_NoMutate）
- 财务权限点（fin:voucher:create/post/reverse、fin:period:close/reopen、fin:ap:pay）接 PUB B1
- `FinSequence` gapless 加固（Plan 1 已建，本计划补连续性 + 作废留号）
- `CP6.WebApi/BackgroundServices/FinReconciliationWorker.cs`（每日对账 HostedService）
- 控制科目手工记账保护（Plan 1 IsControl 校验加强）

### 测试
- `CostCollectServiceTests`（实际料/标准工费/差异）、`FxServiceTests`（已实现/重估）、`BalanceSheetServiceTests`/`IncomeStatementServiceTests`（平衡/期末vs本期）、`FinReconciliationTests`、`[需真库]NoMutateTriggerTests`

---

## 实施分四阶段

- **Phase A**（A-1..A-3）：章06 成本会计（★差异化卖点）
- **Phase B**（B-1..B-2）：章07 多币种（可选）
- **Phase C**（C-1..C-2）：章08 三大报表
- **Phase D**（D-1..D-4）：章09+10 集成与完整性内控（收口）

---

# Phase A — 成本会计（章06 ★卖点）

## Task A-1: CostSheet/Line 实体 + 迁移（章06 §4）

**Files:** Create `CostSheet.cs`/`CostSheetLine.cs`; Modify `CP6Context.cs`; migration

- [ ] **Step 1-3: 写实体**（照 06 §4：CostSheet[WorkOrderId→MES/OrderId/CostCenterId/MaterialActual/LaborStd/OverheadStd/TotalActual 计算属性/StandardCost/Variance 计算属性/Status/Lines]、CostSheetLine[Element 料工费/SourceType PaperRoll·InkLot·Stock·工时·费率/SourceId/Qty/UnitCost/Amount/IsStandard]；CostSheetStatus/CostElement 枚举）+ DbSet
- [ ] **Step 4-5: 迁移 + 提交** → `git commit -m "feat(fin): CostSheet/Line entities (ch06 §4)"`

## Task A-2: CostCollectService — 实际料(PaperRoll/InkLot) + 标准工费（章06 §2/§3）★

**Files:** Create `ICostCollectService.cs`/`CostCollectService.cs`; Test `CostCollectServiceTests.cs`

> ⚠️ **前置确认（F3-D2）**：先 `grep` 确认 PaperRoll/InkLot 消耗记录是否带 WorkOrderId。无则本任务的料数据源缺失，需先补 WMS 消耗-工单关联（可能落 WMS 改造，标阻塞项）。

- [ ] **Step 1: 失败测试**（料=PaperRoll 残米用量×批次价 + InkLot 用量×批次价 + 库存领用；工=标准工时×标准费率[IsStandard=true]；费=成本中心标准分摊；TotalActual=料+工+费；Variance=实际−标准[ProductMaster]）
- [ ] **Step 2: 跑红 → Step 3: 实现**（照 06 §2/§3：CollectMaterialAsync[PaperRoll ConsumptionByWorkOrder + InkLot + StockTransaction 领用，真实用量×批次价]；CollectLaborStd/OverheadStd[标准估算 F3-D1]；标准成本取 ProductMaster；CostSheetLine 记每笔来源可追）
- [ ] **Step 4: 跑绿 + 提交** → `git commit -m "feat(fin): cost collection — actual material (PaperRoll/InkLot) + standard labor/oh (ch06 §2/§3)"`

## Task A-3: 完工结转 + FG 单位成本 + 凭证 + 差异看板（章06 §5）

**Files:** Create `CostSettleService.cs`; Modify `FinBridgeHook.cs`(WorkOrder 完工), AR 成本结转(F3-D3); Controllers/UI; Test

- [ ] **Step 1: 失败测试**（工单完工→料工费归集 WIP 凭证 + WIP→FG 结转凭证；FG 单位成本=TotalActual/完工数；该单位成本可被 AR 成本结转取用[切真实成本]）
- [ ] **Step 2: 跑红 → Step 3: 实现**（CostSettleAsync[完工触发：发 Cost.Collect 事件生成料→WIP 凭证、Cost.Settle 事件生成 WIP→FG 凭证经 AutoVoucherEngine]；FinBridgeHook 接 MES WorkOrder 完工事件；FG 单位成本查询 `FgUnitCostAsync(workOrderId)` 供 AR C-2 用[F3-D3 切真实成本]）
- [ ] **Step 4: 跑绿 → Step 5: CostSheet 控制器 + 差异分析看板（实际vs标准，按成本中心切）+ 提交** → `git commit -m "feat(fin): WIP→FG settle + FG unit cost (feeds AR) + variance dashboard (ch06 §5)"`

---

# Phase B — 多币种（章07，可选 F3-D4）

## Task B-1: 外币双金额 + 已实现汇差（章07 §2/§3）

**Files:** Modify `ApInvoice.cs`/`ArInvoice.cs`(OrigGrossAmount/OrigSettledAmount), `ApSettlementService.cs`(FxDiff); Create `FxService.cs`; migration; Test `FxServiceTests.cs`

- [ ] **Step 1: 失败测试**（外币欠款付清按**原币**判定[OrigGross-OrigSettled==0]非本位币；结算汇差=原币×(开票汇率−付款汇率)→FX_GAIN/FX_LOSS；核销 DiffType=FxDiff）
- [ ] **Step 2: 跑红 → Step 3: 实现**（发票加原币双金额；核销按原币匹配；RealizedFxDiff[照 07 §3：apBookValue−paidBookValue]入 FX_GAIN/FX_LOSS[Role 锚点]；与 Plan 2 尾差核销合并到 ApSettlement.DiffType=FxDiff）
- [ ] **Step 4: 跑绿 + 提交** → `git commit -m "feat(fin): foreign currency dual-amount + realized fx diff on settlement (ch07 §2/§3)"`

## Task B-2: 期末重估 unrealized + 月结挂钩（章07 §4）

**Files:** Modify `FxService.cs`(RevalueAsync), `FiscalPeriodService.cs`(月结挂重估); Test

- [ ] **Step 1: 失败测试**（期末未结外币余额按期末汇率重估→重估凭证 FX_GAIN/FX_LOSS；下期初冲回[reverse]避免重复计；月结前跑重估）
- [ ] **Step 2: 跑红 → Step 3: 实现**（RevalueAsync[照 07 §4：未结外币 origOpen×(账面汇率−期末汇率)→重估凭证]；月结 CloseAsync 前挂重估步；下期初冲回）
- [ ] **Step 4: 跑绿 + 提交** → `git commit -m "feat(fin): period-end fx revaluation (unrealized) + reverse next period (ch07 §4)"`

> **若 F3-D4 确认无外币业务**：Phase B 整体跳过，标 TODO，本计划只做本位币 + 成本 + 报表 + 内控。

---

# Phase C — 三大报表（章08）

## Task C-1: ReportLineMapping + 资产负债表（章08 §2/§4）

**Files:** Create `ReportLineMapping.cs`, `IBalanceSheetService.cs`/`BalanceSheetService.cs`; migration; Test `BalanceSheetServiceTests.cs`

- [ ] **Step 1: 失败测试**（资产负债表取资产/负债/权益类**期末余额**[复用 Plan 1 TrialBalance closeBal]按 ReportLineMapping 重组；本年利润并入权益；**资产=负债+权益+利润 必平**；一报表行汇总多科目[货币资金=现金+银行+其他]）
- [ ] **Step 2: 跑红 → Step 3: 实现**（照 08 §2：BuildAsync 复用 TrialBalance，按 AccountType 分资产/负债/权益，CurrentProfit 并入权益，IsBalanced 校验；ReportLineMapping[ReportType/LineName/AccountRoles/DisplayOrder/SubtotalOf] 配置科目→报表行）
- [ ] **Step 4: 跑绿 + 提交** → `git commit -m "feat(fin): balance sheet from account balances + ReportLineMapping (ch08 §2/§4)"`

## Task C-2: 损益表 + 报表 UI（章08 §3）

**Files:** Create `IIncomeStatementService.cs`/`IncomeStatementService.cs`; Controllers/UI; Test

- [ ] **Step 1: 失败测试**（损益表取收入/成本/费用类**本期发生**[区间累计，非时点]；毛利=收入−COGS；净利润=毛利−费用+营业外−所得税；可跨期间汇总[本年累计]）
- [ ] **Step 2: 跑红 → Step 3: 实现**（照 08 §3：MovementRangeAsync 区间发生额，SumByType 按类/Role 汇总，毛利/营业利润/净利润逐层；report controller + BalanceSheet/IncomeStatement UI + 多语言报表行[Sys_Lang]）
- [ ] **Step 4: 跑绿 + 提交** → `git commit -m "feat(fin): income statement (period movement) + report UI (ch08 §3)"`

---

# Phase D — 集成与完整性内控（章09+10 收口）

## Task D-1: 凭证不可篡改 — DB 触发器双保险（章10 §2）

**Files:** migration（trg_JournalLine_NoMutate）; Test `[需真库]NoMutateTriggerTests.cs`

- [ ] **Step 1: 实现迁移**（照 10 §2：`CREATE TRIGGER trg_JournalLine_NoMutate ON JournalLines INSTEAD OF UPDATE,DELETE` — 已过账[Status=2]凭证行禁改/删 THROW；应用层 JournalEntryService 本就无 Update/Delete[Plan 1] = 双保险）
- [ ] **Step 2: `[需真库]` 测**（直接 UPDATE 已过账凭证行 → 被触发器拒）+ 提交 → `git commit -m "feat(fin): DB trigger blocks mutation of posted vouchers (ch10 §2)"`

## Task D-2: 财务权限点 maker-checker + 控制科目保护（章10 §3/§6）

**Files:** Modify JournalEntryService/PeriodService(权限点), GlAccountService(控制科目); 注册权限点（接 PUB B1）

- [ ] **Step 1-3: 实现**——财务权限点拆分（fin:voucher:create/post/reverse、fin:period:close/reopen、fin:ap:pay；接 PUB B1 `[RequirePermission]`，录↔审分授不同角色）；控制科目（IsControl）手工记账拒绝校验加强（只子账驱动）；自动凭证 maker=SYSTEM 不占人工权限。提交 → `git commit -m "feat(fin): finance permission points (maker-checker) + control account guard (ch10 §3/§6)"`

## Task D-3: FinSequence gapless 加固（章10 §4）

**Files:** Modify `FinSequenceService.cs`(Plan 1); Test

- [ ] **Step 1: 失败测试**（同期间凭证号连续无跳号；作废凭证保留号；并发采番不抢同号）`[逻辑 InMemory + 并发需真库]`
- [ ] **Step 2: 跑红 → Step 3: 实现**（gapless：DB 序列/行锁并发安全；作废凭证标记不删号）+ 提交 → `git commit -m "feat(fin): gapless voucher numbering (audit) (ch10 §4)"`

## Task D-4: 每日对账 FinReconciliationWorker（章10 §5 / 09 数据一致性）

**Files:** Create `CP6.WebApi/BackgroundServices/FinReconciliationWorker.cs`; Test `FinReconciliationTests.cs`

- [ ] **Step 1: 失败测试**（对账覆盖：①试算不平 ②AP 子账≠GL 应付控制 ③AR 子账≠GL 应收控制 ④已确认出货无对应 AR 凭证；任一不一致→issue 告警）
- [ ] **Step 2: 跑红 → Step 3: 实现**（照 10 §5：HostedService 每日跑 4 项对账[复用 Plan 1 TrialBalance + Plan 2 ReconcileAp/Ar + 出货-凭证勾稽]，issue→SignalR 财务看板 + DeadLetterNotifier 告警，复用现有基建）
- [ ] **Step 4: 跑绿 → Step 5: 注册 HostedService + 全量构建全测 + 提交** → `git commit -m "feat(fin): daily reconciliation worker (trial/AP/AR/biz) (ch10 §5)"`

---

## Self-Review（对照章06~10 覆盖）

- **章06**：CostSheet 归集(A-1) ✅ / 实际料 PaperRoll/InkLot(A-2) ✅ / 工费标准估算(A-2,F3-D1) ✅ / 料→WIP→FG→COGS 凭证(A-3) ✅ / 完工 FG 单位成本→AR(A-3,F3-D3) ✅ / 差异分析(A-3) ✅ / 成本中心切分(A-2/A-3) ✅
- **章07**：原币双金额(B-1) ✅ / 原币判付清(B-1) ✅ / 结算已实现汇差(B-1) ✅ / 期末重估 unrealized+冲回(B-2) ✅ / FX Role 锚点(B-1/B-2) ✅（可选 F3-D4）
- **章08**：报表算非存(C-1/C-2) ✅ / 资产负债表期末余额+必平(C-1) ✅ / 损益表本期发生(C-2) ✅ / ReportLineMapping 配置(C-1) ✅ / 现金流量表延后(F3-D5) ⏳
- **章09+10**：不可篡改双保险(D-1) ✅ / maker-checker 权限点(D-2) ✅ / 控制科目保护(D-2) ✅ / gapless 凭证号(D-3) ✅ / 每日对账(D-4) ✅ / 财务禁软删/decimal(贯穿 Plan1-3) ✅

**已知缺口/推迟（已标注）：**
1. **⚠️ PaperRoll/InkLot 消耗-工单关联**（F3-D2）—— 成本料数据源前提，需确认 WMS 是否记录 WorkOrderId，无则先补（可能阻塞 A-2）。
2. **工时实际采集（方案b）**（F3-D1）—— MES 工时报工后续，本计划工费标准估算。
3. **多币种**（F3-D4）—— 无外币业务可整章延后。
4. **现金流量表**（F3-D5）—— v2。
5. **TenantId**（F3-D6）—— OA 章10 统一。
6. **PUB 权限点接入**（D-2）—— PUB B1 落地后 `[RequirePermission]`。

**Type 一致性：** `CostSheet.TotalActual`/FG 单位成本(A-3) 喂 Plan 2 AR 成本结转(F3-D3)；成本凭证经 Plan 2 `AutoVoucherEngine`(A-3)；FX 用 Plan 1 `Role` FX_GAIN/FX_LOSS(B-1/B-2)；报表复用 Plan 1 `TrialBalance`(C-1/C-2)；对账复用 Plan 2 `ReconcileApAsync`(D-4)；触发器保护 Plan 1 `JournalLine`(D-1)。

---

## 执行交接

计划存 `docs/superpowers/plans/2026-06-13-fin-cost-statements.md`。**财务第三份（收官）**。至此 **财务三份计划全齐**：
1. `2026-06-13-fin-gl-kernel-period.md`（总账内核+期间）
2. `2026-06-13-fin-ap-ar-voucher.md`（AP+AR+自动凭证引擎）
3. `2026-06-13-fin-cost-statements.md`（成本+多币种+报表+集成+完整性）← 本文

三份覆盖财务全章 00~10。**下一步按工作流是你修订**（拍板 F3-D1~D6，尤其 **F3-D2 PaperRoll/InkLot 工单关联**——成本卖点的数据前提）。定稿后执行：总账地基 → AP/AR/引擎 → 成本/报表/内控。

---

*初稿生成于 2026-06-13。源：docs/finance/06·07·08·09·10。已勘察：PaperRoll/InkLot/WorkOrder/OeeDaily(MES/WMS 现成,工单关联待确认)、ProductMaster(标准成本)、FxRate(Gap4.3)、Sys_Lang/HostedService/SignalR/DeadLetterNotifier 现成、Plan1 TrialBalance/Role/Plan2 AutoVoucherEngine/Reconcile 前置、零多租户。*
