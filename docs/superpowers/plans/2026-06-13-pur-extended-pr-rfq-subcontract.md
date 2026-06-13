# 采购 扩展 · PR+RFQ+外注+集成+完整性（章05~09）Implementation Plan（初稿）

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

> **工作流（丛书模式）**：我出初稿 → 你修订 → 我评审合并定稿后再编码。**采购第二份（收官）计划**。依赖采购 Plan 1（MVP：SupplierPrice/PurchaseOrder/GR/三单匹配/三委托契约）已落地。

**Goal:** 补全采购完整型（阶段3-5）——章05 采购申请 PR（需求驱动：手工/缺料反流复用 Phase9/工单 + 审批 + PR→PO）+ 章06 RFQ 询价比价（邀报价→按行比价排名→选定→回写采购价表→转 PO）+ 章07 外注加工（外注 PO Type=2 + 支給材发料追踪 IssuedQty + 成品成本=加工费+料 + 只匹加工费）+ 章08 集成（四同步接口落地）+ 章09 完整性（防虚开/吞料/重复收货 + 采购对账）。

**Architecture:** 落 `Pur` 命名空间，复用 Plan 1 的 PO/GR/三单匹配/价表。PR 是采购需求入口（需求与订单分离，便于归集议价+审批前置+追溯），三来源 manual/shortage(复用 Phase9 `MaterialShortage`)/workorder。RFQ 是价格发现：`RfqQuote` 是 `(供应商×行)` 矩阵，按行排名次(价格优先,Rank 建议非自动选)，选定后回写 `SupplierPrice(Source=rfq)` 闭环。外注复用 `PurchaseOrder(Type=2)` + `PoConsignMaterial` 子表：支給材发出是**资产位移非消耗**(IssuedQty 防吞料)、成品成本=加工费(PO单价)+支給材成本(并入,接财务06)、只匹加工费。审批/WMS/财务全走同步接口委托(Plan 1 契约,桩起步)。

**Tech Stack:** .NET 8 + EF Core 8 / xUnit + EF Core InMemory / Vue 3.5 + element-plus。源文档：`docs/procurement/05·06·07·08·09`。

---

## 关键前置决策（待你修订时确认）

| # | 议题 | 现状/对账 | **本稿建议值** |
|---|---|---|---|
| **P2-D1** | **缺料/工单驱动 PR** | 05 §3：复用 Phase9 `MaterialShortage`；工单 BOM 缺料 | `MaterialShortage`(Phase9)现成→接 `GenerateFromShortageAsync`(Handled 防重)；工单 BOM 缺料驱动需确认 MES 工单 BOM 展开缺料数据是否可取，无则工单驱动 PR 后补 |
| **P2-D2** | **外注成品成本接财务** | 07 §5：成品成本接财务 06 成本会计 | 外注 `CalcFinishedCostAsync` 调财务 `IFinCostService.PostSubcontractCost`(财务 Plan 3 章06 实现)；P1 桩，真实对接财务成本落地后 |
| **P2-D3** | **IWmsIssueService** | 07 §4：支給材出库委托 WMS | 新建 `IWmsIssueService` 契约(Purpose=subcontract 区分非销售/消耗)+桩(WMS 实现)；与 Plan 1 的 IWmsReceive/QcQuery 同族 |
| **P2-D4** | **审批接 OA** | 05/02：PR/PO 走 IApprovalService | 复用 Plan 1 的 IApprovalService 桩；OA 落地后接真实(BizType=PR/PO) |
| **P2-D5** | **TenantId/权限** | 同其他模块 | TenantId 延 OA 章10；操作权限接 PUB B1(落地后) |

> **测试基建**：xUnit + InMemory。缺料反流防重、比价排名+回写价表、外注支給材 IssuedQty 对账、成品成本并料、采购对账可纯单测。委托接口桩注入。

---

## File Structure

### 章05 PR（`Pur`）
- `PurchaseRequest.cs`/`PurchaseRequestLine.cs`；`IPurchaseRequestService.cs`/`PurchaseRequestService.cs`（PR CRUD + 审批 + 转 PO）、`PrGenerationService.cs`（缺料/工单驱动）

### 章06 RFQ（`Pur`）
- `Rfq.cs`/`RfqLine.cs`/`RfqSupplier.cs`/`RfqQuote.cs`；`IRfqService.cs`/`RfqService.cs`（建/邀/收报价/比价 Rank/选定/回写价表/转 PO）

### 章07 外注（`Pur`）
- `PoConsignMaterial.cs`；`ISubcontractService.cs`/`SubcontractService.cs`（发料 IssuedQty/成品成本/防吞料对账）
- `Contracts/IWmsIssueService.cs`+桩；`Contracts/IFinCostService.cs`+桩（外注成品成本）

### 章08+09 集成与完整性
- 四同步接口落地说明（IWmsReceive/QcQuery/Issue + IFinAp，Plan1+本计划）
- `PurReconcileService.cs`（采购对账：防虚开/吞料/重复收货）

### 控制器 + DI + 迁移 + 前端 + 测试
- `Controllers/Pur/{PurchaseRequestController,RfqController,SubcontractController}.cs`
- 迁移 `*_PurExtended`；`cp6.web/src/views/pur/{PrView,RfqView,SubcontractView}.vue`
- 测试：`PrGenerationServiceTests`（缺料防重）、`RfqServiceTests`（比价/回写价表）、`SubcontractServiceTests`（IssuedQty/成品成本/对账）

---

## 实施分四阶段

- **Phase A**（A-1..A-2）：章05 PR + 需求驱动
- **Phase B**（B-1..B-3）：章06 RFQ 询价比价
- **Phase C**（C-1..C-3）：章07 外注加工
- **Phase D**（D-1）：章08+09 集成与完整性对账（收口）

---

# Phase A — 采购申请 PR（章05）

## Task A-1: PR 实体 + 缺料/工单驱动生成（章05 §2/§3）

**Files:** Create `PurchaseRequest.cs`/`PurchaseRequestLine.cs`, `PrGenerationService.cs`; Modify `CP6Context.cs`; migration; Test `PrGenerationServiceTests.cs`

- [ ] **Step 1: 失败测试**（缺料反流：MaterialShortage→生成 PR(Source=shortage,SourceRefNo)，Handled 标记防重复生成；估价 EstPrice 取价表/历史；建议供应商）
- [ ] **Step 2: 跑红 → Step 3: 实现**（实体照 05 §2：PurchaseRequest[PrNo/RequesterId/DeptId/Status/Source manual·shortage·workorder/SourceRefNo/ApprovalRef]、PurchaseRequestLine[ItemId/Qty/RequiredDate/EstPrice/SuggestSupplierId/ConvertedPoNo]；PrGenerationService.GenerateFromShortageAsync[复用 Phase9 MaterialShortage，Handled 防重，ResolvePrice 估价，SuggestSupplier 按历史]；工单驱动 GenerateFromWorkOrderAsync[P2-D1 确认 MES BOM 缺料]）
- [ ] **Step 4: 跑绿 → Step 5: 迁移 + 提交** → `git commit -m "feat(pur): PurchaseRequest + demand-driven generation (shortage/workorder) (ch05 §2/§3)"`

## Task A-2: PR 审批 + PR→PO 转换（章05 §4/§5）

**Files:** Create `IPurchaseRequestService.cs`/`PurchaseRequestService.cs`, Controller/UI; Test

- [ ] **Step 1: 失败测试**（PR 审批走 IApprovalService 桩→已批；PR→PO 按 SuggestSupplierId 分组拆多 PO[一PR多供应商]/合单[多PR同供应商]；转后回填 ConvertedPoNo + 状态推进）
- [ ] **Step 2: 跑红 → Step 3: 实现**（SubmitForApproval[IApprovalService(桩) BizType=PR]；ConvertToPoAsync[照 05 §5：按 SuggestSupplierId GroupBy → 复用 Plan 1 PurchaseOrderService.CreateAsync → 回填 ConvertedPoNo；未定供应商行走 RFQ]）
- [ ] **Step 4: 跑绿 → Step 5: PurchaseRequestController + PR UI + 提交** → `git commit -m "feat(pur): PR approval + PR→PO conversion (by supplier) (ch05 §4/§5)"`

---

# Phase B — RFQ 询价比价（章06）

## Task B-1: RFQ 四实体 + 从 PR 发起 + 邀请收报价（章06 §2/§3）

**Files:** Create `Rfq.cs`/`RfqLine.cs`/`RfqSupplier.cs`/`RfqQuote.cs`, `IRfqService.cs`/`RfqService.cs`; Modify `CP6Context.cs`; migration; Test

- [ ] **Step 1-3: 写实体 + 建/邀/收报价**（照 06 §2：Rfq[RfqNo/Date/DueDate/Status/Buyer/SourcePrNo]、RfqLine[ItemId/Qty/RequiredDate/SourcePrNo/SourcePrLineNo 行级追溯]、RfqSupplier[SupplierId 复用 BP 发注先/InviteStatus]、RfqQuote[SupplierId/LineNo/QuotedPrice/CurrencyCd/LeadDays/ValidUntil/IsSelected/Rank]；CreateFromPrAsync[未定供应商的 PR 行汇成 RFQ，SourcePr 追溯]；邀请发出 + 收报价录入）
- [ ] **Step 4-5: 迁移 + 提交** → `git commit -m "feat(pur): RFQ entities + create-from-PR + invite/quote (ch06 §2/§3)"`

## Task B-2: 比价排名 + 选定（章06 §4）★

**Files:** Modify `RfqService.cs`; Test `RfqServiceTests.cs`

- [ ] **Step 1: 失败测试**（按行分组比价 Rank：剔除过期报价[ValidUntil]→价格优先→同价比交期；选定 IsSelected 可按行拆不同供应商；选过期报价→拒）

```csharp
[Fact] public void Rank_PerLine_ExcludesExpired_PriceFirst()
{
    // 行1: A报10/B报8(过期)/C报9 → 剔除B, Rank: C(9)=1, A(10)=2
}
[Fact] public async Task Select_ExpiredQuote_Throws() { /* 选过期报价→异常 */ }
```

- [ ] **Step 2: 跑红 → Step 3: 实现**（RankQuotesAsync[照 06 §4：按 LineNo 分组，剔除 ValidUntil 过期，OrderBy QuotedPrice ThenBy LeadDays，赋 Rank]；SelectAsync[按行选 IsSelected，校验未过期；Rank 是建议、人拍板]）
- [ ] **Step 4: 跑绿 + 提交** → `git commit -m "feat(pur): RFQ per-line ranking + selection (rank advisory, human decides) (ch06 §4)"`

## Task B-3: 回写采购价表 + RFQ→PO（章06 §5/§6）

**Files:** Modify `RfqService.cs`; Controller/UI; Test

- [ ] **Step 1: 失败测试**（选中报价回写 SupplierPrice(Source=rfq)；RFQ→PO 按选中供应商分组,价用询价成交价非价表取价）
- [ ] **Step 2: 跑红 → Step 3: 实现**（WriteBackPricesAsync[照 06 §5：选中报价 Upsert SupplierPrice，Source=rfq，复用 Plan 1 价表服务]；ConvertToPoAsync[按 SupplierId 分组，BuildPoDtoFromRfq 用 QuotedPrice 填 PO 行价，复用 Plan 1 CreateAsync]）
- [ ] **Step 4: 跑绿 → Step 5: RfqController + RFQ/比价 UI + 提交** → `git commit -m "feat(pur): write-back to price list (Source=rfq) + RFQ→PO (ch06 §5/§6)"`

---

# Phase C — 外注加工（章07）

## Task C-1: PoConsignMaterial + IWmsIssueService 契约 + 发料追踪（章07 §2/§4）★

**Files:** Create `PoConsignMaterial.cs`, `Contracts/IWmsIssueService.cs`+桩, `ISubcontractService.cs`/`SubcontractService.cs`; Modify `CP6Context.cs`; migration; Test

- [ ] **Step 1: 失败测试**（发支給材→调 IWmsIssueService(Purpose=subcontract)出库→IssuedQty 累加+WmsIssueNo；分批发料累加；支給材出库不算消耗/不算卖[Purpose 标记]）
- [ ] **Step 2: 跑红 → Step 3: 实现**（PoConsignMaterial[PoNo/LineNo/ConsignItemId/ConsignQty 应发/ConsignUnitCost 内部成本/IssuedQty 已发锚/WmsIssueNo]；IWmsIssueService 契约[IssueAsync,Purpose=subcontract]+桩；IssueConsignAsync[照 07 §4：调 WMS 出库，IssuedQty 累加，Purpose 区分非销售/消耗]；外注 PO 复用 Plan 1 PurchaseOrder Type=2，PO 行 UnitPrice=加工费）
- [ ] **Step 4: 跑绿 → Step 5: 迁移 + DI（桩）+ 提交** → `git commit -m "feat(pur): subcontract consign material + WMS issue (asset relocation, IssuedQty) (ch07 §2/§4)"`

## Task C-2: 收成品成本核算（加工费+支給材成本）+ 只匹加工费（章07 §5/§7）

**Files:** Modify `SubcontractService.cs`, `Contracts/IFinCostService.cs`+桩; Test

- [ ] **Step 1: 失败测试**（成品成本=加工费(PO单价×成品数)+支給材成本(ConsignQty×ConsignUnitCost)；支給材成本"并入"非"另付"；三单匹配只匹加工费[复用 Plan 1 match，支給材不进]）
- [ ] **Step 2: 跑红 → Step 3: 实现**（CalcFinishedCostAsync[照 07 §5：processingFee + consignCost → 调 IFinCostService.PostSubcontractCost 接财务06(P2-D2 桩)]；收成品复用 Plan 1 IWmsReceiveService+GR；加工费走 Plan 1 三单匹配建 AP[支給材不进匹配]）
- [ ] **Step 4: 跑绿 + 提交** → `git commit -m "feat(pur): finished cost (processing+consign) + match processing fee only (ch07 §5/§7)"`

## Task C-3: 防吞料对账 + 外注 UI（章07 §6）

**Files:** Modify `SubcontractService.cs`(对账); Controller/UI; Test

- [ ] **Step 1: 失败测试**（应耗=成品数×BOM单耗 vs 实发 IssuedQty，差异超损耗容差→异常挂起核查）
- [ ] **Step 2: 跑红 → Step 3: 实现**（ReconcileConsignAsync[照 07 §6：实发 IssuedQty vs 成品反推应耗，超容差异常]）
- [ ] **Step 4: 跑绿 → Step 5: SubcontractController + 外注 UI + 提交** → `git commit -m "feat(pur): consign material reconciliation (anti-pilferage) (ch07 §6)"`

---

# Phase D — 集成与完整性（章08+09 收口）

## Task D-1: 四接口落地说明 + 采购对账（防虚开/吞料/重复收货，章08+09）

**Files:** Create `PurReconcileService.cs`; Modify DI（四契约切真实/桩）; Controller/UI; Test `PurReconcileServiceTests.cs`

- [ ] **Step 1: 失败测试**（采购对账：①无 PO/无验收合格货 建不出 AP[防虚开,Plan1 match 已挡]②InvoicedQty 不超 AcceptedQty[防重复开票]③ReceivedQty 不超 Qty+容差[防超收/重复收货]④外注 IssuedQty 对账[防吞料]）
- [ ] **Step 2: 跑红 → Step 3: 实现**（PurReconcileService[汇总四类完整性校验，复用 Plan 1 match 闸门 + C-3 外注对账，issue→告警]；四同步接口落地：IWmsReceive/QcQuery/Issue(WMS 实现) + IFinAp(财务实现) + IApproval(OA 实现)，DI 按配置切真实/桩，跨模块同步直线不走 Phase6 事件[Phase6 仅模块内]）
- [ ] **Step 4: 跑绿 → Step 5: DI 全装配 + 全量构建全测 + 提交** → `git commit -m "feat(pur): integration contracts wiring + procurement reconciliation (anti-fraud) (ch08/09)"`

---

## Self-Review（对照章05~09 覆盖）

- **章05**：PR 需求入口(A-1) ✅ / 三来源 manual·缺料 Phase9·工单(A-1,P2-D1) ✅ / 缺料防重 Handled(A-1) ✅ / PR 审批可插拔(A-2) ✅ / PR→PO 按供应商拆/合单(A-2) ✅ / EstPrice 估价(A-1) ✅
- **章06**：RFQ 四实体(B-1) ✅ / 从 PR 发起+行级追溯(B-1) ✅ / (供应商×行)报价矩阵(B-1) ✅ / 按行比价 Rank+剔除过期(B-2) ✅ / 选定按行拆(B-2) ✅ / Rank 建议非自动(B-2) ✅ / 回写价表 Source=rfq(B-3) ✅ / RFQ→PO 用成交价(B-3) ✅ / ValidUntil 两道校验(B-2/B-3) ✅
- **章07**：外注 PO Type=2 同表+子表(C-1) ✅ / 支給材资产位移非消耗 Purpose(C-1) ✅ / IssuedQty 追踪(C-1) ✅ / 成品成本=加工费+料(C-2) ✅ / 支給材并入非另付(C-2) ✅ / 只匹加工费(C-2) ✅ / 防吞料对账(C-3) ✅
- **章08+09**：四同步接口落地(D-1) ✅ / 防虚开/重复开票/超收/吞料对账(D-1) ✅ / 跨模块同步非 Phase6(D-1) ✅ / 人工放行留痕(Plan1 已有)

**已知缺口/推迟（已标注）：**
1. **工单 BOM 缺料驱动 PR**（P2-D1）—— 需确认 MES 工单 BOM 缺料数据，无则后补；缺料反流(Phase9)现成。
2. **外注成品成本接财务/三委托真实对接**（P2-D2/D3）—— 财务 Plan 3 章06 / WMS / OA 落地后接，本计划桩。
3. **TenantId/PUB 权限**（P2-D5）—— OA 章10 / PUB B1 统一。

**Type 一致性：** PR→PO/RFQ→PO 复用 Plan 1 `PurchaseOrderService.CreateAsync`(A-2/B-3)；回写价表复用 Plan 1 `SupplierPriceService`(B-3)；外注复用 Plan 1 `PurchaseOrder(Type=2)`/`IWmsReceiveService`/三单匹配(C-1/C-2)；`MaterialShortage`(Phase9)接 PR(A-1)；`IWmsIssueService`/`IFinCostService`(C-1/C-2) 桩起步；采购对账复用 Plan 1 match 闸门(D-1)。

---

## 执行交接

计划存 `docs/superpowers/plans/2026-06-13-pur-extended-pr-rfq-subcontract.md`。**采购第二份（收官）**。至此 **采购两份计划全齐**：
1. `2026-06-13-pur-mvp-po-gr-match.md`（MVP：主数据+PO+GR+三单匹配，补全财务 AP 前置）
2. `2026-06-13-pur-extended-pr-rfq-subcontract.md`（扩展：PR+RFQ+外注+集成+完整性）← 本文

两份覆盖采购全章 00~09。**下一步按工作流是你修订**（拍板 P2-D1~D5）。定稿后执行：MVP(主数据→PO→GR→匹配) → 扩展(PR→RFQ→外注→对账)；MVP 即补全财务 AP 前置的价值点。

---

*初稿生成于 2026-06-13。源：docs/procurement/05·06·07·08·09。已勘察：MaterialShortage(Phase9 缺料现成)、BusinessPartner 发注先(询价对象)、Plan1 PurchaseOrder/SupplierPrice/三单匹配/三委托契约前置、财务 ApInvoice.PurchaseOrderId/成本会计(财务Plan2/3)、WMS 出库(IWmsIssueService 待实现)、零多租户、Pur 命名空间全新建。*
