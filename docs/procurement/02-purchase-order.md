# 02 · 采购订单 PO：标准 PO + 状态机 + PostingBasis

> **阶段 0 · 采购的核心单据。** PO（采购订单）是采购链的主角——选供应商、带采购价、算金额、走审批、驱动收货与匹配。更重要的是：**[三单匹配](./04-three-way-match.md) 的"锚"就长在 PO 行上**（`ReceivedQty/AcceptedQty/InvoicedQty` 三个累计量）。本章把标准 PO 的数据模型、状态机、PostingBasis 做出来；外注 PO（Type=2）留到 [07](./07-subcontract.md)。
>
> 上游：[01 主数据](./01-master-data.md)（供应商/采购价/PostingBasis）。下游：[03 收货](./03-goods-receipt.md)（累加 Received/Accepted）、[04 三单匹配](./04-three-way-match.md)（比三个累计量）、[08 集成](./08-integration.md)（审批/AP）。

---

## 一、题眼：PO 行的三个累计量，是整条采购链的锚

```
PurchaseOrderLine
  Qty           ← 订了多少
  ReceivedQty   ← 收了多少（03 收货累加）
  AcceptedQty   ← QC 合格多少（03 检收累加）
  InvoicedQty   ← 开了多少票（04/AP 累加）
```

> **采购的全部状态，本质就是这三个数和 `Qty` 的关系。** 收货累加 `Received`、QC 合格累加 `Accepted`、财务开票累加 `Invoiced`。三单匹配就是比这三个数 + 价格；PO 状态（部分收/收齐/部分票/关闭）也是从这三个数推出来的。把这三个累计量设计对，整条链就立住了。

记住这个锚，后面 03/04 都围着它转。

---

## 二、数据模型：头 + 行

```csharp
// CP6.Entity/DomainModels/Pur/PurchaseOrder.cs —— 头
[Table("Pur_PurchaseOrder")]
public class PurchaseOrder : BaseEntity
{
    public string  PoNo        { get; set; } = "";   // 采购单号
    public Guid    SupplierId  { get; set; }         // → BusinessPartner 发注先（01章复用）
    public DateTime OrderDate  { get; set; }
    public int     Type        { get; set; } = 1;    // 1标准 / 2外注（07章）
    public string  CurrencyCd  { get; set; } = "JPY";
    public decimal FxRate      { get; set; } = 1m;   // ★下单时冻结的汇率
    public string  PostingBasis{ get; set; } = "检收";// 检收/着荷，默认取供应商（01章）
    public int     Status      { get; set; } = 0;    // 见第四节状态机
    public decimal NetAmount   { get; set; }         // 不含税
    public decimal TaxAmount   { get; set; }
    public decimal GrossAmount { get; set; }         // 含税
    public string? SourceRfqNo { get; set; }         // 从哪条 RFQ 来（06章）
    public string? ApprovalRef { get; set; }         // 审批实例引用（08章/approval 05）
}

// CP6.Entity/DomainModels/Pur/PurchaseOrderLine.cs —— 行
[Table("Pur_PurchaseOrderLine")]
public class PurchaseOrderLine : BaseEntity
{
    public string  PoNo        { get; set; } = "";
    public int     LineNo      { get; set; }
    public Guid    ItemId      { get; set; }
    public decimal Qty         { get; set; }
    public decimal UnitPrice   { get; set; }         // 01 价表带出，可改
    public string? TaxCodeId   { get; set; }         // 供应商采购税码带出
    public DateTime RequiredDate{ get; set; }
    public decimal ReceivedQty { get; set; }         // ★累计锚
    public decimal AcceptedQty { get; set; }         // ★累计锚
    public decimal InvoicedQty { get; set; }         // ★累计锚
    public int     MatchStatus { get; set; }         // 0未匹配/1已匹配/2差异挂起
    public int     Status      { get; set; }
}
```

---

## 三、建 PO：复用与带出

建 PO 是一连串"从主数据带值"，把 [01 章](./01-master-data.md) 的复用兑现：

```csharp
// CP6.Core/Services/Pur/PurchaseOrderService.cs（建单核心）
public async Task<string> CreateAsync(PoDto dto, string? user)
{
    var sup = await _db.BusinessPartners.FirstAsync(b => b.Id == dto.SupplierId && b.SupplierFlg);
    var po = new PurchaseOrder {
        PoNo        = await _seq.NextAsync("PO"),
        SupplierId  = sup.Id,
        CurrencyCd  = dto.CurrencyCd ?? sup.CurrencyCd ?? "JPY",
        FxRate      = await _fx.FreezeRateAsync(...),                 // ★冻结汇率（财务07同源）
        PostingBasis= dto.PostingBasis ?? PostingFrom(sup.PurchasePostingDiv), // 供应商带出，可覆盖
        Status      = 0,                                             // 草稿
    };
    foreach (var l in dto.Lines)
    {
        l.UnitPrice ??= await _price.ResolvePriceAsync(sup.Id, l.ItemId, l.Qty, po.OrderDate); // 01 价表带出
        l.TaxCodeId ??= sup.PurchaseTaxCd;                          // 供应商税码带出
        po.Lines.Add(MapLine(l));
    }
    CalcAmounts(po);                                                 // 算 Net/Tax/Gross（见第五节）
    _db.PurchaseOrders.Add(po);
    await _db.SaveChangesAsync();
    return po.PoNo;
}
```

- **供应商必须 `SupplierFlg=true`**——不能给非供应商的取引先下采购单。
- **价、税码、币种、PostingBasis 都先从主数据带出**，再允许单据上覆盖。带出是默认、覆盖是例外。

---

## 四、PO 状态机：被收货与开票"推着走"

```
        提交+审批通过
草稿(0) ───────────────▶ 确认(1)
                          │ 收货累加 Received（03）
                          ▼
                    部分收货(2) ──收齐──▶ 收货完成(3)
                          │ 开票累加 Invoiced（04/AP）
                          ▼
                    部分开票(4) ──票齐+匹配──▶ 关闭(5)
草稿/确认 ──取消──▶ 取消(9)
```

状态不是手工点的，而是**三个累计量到了某个关系就自动迁移**：

```csharp
private int DeriveStatus(PurchaseOrder po)
{
    var lines = po.Lines;
    bool allReceived = lines.All(l => l.ReceivedQty >= l.Qty);
    bool anyReceived = lines.Any(l => l.ReceivedQty > 0);
    bool allInvoiced = lines.All(l => l.InvoicedQty >= l.Qty);
    if (po.Status == 9) return 9;                       // 取消是终态
    if (allInvoiced && AllMatched(lines)) return 5;     // 票齐且匹配 → 关闭
    if (lines.Any(l => l.InvoicedQty > 0)) return 4;    // 部分开票
    if (allReceived) return 3;                          // 收齐
    if (anyReceived) return 2;                          // 部分收货
    return po.Status;                                   // 否则维持（草稿/确认）
}
```

> **PO 状态是派生量，不是输入量。** 收货服务累加 `ReceivedQty` 后调一次 `DeriveStatus`、AP 累加 `InvoicedQty` 后再调——状态永远和三个累计量一致，不会出现"显示已收齐、其实没收"的割裂。取消是唯一的人工终态（且只有草稿/确认能取消，收过货的不能直接取消）。

---

## 五、金额与税额计算

```csharp
private void CalcAmounts(PurchaseOrder po)
{
    foreach (var l in po.Lines) l.Amount = l.Qty * l.UnitPrice;     // 行额
    po.NetAmount = po.Lines.Sum(l => l.Amount);                     // 不含税合计
    po.TaxAmount = po.Lines.Sum(l => TaxOf(l, po));                 // 按行税码算税，端数处理
    po.GrossAmount = po.NetAmount + po.TaxAmount;
}
```

税按**行税码**（供应商 `PurchaseTaxCd` 带出，可改）算，端数处理用供应商的 `PurchaseTaxFractionDiv`。这套税额会在 [04 三单匹配](./04-three-way-match.md) 和发票比对，所以**算法要和财务一致**（同一套端数规则），否则匹配时税额永远差一分钱。

---

## 六、多币种与冻结汇率

进口原纸/油墨常用外币 PO。`CurrencyCd` + `FxRate`：

- 下单时**冻结当时汇率**（`FxRate`），后续收货、匹配、入账都用这个冻结值换算本币，**不随市场汇率漂**。
- 这与 [财务 07 多币种](../finance/README.md) 的"为替凍結"完全同源——采购冻结的汇率会一路传到 AP，保证 PO、发票、凭证用的是同一个汇率，不会因汇率波动产生虚假差异。

> 冻结汇率是多币种采购的命门：不冻，PO 报 100 美元、收货那天汇率变了、开票又变了，三单匹配的金额永远对不上。冻结后，差异只可能来自真实的量价差，而非汇率噪声。

---

## 七、审批接入

PO 提交确认前走审批（金额大的要部门长/财务批）。采购**不自己实现审批**，调 [approval 05 的 `IApprovalService`](../approval/05-integration.md)：

```
PO 草稿 →提交→ IApprovalService.SubmitAsync("PO", poId, buyerId, {amount, supplierId})
  审批通过 → IApprovalCallback.OnApprovedAsync(poId) → PO.Status=确认(1)，记 ApprovalRef
  审批驳回 → PO 退回草稿
```

> 在审批引擎建好前，`IApprovalService` 是"单人/跳过"的桩（总纲），PO 直接确认；引擎好了无缝换实现。`ApprovalRef` 存审批实例号，便于回溯"这单谁批的"。依赖单向：采购调审批，不反向。

---

## 八、资深视角

**为什么 PO 状态要派生、不要手工点？** 手工状态迟早和实际不符——有人收了货忘了改状态、或改错。让状态从 `Received/Accepted/Invoiced` 三个客观累计量派生，状态就永远是真的。**单据状态机的黄金法则：状态是事实的投影，不是独立的输入。**

**为什么三个累计量放在 PO 行、而不是另建匹配表算？** 因为它们是高频读写的"当前进度"，放行上一次更新、一次读取最快。匹配表（[04](./04-three-way-match.md)）记的是"每一次匹配的明细流水"，两者分工：行上是累计现值，匹配表是历史明细。

**Type=1/2 为什么同表？** 标准采购和外注采购 80% 字段相同（供应商、金额、收货、匹配），只是外注多了"支给材"（[07](./07-subcontract.md)的 `PoConsignMaterial`）。同表 + Type 区分，比两套 PO 表省一半代码，外注的特殊部分用子表挂。

---

## 九、与业界对照

| 想理解 | 去看 | 学什么 |
|---|---|---|
| 采购订单 + 收货/发票进度 | **SAP MM 采购订单（PO history）** | PO 行的 GR/IR 数量累计，正是三个累计量 |
| PO 状态驱动 | **Odoo Purchase（draft/purchase/done）** | 状态随收货开票推进 |
| 多币种采购 | **SAP 采购汇率冻结** | PO 汇率与后续单据一致 |

> SAP PO 行的"PO history（收货数量、发票数量累计）"就是本章的 `ReceivedQty/InvoicedQty`——三单匹配（GR/IR）全靠它，核心模型一致。

---

## 十、阶段0（PO 部分）自检

- [ ] PO 行的三个累计量分别由谁累加？为什么说它们是整条采购链的锚？
- [ ] 建 PO 时哪些值从主数据带出？带出和覆盖的关系？
- [ ] PO 状态为什么是派生量？`DeriveStatus` 靠什么判定收齐/部分票/关闭？
- [ ] 多币种为什么要冻结汇率？不冻会怎样？
- [ ] PO 审批怎么接？采购为什么不自己实现审批？
- [ ] 标准 PO 和外注 PO 为什么同表 + Type 区分？

全部能答 → PO 立住了。下一步 [03 收货 GR](./03-goods-receipt.md)：到货建 GR，同步委托 WMS 物理入库，按 PostingBasis 累加 `ReceivedQty`/`AcceptedQty`——三个锚里的前两个开始动。

---

*实现：新建 `CP6.Entity/DomainModels/Pur/{PurchaseOrder,PurchaseOrderLine}.cs` + `CP6.Core/Services/Pur/PurchaseOrderService.cs`；审批走 `IApprovalService`、汇率走财务 FxRate 服务。*
