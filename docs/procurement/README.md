# CP6 采购模块 · 完整设计与实现丛书

> **定位**：CP6 有受注→生产→出货的正向链，也刚补了财务（应收/应付/成本）。但"钱怎么花出去买原纸油墨"这条**采购链是缺的**——财务 AP 现在只能手工录供应商发票，因为没有采购订单可匹配。本模块补上 PR→RFQ→PO→收货→三单匹配，把财务 AP 从"手工录票"升级成"三单匹配自动建票"，并支持纸箱厂的**外注加工（委外印刷/模切）+ 有償支給**。
>
> 风格沿用 [`docs/finance`](../finance/README.md)、[`docs/oa`](../oa/README.md)：真实代码当教材，每章讲为什么这么设计、不这么写会出什么事、与业界（SAP MM / Odoo Purchase）怎么对比。
>
> 需求基线：含外注加工 / PR→PO 全流程 + 需求驱动 / 完整 RFQ / 三单容差匹配 / 双入账基准按供应商配 / 模块自洽+同步接口委托（低耦合可调试）。

---

## 一、先记住这一句话（题眼）

> **采购模块拥有全部单据与逻辑（PR/RFQ/PO/收货/匹配），但物理资源（库存、QC）通过同步接口单向委托给 WMS，应付通过同步接口委托给财务。模块自洽、依赖单向、无双写、无异步编排——可调试性优先。**

这是本模块和财务最大的设计差异：财务模块走 Phase 6 **异步事件**（最终一致）；采购模块刻意走**同步接口调用**（一条直线可追踪）。原因是采购"收货→入库"这种动作，调试时你要能一步步跟下去，而不是在事件死信里捞。**库存唯一真相在 WMS，应付唯一真相在财务，采购不双写它们**。

---

## 二、模块边界：四个同步接口

```
┌─────────────────────── 采购模块 (Procurement) ───────────────────────┐
│  PR 采购申请 → RFQ 询价比价 → PO 采购订单(标准/外注) → GR 收货单 → 三单匹配 │
│  自有数据：PR / RFQ / Quote / PO / POLine / GR / ThreeWayMatch / 采购价表  │
│  自有逻辑：比价选定、容差匹配、外注支給材成本核算                          │
└───┬──────────────┬──────────────────┬──────────────┬──────────────────┘
    │同步只读        │同步接口            │同步接口        │可插拔接口
    ▼              ▼                   ▼              ▼
 BusinessPartner  WMS 服务             财务 AP 服务     审批引擎
 供应商+发注先配置  IWmsReceive/Issue/   IFinApService   (先留桩,下个模块接)
 (复用)           QcQuery (库存唯一真相) (应付唯一真相)
```

| 接口 | 方向 | 职责 | 关联 |
|---|---|---|---|
| `IWmsReceiveService` | 采购→WMS | GR 确认时物理入库（WMS 写库存/批次/PaperRoll） | 着荷基准：入库即认 |
| `IWmsQcQuery` | 采购→WMS | 查 QC 检品结果 | 检收基准：QC 通过才确认 |
| `IWmsIssueService` | 采购→WMS | 外注发料出库（支給材） | 外注用 |
| `IFinApService` | 采购→财务 | 匹配通过 → 建 `ApInvoice`（填 `PurchaseOrderId`） | 补全财务 AP 前置 |
| `IApprovalService` | 采购→审批 | PR/PO 审批（**可插拔，先桩后接审批引擎**） | 下个模块 |

> 财务 AP 也走同步接口（非 Phase 6 事件），与"低耦合可调试"原则一致：采购匹配通过 → 同步调财务建发票，直线可追。财务内部"发票→凭证"仍走它自己的自动凭证引擎（模块内，不跨模块）。

---

## 三、最小数据模型（贯穿全书）

落 `CP6.Entity/DomainModels/Pur/`，与 Erp/Mes/Wms/Fin 平级，全表带 `TenantId`。

```
■ 采购申请 PR（阶段3）
  PurchaseRequest      PrNo, RequesterId, DeptId, RequestDate,
                       Status(草稿/已提/已批/驳回/已转PO/关闭),
                       Source(手工/MaterialShortage缺料/工单需求), SourceRefNo, ApprovalRef
  PurchaseRequestLine  PrNo, LineNo, ItemId, Qty, UnitCd, RequiredDate, EstPrice,
                       SuggestSupplierId, ConvertedPoNo, Status

■ 询价比价 RFQ（阶段4）
  Rfq                  RfqNo, Date, DueDate, Status, Buyer
  RfqLine              RfqNo, LineNo, ItemId, Qty, UnitCd, RequiredDate, SourcePrNo/PrLineNo
  RfqSupplier          RfqNo, SupplierId, InviteStatus
  RfqQuote             RfqNo, SupplierId, LineNo, QuotedPrice, CurrencyCd, LeadDays,
                       ValidUntil, IsSelected, Rank

■ 采购订单 PO（阶段0，标准+外注两型）
  PurchaseOrder        PoNo, SupplierId, OrderDate, Type(1标准/2外注),
                       CurrencyCd, FxRate, PostingBasis(检收/着荷, 默认取BusinessPartner),
                       Status(草稿/确认/部分收/收齐/部分票/关闭/取消),
                       NetAmount/TaxAmount/GrossAmount, SourceRfqNo, ApprovalRef
  PurchaseOrderLine    PoNo, LineNo, ItemId, Qty, UnitPrice, TaxCodeId, RequiredDate,
                       ReceivedQty, AcceptedQty, InvoicedQty,   ← 三单匹配的累计锚
                       MatchStatus(未匹配/已匹配/差异挂起), Status
  PoConsignMaterial    PoNo, LineNo, ConsignItemId, ConsignQty, ConsignUnitCost,
                       IssuedQty, WmsIssueNo   ← 外注:有償支給材料追踪

■ 收货 GR（阶段1，采购自有用于匹配；物理入库委托 WMS）
  GoodsReceipt         GrNo, PoNo, SupplierId, ReceiptDate,
                       Status(待检/已检收/部分/完成), WmsInboundNo, PostingBasis
  GoodsReceiptLine     GrNo, LineNo, PoLineNo, ItemId, ReceivedQty, AcceptedQty, RejectedQty,
                       QcStatus(免检/待检/合格/不良), WmsReceiptDetailRef

■ 三单匹配（阶段2 ★MVP）
  ThreeWayMatch        PoLineId, GrLineId, ApInvoiceLineRef,
                       QtyMatched, PriceVariance, QtyVariance, WithinTolerance,
                       Status(匹配通过/差异挂起/人工放行/异常), HandledBy, Note
  MatchTolerance       ItemClass/SupplierId, QtyTolerancePct, PriceTolerancePct, AmountAbsTol

■ 采购价表（阶段0）
  SupplierPrice        SupplierId, ItemId, Price, CurrencyCd, MinQty(阶梯), ValidFrom/To, Source
```

> **三单匹配的锚**：`PurchaseOrderLine` 的 `ReceivedQty/AcceptedQty/InvoicedQty` 三个累计量——收货累加 Received、QC 合格累加 Accepted、财务发票累加 Invoiced，匹配就是比这三个数 + 价格。

---

## 四、两条核心流程

### 流程 A — 标准采购闭环
```
PR(手工/缺料/工单) →提交→[审批桩]→已批
  → RFQ 邀N家→收报价→比价选中→回写采购价表
  → PO 选中报价转PO→确认(PostingBasis取供应商配置)
  → GR 到货建GR→同步调 IWmsReceiveService 物理入库
        ├ 着荷基准：入库即确认、ReceivedQty 累加
        └ 检收基准：待检→查IWmsQcQuery→合格才确认、AcceptedQty 累加
  → 三单匹配 PO↔GR↔发票 比数量/价格
        ├ 容差内→自动通过→同步调 IFinApService 建ApInvoice(填PoNo)
        └ 超容差→差异挂起→人工放行/拒
  → AP（财务内部）发票→自动凭证：借原材料+进项税/贷应付
```

### 流程 B — 外注加工闭环（Type=2）
```
外注PO(成品行 + PoConsignMaterial支給材)
  → 发料 支給材发外协→同步调 IWmsIssueService 出库→记 IssuedQty+支給材成本
  → (外协加工，系统外)
  → 收成品 GR→调 IWmsReceiveService 入库成品
  → 成本核算 加工费(PO单价)+支給材成本→成品成本（接财务06成本会计）
  → 三单匹配+AP 加工费走匹配→建ApInvoice
```
> 外注关键：**支給材发出不算消耗、不算卖**（仍是你的资产，位置在外协），`PoConsignMaterial` 追踪发了多少/回来多少（防外协吞料），收成品时支給材成本并入成品成本。

---

## 五、章节目录

### Part 0 · 总览
- **00. 心智模型 + 模块边界**（本页）

### Part 1 · MVP（补全财务 AP 前置）
- [01. 供应商与采购主数据](./01-master-data.md) — **阶段0**，复用 BusinessPartner 发注先 + 采购价表
- [02. 采购订单 PO](./02-purchase-order.md) — **阶段0**，标准 PO + 状态机 + PostingBasis
- [03. 收货 GR + WMS 委托](./03-goods-receipt.md) — **阶段1**，IWmsReceiveService/QcQuery、双基准
- [04. 三单匹配](./04-three-way-match.md) — **阶段2 ★MVP 核心**，容差匹配 + 接 AP

### Part 2 · 完整型扩展
- [05. 采购申请 PR + 需求驱动](./05-purchase-request.md) — **阶段3**，缺料/工单驱动 + 审批可插拔
- [06. 询价比价 RFQ](./06-rfq.md) — **阶段4**，询价→报价→比价→回写价表
- [07. 外注加工 + 有償支給](./07-subcontract.md) — **阶段5**，外注 PO + 支給材成本

### Part 3 · 集成与产品化
- [08. 与 CP6/财务集成](./08-integration.md) — 四接口落地、AP 对接、审批接口、Phase 6 边界
- [09. 完整性与异常](./09-integrity.md) — 防虚开/吞料/重复收货 + 采购对账

---

## 六、分阶段实施路线（范围都在，只是先后）

| 阶段 | 目标 | 完成标志 |
|---|---|---|
| **0** | 主数据 + PO 基础 | 能建采购订单（供应商复用、采购价带出） |
| **1** | PO→GR→WMS 入库委托 | 收货物理入库、ReceivedQty 累加、双基准生效 |
| **2 ★MVP** | 三单匹配→AP | 演示"PO→收货→匹配→自动建应付发票"，补全财务 AP 前置 |
| 3 | PR + 需求驱动 | 缺料/工单自动生成采购申请，审批走桩 |
| 4 | RFQ 询价比价 | 询价→比价→选定→转 PO，回写采购价表 |
| 5 | 外注加工 + 有償支給 | 外注 PO 发料→收成品→支給材成本核算 |

> **MVP = 阶段 0-2**：把财务 03 章预留的 `ApInvoice.PurchaseOrderId` 用起来，"手工录票"升级为"三单匹配自动建票"。

---

## 七、复用 vs 新建

| 能力 | CP6 现成的 | 怎么用 |
|---|---|---|
| 供应商主数据 + 采购配置 | `BusinessPartner` 发注先 Tab（SupplierPattern/检收基准/外注/有償支給/采购税码） | 只读复用，不新建供应商表 |
| 物理入库 + 批次 + PaperRoll | WMS `InboundOrder`/`InboundReceipt`（`PoNo` 钩子已留） | `IWmsReceiveService` 同步委托 |
| QC 检品 | WMS `QcInspection` | `IWmsQcQuery` 查检收结果 |
| 外注发料出库 | WMS 出库 | `IWmsIssueService` 同步委托 |
| 采购需求源 | `MaterialShortage`(Phase9 缺料) / 工单缺料 | PR 自动生成 |
| 应付对接 | 财务 `ApInvoice`（`PurchaseOrderId` 预留） | `IFinApService` 同步建票 |
| 外注/AP 凭证 | 财务自动凭证引擎 + 成本会计 | 支給材成本接财务 06 |
| 审批 | （审批引擎未建） | `IApprovalService` 桩，下个模块接 |

**唯一硬依赖：审批引擎未建。** PR/PO 审批先用 `IApprovalService` 简单状态机桩（单人/跳过），审批引擎做好再接——不阻塞采购落地。

---

## 八、与业界对照

| 想理解 | 去看 | 学什么 |
|---|---|---|
| 完整采购模块 | **Odoo Purchase** | PR/RFQ/PO/收货/账单的拼装、三单匹配（bill control: ordered/received qty） |
| 企业级采购 | **SAP MM** | PR→RFQ→PO→GR→IR(发票校验)、容差与冻结、外注(subcontracting + 委托库存) |
| 外注/委托库存 | **SAP 委外加工** | 支給材（components provided）发料、成品收货扣组件 |

> SAP 的 GR/IR（收货/发票校验）就是本书的"三单匹配"；它的 subcontracting components 就是本书的 `PoConsignMaterial`——核心模型全世界一样。

---

## 九、里程碑自检

- [ ] 我能说清"采购为什么走同步接口、财务为什么走异步事件"的区别和理由吗？
- [ ] 库存唯一真相在哪？采购为什么不能自己写库存？
- [ ] 三单匹配靠 PO 行的哪三个累计量？容差内/超容差分别怎么走？
- [ ] 检收基准和着荷基准在"何时确认 GR/建 AP"上差在哪？
- [ ] 外注的支給材发出去后，会计上算消耗了吗？收成品时成本怎么并？
- [ ] PR/PO 审批引擎没建，怎么不阻塞采购落地？

全部能答 → 采购链通，财务 AP 从"手工录票"升级为"三单匹配自动建票"，CP6 的"买"这条腿立住了。

---

*生成于 2026-06-10。需求基线见首部。配套实现将落于 `CP6.Entity/DomainModels/Pur`、`CP6.Core/Services/Pur`、`cp6.web/src/views/pur`（随章节推进）。*
