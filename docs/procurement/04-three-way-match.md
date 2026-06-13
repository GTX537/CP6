# 04 · 三单匹配（容差匹配 + 接 AP）

> **阶段 2 · ★MVP 核心。** 这是整个采购模块的题眼，也是它存在的最初理由：把 **PO、GR、发票**三张单的数量和价格对一遍，容差内**自动**建应付发票，超容差**挂起**人工处理。本章结束时，财务总纲里预留的 `ApInvoice.PurchaseOrderId` 被真正用起来——财务 AP 从"手工录票"升级为"三单匹配自动建票"。
>
> 上游：[02 PO](./02-purchase-order.md)（三个累计锚）、[03 收货](./03-goods-receipt.md)（`AcceptedQty`）。下游：[财务 AP](../finance/03-accounts-payable.md)（`IFinApService` 建发票）、[09 完整性](./09-integrity.md)（防虚开）。

---

## 一、题眼：比三个数 + 价格

> **三单匹配 = 拿 PO 行的三个累计量对一遍——订了多少（`Qty`）、验收合格多少（`AcceptedQty`）、开票多少（`InvoicedQty`）——再比单价。数量与价格都在容差内 → 自动建应付发票、累加 `InvoicedQty`；超容差 → 挂起人工放行/拒。**

这就是 SAP 说的 **GR/IR（收货/发票校验）**。三单是：

| 单 | 提供 | 在哪 |
|---|---|---|
| **PO** 采购订单 | 订了多少、约定单价 | [02](./02-purchase-order.md) `PurchaseOrderLine` |
| **GR** 收货单 | 验收合格多少（`AcceptedQty`） | [03](./03-goods-receipt.md) |
| **发票** 供应商发票 | 要钱多少、按什么价 | 供应商寄来、录入 |

> 匹配的意义：**不见货不付钱、不对量不付钱、不对价不付钱。** 供应商开 100 个的票，但你只验收了 90 个 → 差异挂起，不能稀里糊涂付 100 个的钱。三单匹配是采购防虚增应付的闸门。

---

## 二、数据模型

```csharp
// CP6.Entity/DomainModels/Pur/ThreeWayMatch.cs —— 一次匹配的明细
[Table("Pur_ThreeWayMatch")]
public class ThreeWayMatch : BaseEntity
{
    public Guid    PoLineId      { get; set; }
    public Guid?   GrLineId      { get; set; }
    public string? ApInvoiceLineRef { get; set; }   // 关联建出的应付发票行
    public decimal QtyMatched    { get; set; }
    public decimal PriceVariance { get; set; }       // 价差（发票价 − PO价）
    public decimal QtyVariance   { get; set; }       // 量差（发票量 − 已验收量）
    public bool    WithinTolerance { get; set; }
    public int     Status        { get; set; }       // 0通过/1差异挂起/2人工放行/3异常
    public Guid?   HandledBy     { get; set; }
    public string? Note          { get; set; }
}

// CP6.Entity/DomainModels/Pur/MatchTolerance.cs —— 容差配置
[Table("Pur_MatchTolerance")]
public class MatchTolerance : BaseEntity
{
    public string? ItemClass        { get; set; }    // 按物料类
    public Guid?   SupplierId       { get; set; }    // 或按供应商
    public decimal QtyTolerancePct  { get; set; }    // 数量容差 %
    public decimal PriceTolerancePct{ get; set; }    // 价格容差 %
    public decimal AmountAbsTol     { get; set; }    // 金额绝对容差（小额放行）
}
```

---

## 三、匹配逻辑

发票录入后，逐行对应到 PO 行，算量差与价差，判容差：

```csharp
// CP6.Core/Services/Pur/ThreeWayMatchService.cs
public async Task<MatchOutcome> MatchAsync(InvoiceLineDto inv, string? user)
{
    var poLine = await _db.PurchaseOrderLines.FindAsync(inv.PoLineId);
    var tol    = await ResolveTolerance(poLine);                       // 按物料类/供应商取容差

    // 数量：发票要开的量，不能超过"已验收 − 已开票"的剩余
    var remainAccepted = poLine.AcceptedQty - poLine.InvoicedQty;      // ★只认验收合格、未开票的量
    var qtyVar   = inv.Qty - remainAccepted;
    var priceVar = inv.UnitPrice - poLine.UnitPrice;

    bool qtyOk   = inv.Qty <= remainAccepted + remainAccepted * tol.QtyTolerancePct/100m;
    bool priceOk = Math.Abs(priceVar) <= poLine.UnitPrice * tol.PriceTolerancePct/100m;
    bool amtOk   = Math.Abs(qtyVar * inv.UnitPrice) <= tol.AmountAbsTol; // 小额绝对放行

    var match = new ThreeWayMatch {
        PoLineId = poLine.Id, QtyMatched = Math.Min(inv.Qty, remainAccepted),
        QtyVariance = qtyVar, PriceVariance = priceVar,
        WithinTolerance = (qtyOk && priceOk) || amtOk
    };

    if (match.WithinTolerance)
    {
        match.Status = 0;                                             // 通过
        await BuildApInvoiceAsync(poLine, inv, user);                // ★同步建 AP
        poLine.InvoicedQty += match.QtyMatched;                      // 第三个锚动
        poLine.MatchStatus = 1;
        DeriveStatus(poLine.Po);                                     // PO 状态派生（02）
    }
    else
    {
        match.Status = 1;                                            // 差异挂起
        poLine.MatchStatus = 2;
    }
    _db.ThreeWayMatches.Add(match);
    await _db.SaveChangesAsync();
    return new MatchOutcome(match);
}
```

**关键一行**：数量比的是 `AcceptedQty − InvoicedQty`（已验收合格、尚未开票的剩余），**不是订单量、也不是收货量**。因为只有"验收合格"的货才该付钱（[03 双基准](./03-goods-receipt.md)的 `AcceptedQty` 在此发挥），且不能重复开票。

---

## 四、接 AP：匹配通过同步建应付发票

匹配通过 → 同步调财务的 `IFinApService` 建 `ApInvoice`，**填上 `PurchaseOrderId`**：

```csharp
// 采购→财务（同步接口，总纲四接口之一）
public interface IFinApService
{
    Task<string> CreateApInvoiceAsync(ApInvoiceCreateDto dto);  // dto 含 PurchaseOrderId/供应商/金额/税
}

private async Task BuildApInvoiceAsync(PurchaseOrderLine poLine, InvoiceLineDto inv, string? user)
{
    await _finAp.CreateApInvoiceAsync(new ApInvoiceCreateDto {
        SupplierId      = poLine.Po.SupplierId,
        PurchaseOrderId = poLine.Po.Id,            // ★财务 03 章预留的字段，终于用上
        Amount          = inv.Qty * inv.UnitPrice,
        TaxCd           = poLine.TaxCodeId,
        CurrencyCd      = poLine.Po.CurrencyCd, FxRate = poLine.Po.FxRate, // 用 PO 冻结汇率
        SourceMatchRef  = "..."                    // 回指本次匹配，可追溯
    });
}
```

> **这就是 MVP 的全部意义。** 财务 [03 应付](../finance/03-accounts-payable.md) 当初在 `ApInvoice` 上预留了 `PurchaseOrderId` 却没人填——因为那时没有采购单可匹配，只能手工录票。现在采购把它填上：**PO→收货→匹配→自动建应付发票**，财务 AP 前置补全。财务内部"发票→自动凭证（借原材料/进项税、贷应付）"仍走它自己的凭证引擎，不跨模块。

**为什么同步而非事件？** 与 [03](./03-goods-receipt.md)同理：匹配通过 → 当场建票，一条直线可追。汇率用 PO 冻结值（[02](./02-purchase-order.md)），保证 PO、发票、凭证同一汇率。

---

## 五、容差与差异处理

```
匹配 ─┬─ 容差内 ──▶ 自动通过 → 建 AP → InvoicedQty 累加
      └─ 超容差 ──▶ 差异挂起 → 人工：放行(Status=2,记 HandledBy/Note) / 拒(Status=3)
```

- 容差**按物料类或供应商配**（`MatchTolerance`）：大宗原纸价格波动大、给宽一点；精密件给严一点。
- **金额绝对容差**（`AmountAbsTol`）：差几日元的零头直接放行，不为一分钱挂起——避免大量小额差异堵塞流程。
- 人工放行要**留痕**（`HandledBy`/`Note`）：谁放的、为什么放，可审计。这是防"随便放行虚增应付"的内控点。

---

## 六、防虚开（与 09 呼应）

三单匹配天然是防虚开闸门：

- **没有 PO + 没有验收合格的货 → 建不出 AP**。`MatchAsync` 必须挂到具体 `PoLineId`、且 `remainAccepted > 0`，凭空一张发票无从匹配。
- **不能重复开票**：`InvoicedQty` 累加且不能超 `AcceptedQty`（含容差）——同一批货开两次票，第二次 `remainAccepted` 不足，挂起。
- 这些规则把"虚开发票套现"挡在自动建票之外，详见 [09 完整性](./09-integrity.md)。

---

## 七、资深视角

**为什么数量基准是 `Accepted` 而不是 `Received`？** 因为付钱付的是"验收合格的货"。检收基准下，收到 100、合格 90，你只欠供应商 90 个的钱。用 `AcceptedQty − InvoicedQty` 当可开票量，QC 不良的自动不进应付——质量闸门和财务闸门在这里联动。

**三单还是两单？** 着荷基准（免检）其实是"两单匹配"（PO↔发票，`Accepted=Received`）；检收基准才是完整三单（PO↔GR↔发票）。本章用一套逻辑覆盖两者——因为 `AcceptedQty` 在着荷下等于收货量，公式不变。**双基准在匹配层自动统一**。

**容差是内控的旋钮**：太松→虚增应付溜过去；太紧→大量正常波动被挂起、采购被差异淹没。容差按品类/供应商精调，加金额绝对放行兜小额，是让"自动建票"既安全又顺畅的关键调参。

---

## 八、与业界对照

| 想理解 | 去看 | 学什么 |
|---|---|---|
| 三单匹配 | **SAP MM 发票校验（MIRO / GR-IR）** | PO×GR×发票三向校验、容差、冻结 |
| 容差与冻结 | **SAP 容差键（Tolerance Keys）** | 数量/价格/金额容差、超限冻结 |
| 采购对账 | **Odoo 账单控制（bill control: ordered/received）** | 按订购量 vs 收货量控票 |

> SAP 的 GR/IR 科目（收货/发票校验暂记）+ MIRO 三向匹配，就是本章——`AcceptedQty` 对应 GR、发票校验对应建 AP，容差键对应 `MatchTolerance`。核心模型全世界一致。

---

## 九、阶段2（★MVP）自检

- [ ] 三单是哪三单？匹配比哪三个数 + 什么？
- [ ] 可开票量为什么是 `AcceptedQty − InvoicedQty`，不是订单量或收货量？
- [ ] 匹配通过后做什么？`ApInvoice.PurchaseOrderId` 为什么是 MVP 的关键？
- [ ] 容差三个维度（数量%/价格%/金额绝对）各防什么？人工放行为什么要留痕？
- [ ] 三单匹配怎么防虚开和重复开票？
- [ ] 着荷基准为什么相当于两单匹配？一套逻辑怎么覆盖双基准？

全部能答 → **MVP 闭合**：PO→收货→匹配→自动建应付发票跑通，财务 AP 前置补全。采购模块的"最初理由"达成。后面 [05 PR](./05-purchase-request.md)、[06 RFQ](./06-rfq.md)、[07 外注](./07-subcontract.md) 是完整型扩展，[08](./08-integration.md)/[09](./09-integrity.md) 收口集成与完整性。

---

*实现：新建 `CP6.Entity/DomainModels/Pur/{ThreeWayMatch,MatchTolerance}.cs` + `CP6.Core/Services/Pur/ThreeWayMatchService.cs`；`IFinApService` 由财务实现，填 `ApInvoice.PurchaseOrderId`。*
