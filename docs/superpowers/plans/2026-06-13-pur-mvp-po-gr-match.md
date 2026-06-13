# 采购 MVP · 主数据+PO+收货+三单匹配（章01~04）Implementation Plan（初稿）

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

> **工作流（丛书模式）**：我出初稿 → 你修订 → 我评审合并定稿后再编码。**采购第一份计划**（共两份）。MVP=阶段0-2，补全财务 AP 前置。

**Goal:** 落地采购 MVP（阶段0-2）——章01 主数据（复用 `BusinessPartner` 发注先 + 新建采购价表 `SupplierPrice` 阶梯价）+ 章02 采购订单 PO（三累计锚 + 派生状态机 + 冻结汇率）+ 章03 收货 GR（委托 WMS 物理入库 + 双基准）+ 章04 **三单匹配（★MVP 核心：容差匹配 → 同步建应付发票，填 `ApInvoice.PurchaseOrderId`）**。完成后："PO→收货→匹配→自动建应付发票"跑通，财务 AP 从"手工录票"升级为"三单匹配自动建票"。

**Architecture:** 落 `Pur` 命名空间（`CP6.Entity/DomainModels/Pur`、`CP6.Core/Services/Pur`、`Controllers/Pur`、`views/pur`）。**模块自洽、同步接口委托、依赖单向、不双写**——采购拥有全部单据/逻辑（PR/PO/GR/匹配），但物理库存同步委托 WMS（`IWmsReceiveService`/`IWmsQcQuery`，库存唯一真相在 WMS）、应付同步委托财务（`IFinApService`，应付唯一真相在财务）、审批委托 OA（`IApprovalService`，桩）。三单匹配的锚 = `PurchaseOrderLine` 的 `ReceivedQty`/`AcceptedQty`/`InvoicedQty` 三累计量；PO 状态是这三个数的**派生投影**（非手工输入）。匹配比 `AcceptedQty−InvoicedQty`（验收合格未开票量）+ 价差，容差内自动建 AP。冻结汇率贯穿 PO→GR→AP。

**Tech Stack:** .NET 8 + EF Core 8 / xUnit + EF Core InMemory / Vue 3.5 + element-plus。源文档：`docs/procurement/01·02·03·04`（引用财务 03 AP、WMS、OA 05）。

---

## 关键前置决策（待你修订时确认）

| # | 议题 | 现状/对账 | **本稿建议值** |
|---|---|---|---|
| **P-D1** | **三个委托接口的对端** | WMS/财务/OA 接口由各模块实现 | 采购**只立契约 + P1 桩**：`IWmsReceiveService`/`IWmsQcQuery`（WMS 实现，桩返回入库号+合格）、`IFinApService`（财务实现，桩建票，财务 Plan 2 有真实 ApInvoice）、`IApprovalService`（OA 实现，桩单人/跳过）。采购编译期只依赖抽象，可独立交付 + 测试。真实对接属各模块/集成（采购 Plan 2 章08 + 各模块改造） |
| **P-D2** | **BusinessPartner 发注先字段** | 文档列 SupplierFlg/SupplierPattern/PurchasePostingDiv/PurchaseTaxCd/CurrencyCd/外注字段 | ⚠️ 需确认 `BusinessPartner` 实际是否已有这些发注先字段（文档称"已存在"）。若缺，需先补字段（落主数据，非采购）。建 PO 只读引用 |
| **P-D3** | **采番** | 文档 `_seq.NextAsync("PO")` | 用既有 `DocNumber.NextAsync(db,"PO")`/`MesSequence` 范式生成 PO/GR 号；或建 PurSequence。建议复用 DocNumber |
| **P-D4** | **冻结汇率来源** | 02：FreezeRateAsync 财务同源 | 复用 `FxRate`（Gap 4.3 现成）冻结 PO 汇率，一路传 GR→AP（与财务 07 同源） |
| **P-D5** | **TenantId/审计/权限** | 同其他模块 | TenantId 延 OA 章10；审计字段以代码为准；PR/PO 审批接 OA（桩起步）；操作权限接 PUB B1（落地后） |

> **测试基建**：xUnit + InMemory。PO 派生状态机、双基准 Accepted 累加、三单匹配容差+接 AP、阶梯价带出可纯单测（doc 已给代码）。委托接口用桩注入测。

---

## File Structure

### 章01 主数据
- `CP6.Entity/DomainModels/Pur/SupplierPrice.cs`；`ISupplierPriceService.cs`/`SupplierPriceService.cs`（阶梯价带出）
- （`BusinessPartner` 发注先 Tab 只读复用，不新建供应商表）

### 章02 PO
- `PurchaseOrder.cs`/`PurchaseOrderLine.cs`（三累计锚）；`IPurchaseOrderService.cs`/`PurchaseOrderService.cs`（建单带出 + DeriveStatus + 审批）
- `Contracts/IApprovalService.cs`（采购侧契约，OA 实现）+ `StubApprovalService.cs`

### 章03 GR
- `GoodsReceipt.cs`/`GoodsReceiptLine.cs`；`IGoodsReceiptService.cs`/`GoodsReceiptService.cs`（双基准 + 回写锚）
- `Contracts/IWmsReceiveService.cs`/`IWmsQcQuery.cs` + 桩（WMS 实现）

### 章04 三单匹配
- `ThreeWayMatch.cs`/`MatchTolerance.cs`；`IThreeWayMatchService.cs`/`ThreeWayMatchService.cs`（容差匹配 + 接 AP）
- `Contracts/IFinApService.cs` + 桩（财务实现）

### 控制器 + DI + 迁移 + 前端 + 测试
- `Controllers/Pur/{SupplierPriceController,PurchaseOrderController,GoodsReceiptController,ThreeWayMatchController}.cs`
- 迁移 `*_PurMvp`；`cp6.web/src/views/pur/{SupplierPriceView,PurchaseOrderView,GoodsReceiptView,MatchView}.vue`
- 测试：`PurchaseOrderServiceTests`（派生状态机/带出）、`GoodsReceiptServiceTests`（双基准）、`ThreeWayMatchServiceTests`（★容差/接AP/防重开）、`SupplierPriceServiceTests`（阶梯价）

---

## 实施分四阶段

- **Phase A**（A-1）：章01 主数据（采购价表 + 复用供应商）
- **Phase B**（B-1..B-2）：章02 PO（三累计锚 + 派生状态机 + 审批桩）
- **Phase C**（C-1..C-2）：章03 GR（WMS 委托 + 双基准）
- **Phase D**（D-1..D-2）：章04 三单匹配（★MVP → 接 AP）

---

# Phase A — 主数据（章01）

## Task A-1: SupplierPrice 阶梯价表 + 带出服务（章01 §3/§4）

**Files:** Create `SupplierPrice.cs`, `ISupplierPriceService.cs`/`SupplierPriceService.cs`; Modify `CP6Context.cs`; migration; Test `SupplierPriceServiceTests.cs`

> ⚠️ 先确认（P-D2）`BusinessPartner` 发注先字段齐备（SupplierFlg/SupplierPattern/PurchasePostingDiv/PurchaseTaxCd/CurrencyCd）；缺则先补（主数据）。

- [ ] **Step 1: 失败测试**（阶梯价：买 100 取 100 档、买 1000 取 1000 档[满足 MinQty 的最高阶梯]；只取当时有效[ValidFrom/To]；无价返回 null）

```csharp
[Fact] public async Task ResolvePrice_PicksHighestQualifyingTier()
{
    db.SupplierPrices.AddRange(
        new(){SupplierId=s,ItemId=i,Price=10,MinQty=1,ValidFrom=d0},
        new(){SupplierId=s,ItemId=i,Price=8,MinQty=1000,ValidFrom=d0});
    await db.SaveChangesAsync();
    Assert.Equal(8, await svc.ResolvePriceAsync(s,i,qty:1500,onDate:now));  // 够1000档→8
    Assert.Equal(10, await svc.ResolvePriceAsync(s,i,qty:500,onDate:now));  // 不够→10
}
```

- [ ] **Step 2: 跑红 → Step 3: 实现**（SupplierPrice[SupplierId/ItemId/Price/CurrencyCd/MinQty/ValidFrom/ValidTo/Source]；ResolvePriceAsync[照 01 §4：满足 MinQty≤qty ∧ 当时有效，取 MinQty 最大]）
- [ ] **Step 4: 跑绿 → Step 5: 迁移 + SupplierPriceController/UI + 提交** → `git commit -m "feat(pur): SupplierPrice tiered pricing + resolve (ch01 §3/§4)"`

---

# Phase B — 采购订单 PO（章02）

## Task B-1: PO 实体 + 三累计锚 + 建单带出 + 迁移（章02 §2/§3）

**Files:** Create `PurchaseOrder.cs`/`PurchaseOrderLine.cs`, `IPurchaseOrderService.cs`/`PurchaseOrderService.cs`, `Contracts/IApprovalService.cs`+桩; Modify `CP6Context.cs`; migration; Test

- [ ] **Step 1: 失败测试**（建 PO：供应商须 SupplierFlg；价/税码/币种/PostingBasis 从主数据带出可覆盖；算 Net/Tax/Gross；冻结汇率；三累计锚初始 0）
- [ ] **Step 2: 跑红 → Step 3: 实现**（实体照 02 §2：PurchaseOrder[PoNo/SupplierId/Type/CurrencyCd/FxRate 冻结/PostingBasis/Status/Net/Tax/Gross/SourceRfqNo/ApprovalRef]、PurchaseOrderLine[ItemId/Qty/UnitPrice/TaxCodeId/RequiredDate/**ReceivedQty/AcceptedQty/InvoicedQty 三锚**/MatchStatus/Status]；CreateAsync[照 02 §3：校验 SupplierFlg、ResolvePrice 带价、供应商税码/币种/PostingBasis 带出、FreezeRate、CalcAmounts]；IApprovalService 契约 + StubApprovalService[单人/跳过]）
- [ ] **Step 4: 跑绿 → Step 5: 迁移 + 提交** → `git commit -m "feat(pur): PurchaseOrder + 3 accumulator anchors + create-with-defaults (ch02 §2/§3)"`

## Task B-2: PO 派生状态机 + 审批接入（章02 §4/§7）

**Files:** Modify `PurchaseOrderService.cs`(DeriveStatus); Controller; Test

- [ ] **Step 1: 失败测试**（DeriveStatus 派生：anyReceived→部分收货、allReceived→收齐、anyInvoiced→部分开票、allInvoiced+matched→关闭；取消仅草稿/确认可；提交→IApprovalService.Submit→通过 OnApproved 置确认）
- [ ] **Step 2: 跑红 → Step 3: 实现**（DeriveStatus[照 02 §4：从三累计量派生状态，非手工]；SubmitForApprovalAsync[调 IApprovalService(桩)，回调置确认 + ApprovalRef]；取消校验）
- [ ] **Step 4: 跑绿 → Step 5: PurchaseOrderController + PO UI + 提交** → `git commit -m "feat(pur): PO derived state machine + approval stub (ch02 §4/§7)"`

---

# Phase C — 收货 GR（章03）

## Task C-1: WMS 委托契约 + 桩 + GR 实体（章03 §2/§3）

**Files:** Create `Contracts/IWmsReceiveService.cs`/`IWmsQcQuery.cs`+桩, `GoodsReceipt.cs`/`GoodsReceiptLine.cs`; Modify `CP6Context.cs`; migration

- [ ] **Step 1-3: 写契约 + 桩 + 实体**（IWmsReceiveService.ReceiveAsync[返回 WmsInboundNo+明细引用]、IWmsQcQuery.QueryByReceiptAsync[合格/不良/待检]；P1 桩[StubWmsReceive 返回假入库号、StubWmsQc 返回全合格]；GoodsReceipt[GrNo/PoNo/SupplierId/ReceiptDate/Status/WmsInboundNo/PostingBasis]、GoodsReceiptLine[PoLineNo/ItemId/ReceivedQty/AcceptedQty/RejectedQty/QcStatus/WmsReceiptDetailRef]）
- [ ] **Step 4-5: 迁移 + DI（桩，配置 SpacePurWms:Enabled 切真实）+ 提交** → `git commit -m "feat(pur): WMS receive/QC contracts + stubs + GR entities (ch03 §2/§3)"`

## Task C-2: 双基准收货 + 回写锚（章03 §4/§5）★

**Files:** Create `IGoodsReceiptService.cs`/`GoodsReceiptService.cs`; Test `GoodsReceiptServiceTests.cs`

- [ ] **Step 1: 失败测试**（着荷基准：收货→ReceivedQty+AcceptedQty 同累加→可建AP；检收基准：收货→只 ReceivedQty 累加，QcStatus=待检→ApplyQcResult 合格才 AcceptedQty 累加；回写 PO 行触发 DeriveStatus；超收挡）

```csharp
[Fact] public async Task Confirm_AccrualBasis_AcceptsImmediately()
{
    // PostingBasis=着荷 → ReceivedQty=AcceptedQty=收货量, PO 锚同步
}
[Fact] public async Task Confirm_InspectionBasis_AcceptsOnlyAfterQcPass()
{
    await svc.ConfirmReceiveAsync(grDto);   // 检收 → 只 Received 累加, Accepted=0
    Assert.Equal(0, poLine.AcceptedQty);
    await svc.ApplyQcResultAsync(grNo);     // QC 合格 → Accepted 累加
    Assert.Equal(qty, poLine.AcceptedQty);
}
```

- [ ] **Step 2: 跑红 → Step 3: 实现**（照 03 §4：ConfirmReceiveAsync[调 IWmsReceiveService 入库→WmsInboundNo；ReceivedQty 累加；着荷→AcceptedQty 同累加+免检；检收→QcStatus=待检 Accepted 不动]；ApplyQcResultAsync[查 IWmsQcQuery→合格累加 AcceptedQty/不良记 RejectedQty]；AddPoReceived/AddPoAccepted 回写 PO 行 + DeriveStatus；超收按容差挡）
- [ ] **Step 4: 跑绿 → Step 5: GoodsReceiptController + GR UI + 提交** → `git commit -m "feat(pur): dual-basis goods receipt + WMS delegation + anchor writeback (ch03 §4/§5)"`

---

# Phase D — 三单匹配（章04 ★MVP）

## Task D-1: 三单匹配实体 + 容差匹配（章04 §2/§3）★★

**Files:** Create `ThreeWayMatch.cs`/`MatchTolerance.cs`, `IThreeWayMatchService.cs`/`ThreeWayMatchService.cs`, `Contracts/IFinApService.cs`+桩; Modify `CP6Context.cs`; migration; Test `ThreeWayMatchServiceTests.cs`

- [ ] **Step 1: 失败测试（★MVP 核心）**

```csharp
[Fact] public async Task Match_WithinTolerance_AutoBuildsAp_AccumulatesInvoiced()
{
    // PO行 Accepted=100 Invoiced=0；发票 100@同价 → 容差内 → 建AP(填PurchaseOrderId) + InvoicedQty=100
    var r = await svc.MatchAsync(new InvoiceLineDto{PoLineId=pl,Qty=100,UnitPrice=10}, "u");
    Assert.Equal(0, r.Match.Status);                 // 通过
    Assert.Equal(100, poLine.InvoicedQty);
    Assert.Single(stubFinAp.CreatedInvoices.Where(x => x.PurchaseOrderId == poId));  // ★填了 PoId
}
[Fact] public async Task Match_OverTolerance_SuspendsForReview()
{
    // 发票价超容差 → Status=1 差异挂起, 不建AP, InvoicedQty 不动
}
[Fact] public async Task Match_CannotOverInvoice_BeyondAccepted()
{
    // Accepted=90 已 Invoiced=90 → 再开票 remainAccepted=0 → 挂起(防重复开票)
}
```

- [ ] **Step 2: 跑红 → Step 3: 实现**（照 04 §3：ThreeWayMatch/MatchTolerance 实体；MatchAsync[remainAccepted=AcceptedQty−InvoicedQty；qtyVar/priceVar；按物料类/供应商取容差 qtyOk/priceOk/金额绝对放行 amtOk；容差内→BuildApInvoice+InvoicedQty 累加+DeriveStatus，超容差→Status=1 挂起]；人工放行/拒留痕 HandledBy/Note）
- [ ] **Step 4: 跑绿 + 提交** → `git commit -m "feat(pur): three-way match with tolerance (accepted-invoiced + price) (ch04 §3)"`

## Task D-2: 接 AP（同步建发票填 PurchaseOrderId）+ 差异处理 + 控制器/UI（章04 §4/§5）

**Files:** Modify `ThreeWayMatchService.cs`(BuildApInvoice); `Contracts/IFinApService.cs`+桩; Controller; Test

- [ ] **Step 1: 失败测试**（匹配通过→IFinApService.CreateApInvoice(含 PurchaseOrderId/供应商/金额/税/PO冻结汇率)；差异挂起→人工放行 Status=2 留痕→建AP / 拒 Status=3）
- [ ] **Step 2: 跑红 → Step 3: 实现**（IFinApService 契约[CreateApInvoiceAsync(ApInvoiceCreateDto 含 PurchaseOrderId)] + StubFinAp[记录建票，财务 Plan2 真实实现]；BuildApInvoiceAsync[照 04 §4：填 PurchaseOrderId、PO 冻结汇率、SourceMatchRef]；差异放行/拒接口）
- [ ] **Step 4: 跑绿 → Step 5: ThreeWayMatchController + 匹配/差异处理 UI + DI 全装配（桩，配置切真实）+ 提交** → `git commit -m "feat(pur): match→AP (fills ApInvoice.PurchaseOrderId) + variance handling (ch04 §4/§5) — MVP done"`

---

## Self-Review（对照章01~04 覆盖）

- **章01**：复用 BusinessPartner 发注先(A-1 只读) ✅ / SupplierPrice 阶梯价+有效期(A-1) ✅ / 带价规则(A-1) ✅ / PostingBasis 带出(B-1) ✅
- **章02**：PO 头行+三累计锚(B-1) ✅ / 建单带出(B-1) ✅ / 派生状态机(B-2) ✅ / 冻结汇率(B-1,P-D4) ✅ / 审批桩(B-2,P-D1) ✅ / 金额税额(B-1) ✅ / 标准+外注同表 Type(B-1，外注流程归 Plan2 章07)
- **章03**：GR 单据不写库存(C-1/C-2) ✅ / IWmsReceiveService/QcQuery 委托+桩(C-1) ✅ / 双基准着荷/检收(C-2) ✅ / 回写锚+DeriveStatus(C-2) ✅ / 超收挡(C-2) ✅
- **章04**：三单匹配比 Accepted−Invoiced+价差(D-1) ✅ / 容差(数量%/价格%/金额绝对)(D-1) ✅ / 接 AP 填 PurchaseOrderId(D-2)★ ✅ / 差异挂起+人工放行留痕(D-1/D-2) ✅ / 防重复开票(D-1) ✅ / 双基准统一(D-1，Accepted 着荷=Received) ✅

**已知缺口/推迟（已标注）：**
1. **三委托接口真实对接**（P-D1）—— WMS/财务/OA 各自实现，本计划立契约+桩；真实对接属采购 Plan 2 章08 + 各模块改造。财务 Plan 2 已有真实 ApInvoice(填 PurchaseOrderId)。
2. **BusinessPartner 发注先字段确认**（P-D2）—— 缺则先补（主数据）。
3. **PR/RFQ/外注**（章05/06/07）—— 采购 Plan 2（阶段3-5）。
4. **TenantId/PUB 权限**（P-D5）—— OA 章10 / PUB B1 统一。

**Type 一致性：** `PurchaseOrderLine` 三累计锚(B-1) 被 GR 回写(C-2)/匹配累加(D-1) 一致用；`DeriveStatus`(B-2) 被 GR/匹配回写后调用；`AcceptedQty−InvoicedQty`(D-1) 是匹配可开票量；`IFinApService.CreateApInvoice(PurchaseOrderId)`(D-2) 对接财务 Plan 2 `ApInvoice.PurchaseOrderId`（财务 03 预留字段兑现）；`IWmsReceiveService`/`IApprovalService`(C-1/B-2) 桩起步。

---

## 执行交接

计划存 `docs/superpowers/plans/2026-06-13-pur-mvp-po-gr-match.md`。**采购第一份（MVP，补全财务 AP 前置）**。后续：
- 采购 Plan 2 = `2026-06-13-pur-extended-pr-rfq-subcontract.md`（章05 PR+需求驱动 + 06 RFQ + 07 外注加工 + 08 集成 + 09 完整性）

**下一步按工作流是你修订**（拍板 P-D1~D5）。定稿后执行：主数据 → PO → GR → 三单匹配；MVP 完成即"PO→收货→匹配→自动建应付发票"，与财务 Plan 2 的 `ApInvoice.PurchaseOrderId` 接通（采购填、财务消费）。

---

*初稿生成于 2026-06-13。源：docs/procurement/01·02·03·04。已勘察：BusinessPartner(发注先复用,字段待确认)、WMS InboundOrder/QcInspection 现成(接口需实现,P1桩)、财务 ApInvoice.PurchaseOrderId 预留(财务Plan2)、FxRate(Gap4.3)、DocNumber 采番、零多租户、Pur 命名空间全新建(目录未创建)。*
