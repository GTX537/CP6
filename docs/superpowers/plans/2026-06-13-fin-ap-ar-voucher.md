# 财务 AP + AR + 自动凭证引擎（章03+04+05）Implementation Plan（初稿）

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

> **工作流（丛书模式）**：我出初稿 → 你修订 → 我评审合并定稿后再编码。**财务第二份计划**。依赖 **财务 Plan 1（总账内核+期间）已落地**（GlAccount/Role/JournalEntry/AutoPostAsync/ReverseAsync/FiscalPeriod）。

**Goal:** 让 CP6 第一次"业务动作自动变成会计凭证"——章05 自动凭证引擎（`PostingRule` 规则即数据 + `AutoVoucherEngine` 解释器 + `FinBridgeHook` 挂 Phase6 异步生成）；章03 应付 AP（★MVP：供应商发票→付款→核销，子账与 GL 勾稽）；章04 应收 AR（镜像 AP + **出货自动开票**吃 CP6 现成 Order/Outbound + 信用控制）。完成后能演示"采购欠款→付款→账自动平"和"确认出货→自动确认收入/结转成本"。

**Architecture:** 落 `Fin` 命名空间。核心是**一台引擎、规则千变**：`AutoVoucherEngine.GenerateAsync(FinBizEvent)` 四步——幂等(按 SourceDocNo) → 找 `PostingRule`(按 EventType) → 拼凭证(FixedRole 固定行按 Role 锚点取科目 + DocumentLines 透传炸开单据行各进各科目) → `AutoPostAsync` 直过(复用 Plan 1，含借贷恒等+锁期双保险)。**复用 CP6 Phase6 基建**：`FinBridgeHook : BridgeHookBase` 挂 `IntegrationEvent` 异步驱动(重试/死信现成)，不内联业务事务。AP/AR 三段(发票/收付款/核销)各生成凭证经引擎；核销是勾稽不产新凭证(尾差除外)；红冲/撤销跟随触发源(系统 autoPost 直过)。子账余额永远=GL 控制科目余额(月结对账第一刀)。

**Tech Stack:** .NET 8 + EF Core 8 + Phase6 IntegrationEvent/BridgeHookBase（复用）+ SignalR（账龄预警）/ xUnit + EF Core InMemory / Vue 3.5 + element-plus。源文档：`docs/finance/03·04·05`。

---

## 关键前置决策（待你修订时确认）

| # | 议题 | 现状/对账 | **本稿建议值** |
|---|---|---|---|
| **F2-D1** | **MVP 币种** | 03/04 明确 MVP 本位币，外币延 07 章 | 本计划**只跑本位币**（CurrencyCd/FxRate 字段预留但 =1）；外币 AP/AR + 汇差 = Finance Plan 3 章07。DiffType.FxDiff 枚举预留不实现 |
| **F2-D2** | **AR 成本结转金额来源** | 04 §2：成本来自 06 章 FG 单位成本，没有则估算 | **AR 成本结转用估算成本**（标准成本/上次实际）起步，Finance Plan 3 章06 落地后自动切真实成本（总纲 §5 跨阶段依赖：别卡等 06） |
| **F2-D3** | **采购三单匹配** | 03：ApInvoice.PurchaseOrderId 预留，采购模块未建 | **AP 发票手工录起步**，PurchaseOrderId 可空预留；三单匹配属采购模块（其计划）落地后开启 |
| **F2-D4** | **FinBridgeHook 挂点** | 05 §5：挂 IErpBridgeHook 出货链 + 新建 IFinBridgeHook | 新建 `IFinBridgeHook : BridgeHookBase` + Dispatcher 路由（仿 SPACE/WMS 路由模式，见既有 IntegrationEventDispatcher）；出货确认事件→AR 自动开票，出货取消→红冲 |
| **F2-D5** | **TenantId/审计/权限** | 同 Plan 1 | TenantId 延 OA 章10；审计字段以代码为准；制单/复核/收付款权限点接 PUB B1（落地后），本阶段服务层校验 |
| **F2-D6** | **CreditNote 复用** | 04 §4：销售退货红字接现有 CreditNote(Phase 10a) | 复用现有 `CreditNote`（DbContext 已有 DbSet）→ 生成 AR 红字发票 + 红冲；不新建销售红字实体 |

> **测试基建**：xUnit + InMemory。引擎幂等/规则拼凭证/Role 取科目、AP 核销多对多+尾差、子账勾稽、红冲补偿、AR 自动开票幂等、信用控制可纯单测（核心价值，doc 已给代码）。`(Source,SourceDocNo)` 过滤唯一索引需 `[需真库]` 兜底。

---

## ✅ 决策定稿（2026-06-15，用户逐项拍板）

| 决策 | 最终值 |
|---|---|
| **F2-D1** | **全外币**：实体原币+本位币(JPY)双金额列 + 已实现汇差 + **期末未实现汇兑重估**。复用现成 `FxRate`（基轴=JPY，受注冻结汇率用法已成熟）。新增 GL 角色 `FX_GAIN_LOSS`。`DiffType.FxDiff` 由占位转实做。 |
| **F2-D2** | AR 成本结转用**估算成本**起步，章06 落地后自动切真实成本。 |
| **F2-D3** | **预定义 `IFinAp` 契约接口**（AP 实现，采购 #10 落地接真实）；AP 手工录起步，`PurchaseOrderId` 可空预留。 |
| **F2-D4** | 异步**新建 `IFinBridgeHook`** + `WMS\|FIN` Dispatcher 路由（复用 Phase6 重试/死信，NoOp 可关）。 |
| **F2-D5** | Fin 控制器 **day-1 贴 `[RequirePermission]` + 权限点 seed**（含回贴已建的 4 个 GL 控制器，保持模块一致）；TenantId 延 OA 章10。 |
| **F2-D6** | 复用 `CreditNote` + 补 `ArInvoice.CreditNoteId` 关联；不新建销售红字实体。 |

### 因定稿带来的任务增量（相对初稿）
- **全外币贯穿 A/B/C**：`FinBizEvent` 带 `CurrencyCd`/`FxRate`；引擎过账原币×汇率→本位币，`JournalLine` 已有原币列(CurrencyCd/FxRate/OrigAmount)直接填；核销时按发票汇率vs收付款汇率差额算**已实现汇兑损益**写 `FX_GAIN_LOSS`。
- **新增 Task D-1 期末汇兑重估**：挂已建好的章02 `PeriodCloseService`，关账时重估未核销外币 AP/AR 余额→未实现汇兑损益凭证（autoPost）。
- **Task A-1 增** `IFinAp` 契约接口骨架（D3）。
- **每个控制器任务增** `[RequirePermission]` + 权限点 seed（D5）。
- **GL 地基已勘察确认**：Fin 实体继承 `BaseEntity`；DbSet 注册于 `CP6Context` 财务 region 并配索引；`JournalLine` 原币列现成；`IJournalEntryService.AutoPostAsync(JournalEntry)`/`ReverseAsync(id,maker,reason,autoPost)` 即引擎/补偿入口；`GlAccount.Role` 锚点就位。

---

## File Structure

### 章05 自动凭证引擎（`Fin` + `Integration`）
- `PostingRule.cs`/`PostingRuleLine.cs`（+ PostingSide/RuleLineSource 枚举）
- `FinBizEvent.cs`（事件载荷：头字段 + DocLines + PartnerId + CostCenterId + GetAmount/GetGuid 取值）
- `IAutoVoucherEngine.cs`/`AutoVoucherEngine.cs`（GenerateAsync 四步）
- `IFinBridgeHook.cs`/`FinBridgeHook.cs`（BridgeHookBase；出货/AP/取消事件）+ Dispatcher 路由注册
- PostingRule seed（AP.InvoicePosted/AP.Payment/AR.Revenue/AR.Cogs 规则）

### 章03 AP（`Fin`）
- `ApInvoice.cs`/`ApInvoiceLine.cs`/`Payment.cs`/`BankAccount.cs`/`ApSettlement.cs`/`TaxCode.cs`（+ 枚举）
- `IApInvoiceService.cs`/`ApInvoiceService.cs`（录入防重 + 过账发事件 + 红冲）、`PaymentService.cs`（付款 + 预付 + 撤销）、`ApSettlementService.cs`（核销多对多 + 尾差）、`ApReconcileService.cs`（子账↔GL）、`ApAgingService.cs`（账龄）

### 章04 AR（`Fin`，镜像 AP）
- `ArInvoice.cs`/`ArInvoiceLine.cs`/`Receipt.cs`/`ArSettlement.cs`（镜像）
- `ArInvoiceService.cs`（CreateFromShipment 自动开票）、`ReceiptService.cs`、`ArSettlementService.cs`、`CreditControlService.cs`（信用额度）、AR 红字接 CreditNote

### 控制器 + DI + 迁移 + 前端 + 测试
- `Controllers/Fin/{ApInvoiceController,PaymentController,ArInvoiceController,ReceiptController,PostingRuleController}.cs`
- 迁移 `*_FinApArVoucher`；`BusinessPartner` 加 `CreditLimit`
- `cp6.web/src/views/fin/{ApInvoiceView,PaymentView,ApAgingView,ArInvoiceView,ReceiptView,ArAgingView,PostingRuleView}.vue`
- 测试：`AutoVoucherEngineTests`（★幂等/规则/Role/透传）、`ApSettlementServiceTests`（★核销/尾差/勾稽）、`ApReversalTests`（红冲/付款撤销）、`ArAutoInvoiceTests`（出货开票/信用）

---

## 实施分三阶段

- **Phase A**（A-1..A-3）：章05 自动凭证引擎（先做，AP/AR 凭证都靠它）★
- **Phase B**（B-1..B-5）：章03 AP（★MVP）
- **Phase C**（C-1..C-3）：章04 AR（镜像 + 出货自动开票）

---

# Phase A — 自动凭证引擎（章05 ★枢纽）

## Task A-1: PostingRule + FinBizEvent + 迁移（章05 §2）

**Files:** Create `PostingRule.cs`, `PostingRuleLine.cs`, `FinBizEvent.cs`; Modify `CP6Context.cs`; migration

- [ ] **Step 1-3: 写实体**（照 05 §2：PostingRule[EventType/Name/IsActive/Lines]；PostingRuleLine[Side/Source/AccountRole/AmountField/CarryPartner/CarryCostCenter/FallbackAccountId/LineAccountField/LineAmountField]；PostingSide/RuleLineSource 枚举；FinBizEvent[EventType/Source/SourceDocNo/BizDate/PartnerId/CostCenterId/Description + DocLines + GetAmount(field)/GetGuid(field) 反射取值]）
- [ ] **Step 4-5: 迁移 + 提交** → `git commit -m "feat(fin): PostingRule + FinBizEvent (rule-as-data) (ch05 §2)"`

## Task A-2: AutoVoucherEngine 四步 + 幂等（章05 §3/§4）★★

**Files:** Create `IAutoVoucherEngine.cs`/`AutoVoucherEngine.cs`; Modify migration（`(Source,SourceDocNo)` 过滤唯一索引）; Test `AutoVoucherEngineTests.cs`

- [ ] **Step 1: 失败测试（★核心）**

```csharp
public class AutoVoucherEngineTests
{
    [Fact] public async Task Generate_FixedRole_ResolvesAccountByRole()
    {
        // 规则: 贷 AP_CONTROL 取头 GrossAmount → 引擎按 Role 找到 2202 科目，金额对
    }
    [Fact] public async Task Generate_DocumentLines_ExplodesPerAccount()
    {
        // 混行发票3行原材料+1行运费 → 炸成多条借方分录各进各 ExpenseAccountId（按科目+成本中心合并）
    }
    [Fact] public async Task Generate_Idempotent_SkipsDuplicateSourceDoc()
    {
        // 同 SourceDocNo 已过账 → 第二次跳过，不重复生成
        await eng.GenerateAsync(evt); var n1 = await CountPosted(evt.SourceDocNo);
        await eng.GenerateAsync(evt); Assert.Equal(n1, await CountPosted(evt.SourceDocNo));
    }
    [Fact] public async Task Generate_Unbalanced_RejectedByAutoPost()
    {
        // 规则配错致借贷不平 → AutoPostAsync 校验拦下，不落库（双保险）
    }
}
```

- [ ] **Step 2: 跑红 → Step 3: 实现**（照 05 §3 四步：①幂等[查 Source≠Manual ∧ SourceDocNo ∧ Posted 已存在则跳过]②找 PostingRule[EventType+IsActive]③拼凭证[FixedRole：ByRoleAsync 取科目+头字段金额+0额跳过+带 Partner/CostCenter；DocumentLines：GroupBy(科目,成本中心) 炸开单据行]④AutoPostAsync 直过[Plan 1，含恒等+锁期]）+ 迁移加 `(Source,SourceDocNo)` 过滤唯一索引[Source≠0 ∧ Status=2]
- [ ] **Step 4: 跑绿 + 提交** → `git commit -m "feat(fin): AutoVoucherEngine (idempotent/rule/role/passthrough → autopost) (ch05 §3/§4)"`

## Task A-3: FinBridgeHook 挂 Phase6 + 红冲补偿 + 规则 seed（章05 §5/§6）

**Files:** Create `IFinBridgeHook.cs`/`FinBridgeHook.cs`; Modify `IntegrationEventDispatcher.cs`(路由), `Program.cs`(DI); PostingRule seed; Test

- [ ] **Step 1: 失败测试**（FinBridgeHook.OnShipmentConfirmed→落 IntegrationEvent + 调引擎生成；OnShipmentCancelled→找凭证 ReverseAsync 红冲 autoPost）
- [ ] **Step 2: 跑红 → Step 3: 实现**（FinBridgeHook : BridgeHookBase 仿 IWmsBridgeHook：PersistEventAsync + GenerateAsync；Dispatcher 加 WMS|FIN|OnShipmentConfirmed 等路由；红冲补偿[按 SourceDocNo 找已过账凭证→ReverseAsync(autoPost:true) 系统红冲直过]；seed PostingRule：AP.InvoicePosted[DocumentLines 借 ExpenseAccount + FixedRole 借 TAX_INPUT + 贷 AP_CONTROL 带 Partner]、AP.Payment[借 AP_CONTROL 带 Partner/贷 Bank]、AR.Revenue[借 AR_CONTROL 带 Partner/贷 REVENUE/贷 TAX_OUTPUT]、AR.Cogs[借 COGS/贷 FG]）
- [ ] **Step 4: 跑绿 → Step 5: DI（FinBridgeHook + AutoVoucherEngine）+ 提交** → `git commit -m "feat(fin): FinBridgeHook on Phase6 + reversal compensation + rule seed (ch05 §5/§6)"`

---

# Phase B — 应付 AP（章03 ★MVP）

## Task B-1: AP 实体 + TaxCode + BankAccount + 防重 + 迁移（章03 §2）

**Files:** Create `ApInvoice.cs`/`ApInvoiceLine.cs`/`Payment.cs`/`BankAccount.cs`/`ApSettlement.cs`/`TaxCode.cs`; Modify `CP6Context.cs`; migration

- [ ] **Step 1-3: 写实体**（照 03 §2：ApInvoice[No/SupplierInvoiceNo/SupplierId/InvoiceDate/DueDate/币种/Net/Tax/Gross/SettledAmount/Status/PurchaseOrderId 可空/JournalEntryId/IsCreditMemo/OriginInvoiceId/RmaId/Lines]、ApInvoiceLine[ItemId/Qty/UnitPrice/Amount/TaxCodeId/TaxAmount/ExpenseAccountId/CostCenterId]、Payment[No/SupplierId/PayDate/Amount/Method/BankAccountId/IsPrepayment/SettledAmount/Status/JournalEntryId]、BankAccount[Code/Name/BankName/AccountNo/CurrencyCd/GlAccountId]、ApSettlement[PaymentId/ApInvoiceId/SettledAmount/DiffAmount/DiffType/DiffAccountId]、TaxCode[Code/Name/Rate/Direction/Recoverable]；**防重唯一索引 `(SupplierId,SupplierInvoiceNo)`**）
- [ ] **Step 4-5: 迁移 + 提交** → `git commit -m "feat(fin): AP entities + TaxCode/BankAccount + dup-guard index (ch03 §2)"`

## Task B-2: ApInvoiceService 录入+过账（发事件生成凭证，章03 §3①）

**Files:** Create `IApInvoiceService.cs`/`ApInvoiceService.cs`; Test

- [ ] **Step 1: 失败测试**（录入防重[同 SupplierInvoiceNo→拒]；过账→发 AP.InvoicePosted 事件→生成"借原材料+进项税/贷应付"凭证；不可抵扣税[Recoverable=false]→税并入成本行无独立税行）
- [ ] **Step 2: 跑红 → Step 3: 实现**（CreateAsync[防重 + 行级算税 + Gross=Net+Tax]；PostAsync[发 FinBizEvent(AP.InvoicePosted) → AutoVoucherEngine → 回填 JournalEntryId + Status]；税码 Recoverable 决定 TAX_INPUT 行是否生成[不可抵扣并入 ExpenseAccount 金额]）
- [ ] **Step 4: 跑绿 + 提交** → `git commit -m "feat(fin): AP invoice entry+post → voucher, recoverable tax (ch03 §3)"`

## Task B-3: PaymentService 付款+预付+撤销（章03 §3②/§6.1）

**Files:** Create `PaymentService.cs`; Test `ApReversalTests.cs`

- [ ] **Step 1: 失败测试**（付款过账→借应付/贷银行；预付[IsPrepayment]→借预付账款/贷银行 不冲应付；付款撤销→先解核销还原发票欠款 再红冲付款凭证[顺序]）
- [ ] **Step 2: 跑红 → Step 3: 实现**（PayAsync[发 AP.Payment 事件；IsPrepayment 时规则走预付账款]；ReversePaymentAsync[照 03 §6.1：①解 Settlements 还原 inv.SettledAmount(实付+差额) + Status ②ReverseAsync 付款凭证 autoPost ③Status=Reversed]）
- [ ] **Step 4: 跑绿 + 提交** → `git commit -m "feat(fin): AP payment + prepayment + payment reversal (order: unsettle→reverse) (ch03 §3/§6.1)"`

## Task B-4: ApSettlementService 核销多对多+尾差 + 子账勾稽（章03 §3③/§4）★

**Files:** Create `ApSettlementService.cs`, `ApReconcileService.cs`; Test `ApSettlementServiceTests.cs`

- [ ] **Step 1: 失败测试**（一付款核销多发票；超额拒；尾差/折扣[Diff]→发票清掉+差额写冲凭证[借应付/贷财务费用]+必须 DiffAccountId；**子账未付合计==GL AP_CONTROL 余额**[含尾差写冲后仍平]）

```csharp
[Fact] public async Task Settle_WithCashDiscount_WritesOffDiff_SubLedgerMatchesGl()
{
    // 付9998清10000发票,折扣2 → 发票Paid + 差额写冲凭证 + AP子账==GL应付余额
    var recon = await reconSvc.ReconcileApAsync(period);
    Assert.True(recon.IsMatched);
}
```

- [ ] **Step 2: 跑红 → Step 3: 实现**（SettleAsync[照 03 §3③：校验付款余额/供应商一致/发票超额/有差额须 DiffAccountId；建 ApSettlement；cleared=实付+差额 更新发票 SettledAmount/Status；含差额→发尾差写冲事件]；ReconcileApAsync[子账 Σ(Gross-Settled) vs GL ByRole(AP_CONTROL) 余额，IsMatched]）
- [ ] **Step 4: 跑绿 + 提交** → `git commit -m "feat(fin): AP settlement (M:N + write-off) + sub-ledger↔GL reconcile (ch03 §3/§4)"`

## Task B-5: 账龄 + 供应商红字 + 控制器/UI（章03 §5/§6.2）

**Files:** Create `ApAgingService.cs`; Modify `ApInvoiceService.cs`(红字); Controllers/UI

- [ ] **Step 1-3: 实现**——账龄分桶（未到期/1-30/31-60/60+，SignalR 逾期预警）；供应商红字（IsCreditMemo 负向发票，借应付/贷原材料+进项转出，接 WMS RMA 事件）；ApInvoiceController/PaymentController + AP 发票/付款/账龄 UI + 提交 → `git commit -m "feat(fin): AP aging + credit memo + controllers/UI (ch03 §5/§6.2)"`

---

# Phase C — 应收 AR（章04 镜像 + 出货自动开票）

## Task C-1: AR 实体（镜像 AP）+ 迁移（章04 §1）

**Files:** Create `ArInvoice.cs`/`ArInvoiceLine.cs`/`Receipt.cs`/`ArSettlement.cs`; Modify `CP6Context.cs`; migration

- [ ] **Step 1-3: 写实体**（镜像 AP：ArInvoice[CustomerId/ShipmentId/OrderId/IsCreditMemo... 照 04 §1]、ArInvoiceLine/Receipt[镜像 Payment 含 BankAccountId/撤销]/ArSettlement[镜像含尾差]；AR 用 AR_CONTROL/TAX_OUTPUT/REVENUE/COGS/FG Role）
- [ ] **Step 4-5: 迁移 + 提交** → `git commit -m "feat(fin): AR entities mirror AP (ch04 §1)"`

## Task C-2: 出货自动开票 + 双凭证（收入+成本结转，章04 §2）★

**Files:** Modify `ArInvoiceService.cs`(CreateFromShipment), `FinBridgeHook.cs`; Test `ArAutoInvoiceTests.cs`

- [ ] **Step 1: 失败测试**（出货确认事件→自动生成 AR 发票[幂等键 ShipmentId,同出货不开两次]→收入确认凭证[借应收/贷收入+销项税]+成本结转凭证[借COGS/贷FG,成本用估算]；出货取消→两凭证红冲+发票作废）

```csharp
[Fact] public async Task ShipmentConfirmed_AutoCreatesInvoice_AndTwoVouchers()
{
    await finHook.OnShipmentConfirmedAsync(shipEvt);
    Assert.Single(db.ArInvoices.Where(i => i.ShipmentId == shipEvt.ShipmentId));
    // 收入凭证 + 成本结转凭证各一
    await finHook.OnShipmentConfirmedAsync(shipEvt);  // 重放
    Assert.Single(db.ArInvoices.Where(i => i.ShipmentId == shipEvt.ShipmentId));  // 幂等不重复
}
```

- [ ] **Step 2: 跑红 → Step 3: 实现**（CreateFromShipmentAsync[幂等键 ShipmentId 防重开票，吃 Order/Outbound 数据]；FinBridgeHook.OnShipmentConfirmed[①开票②AR.Revenue 事件③AR.Cogs 事件 成本用估算(F2-D2)]；OnShipmentCancelled[两凭证红冲+发票 Reversed]）
- [ ] **Step 4: 跑绿 + 提交** → `git commit -m "feat(fin): AR auto-invoice from shipment + revenue/cogs vouchers (ch04 §2)"`

## Task C-3: 信用控制 + 收款核销 + 销售红字(接CreditNote) + 控制器/UI（章04 §3/§4）

**Files:** Create `CreditControlService.cs`, `ReceiptService.cs`, `ArSettlementService.cs`; Modify `BusinessPartner`(+CreditLimit); Controllers/UI

- [ ] **Step 1: 失败测试**（信用控制：已欠+本单>CreditLimit→拦；收款核销镜像 AP；销售退货红字接 CreditNote→AR 红字发票+冲收入/成本）
- [ ] **Step 2: 跑红 → Step 3: 实现**（CheckCreditAsync[BusinessPartner.CreditLimit + openAr 校验，出货前钩子]；ReceiptService/ArSettlementService 镜像 AP；销售红字接现有 CreditNote 生成 AR 红字 + 红冲）
- [ ] **Step 4: 跑绿 → Step 5: AR 控制器 + UI（发票/收款/账龄）+ PostingRuleController/UI + DI 全装配 + 提交** → `git commit -m "feat(fin): credit control + AR receipt/settle + sales credit note (ch04 §3/§4)"`

---

## Self-Review（对照章03/04/05 覆盖）

- **章05**：PostingRule 规则即数据(A-1) ✅ / Role 锚点取科目(A-2) ✅ / DocumentLines 透传(A-2) ✅ / 幂等(A-2) ✅ / AutoPost 直过双保险(A-2) ✅ / FinBridgeHook 挂 Phase6(A-3) ✅ / 红冲补偿跟随触发源(A-3) ✅ / 规则 seed(A-3) ✅
- **章03**：三段式生命周期(B-2/B-3/B-4) ✅ / 发票防重(B-1) ✅ / 三段凭证(B-2/B-3) ✅ / 可抵扣税(B-2) ✅ / 核销多对多+尾差(B-4) ✅ / 预付款(B-3) ✅ / 子账↔GL勾稽(B-4) ✅ / 账龄(B-5) ✅ / 红冲+付款撤销(B-3) ✅ / 供应商红字接RMA(B-5) ✅
- **章04**：AR 镜像 AP(C-1) ✅ / 出货自动开票+双凭证(C-2) ✅ / 幂等 ShipmentId(C-2) ✅ / 信用控制(C-3) ✅ / 销售退货红字接 CreditNote(C-3) ✅ / 成本估算起步(C-2,F2-D2) ✅ / AR↔GL勾稽(C-3镜像) ✅

**已知缺口/推迟（已标注）：**
1. **外币 AP/AR + 汇差**（F2-D1）—— Finance Plan 3 章07，本计划本位币。
2. **AR 成本结转真实成本**（F2-D2）—— Plan 3 章06 落地后切换，本计划估算。
3. **采购三单匹配**（F2-D3）—— 采购模块计划，PurchaseOrderId 预留。
4. **TenantId**（F2-D5）—— OA 章10 统一。
5. **PUB 权限点**（F2-D5）—— PUB B1 落地后接。

**Type 一致性：** `AutoVoucherEngine.GenerateAsync(FinBizEvent)`(A-2) 被 AP(B-2/B-3)/AR(C-2) 发事件调用；`PostingRule` Role 锚点(A-1) 对应 Plan 1 `GlAccount.Role`；`AutoPostAsync`/`ReverseAsync`(Plan 1) 被引擎(A-2)/补偿(A-3)/撤销(B-3) 用；`ApSettlement`/`ArSettlement` 镜像结构(B-1/C-1)；`FinBridgeHook`(A-3) 复用 Phase6 `BridgeHookBase`。

---

## 执行交接

计划存 `docs/superpowers/plans/2026-06-13-fin-ap-ar-voucher.md`。**财务第二份（AP MVP + AR + 引擎）**。后续：
- Finance Plan 3 = `2026-06-13-fin-cost-statements.md`（章06 成本会计 + 07 多币种 + 08 报表 + 09 集成 + 10 完整性审计）

**下一步按工作流是你修订**（拍板 F2-D1~D6）。定稿后执行：总账地基 → **本计划(引擎→AP→AR)** → 成本/报表；阶段2 AP 即 MVP 价值点（先于 AR/成本可演示）。

---

*初稿生成于 2026-06-13。源：docs/finance/03·04·05（文档含可落码代码）。已勘察：Phase6 BridgeHookBase/IntegrationEvent/Dispatcher 复用、BusinessPartner/Order/Outbound/CreditNote/IErpBridgeHook 现成、Plan1 AutoPostAsync/ReverseAsync/Role/FiscalPeriod 前置、零多租户。*
