# 06 · 询价比价 RFQ

> **阶段 4 · 给采购装上"货比三家"。** [05 PR](./05-purchase-request.md) 解决了"要买什么"，但没回答"向谁买、什么价"。需求里那些**没定供应商**、或要**重新议价**的行，不能拍脑袋下单——要走询价：邀几家供应商报价、横向比价、选中一家，再把成交价**回写采购价表**喂给下次下单。本章结束时，采购有了价格发现机制，价表从"手工维护"升级为"询价自动沉淀"。
>
> 上游：[05 PR](./05-purchase-request.md)（未定供应商的行走询价）、[01 采购价表](./01-master-data.md)（询价的回写目标）。下游：[02 PO](./02-purchase-order.md)（选中报价转 PO）、[08 集成](./08-integration.md)。

---

## 一、题眼：RFQ 是价格与供应商的"竞争选择"

PR 是"我要买"，PO 是"向他买、这个价"，中间缺一环——**凭什么是他、凭什么这个价**。RFQ（Request for Quotation，询价）就是这一环：

> **把同一批需求同时发给 N 家供应商，收回各家报价，按价格 / 交期 / 起订量横向比，选定后回写价表。RFQ 是采购唯一的"价格发现"机制——没有它，采购价要么靠人脉拍脑袋，要么永远用旧价。**

为什么要把询价单独建一层，而不是在 PO 里随手填价？

- **可比性**：同一份需求（同物料、同数量、同交期要求）发给多家，报价才可比。临时各填各的没法比。
- **可追溯**：每个成交价能回答"比过哪几家、为什么选他"——`RfqQuote` 留着全部落选报价，审计能查。
- **价表沉淀**：选中报价回写 [`SupplierPrice`](./01-master-data.md)（`Source=rfq`），下次同物料下单直接带出，询价成果不丢。

> RFQ 不对供应商产生采购义务——它只是"请报个价"。报价（`RfqQuote`）也只是供应商的要约，选中后才转成对外承诺的 [PO](./02-purchase-order.md)。**RFQ→报价→选定→PO** 是"询价 → 要约 → 选择 → 订单"的转化。

---

## 二、数据模型

RFQ 一头三身：询价头、询价行（买什么）、被邀供应商（问谁）、报价（各家答什么）。落 `CP6.Entity/DomainModels/Pur/`，全表带 `TenantId`。

```csharp
// CP6.Entity/DomainModels/Pur/Rfq.cs —— 询价头
[Table("Pur_Rfq")]
public class Rfq : BaseEntity
{
    public string   RfqNo  { get; set; } = "";
    public DateTime Date   { get; set; }
    public DateTime DueDate{ get; set; }                 // 报价截止
    public int      Status { get; set; }                 // 草稿/已发/报价中/已比价/已选定/关闭
    public Guid?    Buyer  { get; set; }                 // 询价员
    public string?  SourcePrNo { get; set; }             // 从哪张 PR 发起（05 转来）
}

// CP6.Entity/DomainModels/Pur/RfqLine.cs —— 询价行（买什么）
[Table("Pur_RfqLine")]
public class RfqLine : BaseEntity
{
    public string   RfqNo       { get; set; } = "";
    public int      LineNo      { get; set; }
    public Guid     ItemId      { get; set; }
    public decimal  Qty         { get; set; }
    public string?  UnitCd      { get; set; }
    public DateTime RequiredDate{ get; set; }
    public string?  SourcePrNo  { get; set; }            // 行级来源（PR 行可来自不同 PR）
    public int?     SourcePrLineNo { get; set; }
}

// CP6.Entity/DomainModels/Pur/RfqSupplier.cs —— 被邀供应商（问谁）
[Table("Pur_RfqSupplier")]
public class RfqSupplier : BaseEntity
{
    public string RfqNo        { get; set; } = "";
    public Guid   SupplierId   { get; set; }             // 复用 BusinessPartner 发注先（01）
    public int    InviteStatus { get; set; }             // 已邀/已读/已报价/拒绝
}

// CP6.Entity/DomainModels/Pur/RfqQuote.cs —— 报价（各家答什么，每家×每行一条）
[Table("Pur_RfqQuote")]
public class RfqQuote : BaseEntity
{
    public string   RfqNo       { get; set; } = "";
    public Guid     SupplierId  { get; set; }
    public int      LineNo      { get; set; }            // 对应 RfqLine.LineNo
    public decimal  QuotedPrice { get; set; }
    public string   CurrencyCd  { get; set; } = "JPY";
    public int      LeadDays    { get; set; }            // 交期（天）
    public DateTime ValidUntil  { get; set; }            // 报价有效期
    public bool     IsSelected  { get; set; }            // ★选中标记
    public int      Rank        { get; set; }            // 比价名次（1=最优，比价时算出）
}
```

> **报价矩阵**：`RfqQuote` 是 `(供应商 × 询价行)` 的笛卡尔——3 家 × 4 行 = 12 条报价。比价就是在这张矩阵上，**按行**挑出每一行的最优供应商。一张询价单可以"按行拆给不同供应商"（A 家纸便宜、B 家油墨便宜）。

---

## 三、RFQ 全流程

```
建询价（从 PR / 手工）
  → 邀 N 家（RfqSupplier，复用 BusinessPartner 发注先）
  → 发出（Status=已发，记 InviteStatus）
  → 收报价（RfqQuote 录入：价/交期/有效期）
  → 比价（按行算 Rank：价格优先、交期/MOQ 兜底）
  → 选定（IsSelected，可整单一家 / 按行拆多家）
  → 回写采购价表（SupplierPrice，Source=rfq）
  → 转 PO（复用 02 CreateAsync，按选中供应商分组）
```

### 1. 从 PR 发起询价

[05 PR](./05-purchase-request.md) 里没定供应商（`SuggestSupplierId == null`）或需重新议价的行，汇成一张 RFQ：

```csharp
// CP6.Core/Services/Pur/RfqService.cs
public async Task<string> CreateFromPrAsync(string prNo, IEnumerable<int> prLineNos, string? user)
{
    var rfq = new Rfq { RfqNo = await _seq.NextAsync("RFQ"), Date = _clock.Today,
        Status = 0, SourcePrNo = prNo };

    int ln = 1;
    foreach (var pl in await LoadPrLines(prNo, prLineNos))
        rfq.Lines.Add(new RfqLine { LineNo = ln++, ItemId = pl.ItemId, Qty = pl.Qty,
            UnitCd = pl.UnitCd, RequiredDate = pl.RequiredDate,
            SourcePrNo = prNo, SourcePrLineNo = pl.LineNo });   // 行级追溯回 PR

    _db.Rfqs.Add(rfq);
    await _db.SaveChangesAsync();
    return rfq.RfqNo;
}
```

> **需求归集在这里发生**：多张 PR 的同物料行可以汇进一张 RFQ 的一行（合量询价拿低价），也可以一张 PR 的多行拆进一张 RFQ——`SourcePrNo/SourcePrLineNo` 保证不管怎么并怎么拆，每条都追得回源头需求。

### 2. 邀请与发出

挑 N 家供应商（复用 [`BusinessPartner` 发注先](./01-master-data.md)，不新建供应商），落 `RfqSupplier`，状态推到"已发"。发出后等供应商报价（线上填或采购代录）。

### 3. 收报价

每家对每行报一个 `RfqQuote`：价格、币种、交期 `LeadDays`、有效期 `ValidUntil`。**有效期是硬约束**——过期报价不能被选中转 PO（防止用一个早已失效的低价去下单）。

---

## 四、比价与选定：按行算名次

比价的核心是**按行**给各家报价排名次（`Rank`），默认"价格优先、交期/MOQ 兜底"，但名次只是建议，**选谁由采购拍板**（`IsSelected`）——系统给排序，人做决策。

```csharp
// CP6.Core/Services/Pur/RfqService.cs —— 比价：按行排名
public async Task RankQuotesAsync(string rfqNo)
{
    var quotes = await _db.RfqQuotes.Where(q => q.RfqNo == rfqNo).ToListAsync();

    foreach (var grp in quotes.GroupBy(q => q.LineNo))          // ★按行分组比
    {
        var ranked = grp
            .Where(q => q.ValidUntil >= _clock.Today)           // 先剔除过期报价
            .OrderBy(q => q.QuotedPrice)                        // 价格优先
            .ThenBy(q => q.LeadDays)                            // 同价比交期
            .ToList();
        for (int i = 0; i < ranked.Count; i++) ranked[i].Rank = i + 1;   // 1=最优
    }
    await _db.SaveChangesAsync();
}

// 选定：可整单选一家，也可按行拆给不同家
public async Task SelectAsync(string rfqNo, IEnumerable<(int lineNo, Guid supplierId)> picks, string? user)
{
    var quotes = await _db.RfqQuotes.Where(q => q.RfqNo == rfqNo).ToListAsync();
    foreach (var (lineNo, supplierId) in picks)
    {
        var q = quotes.First(x => x.LineNo == lineNo && x.SupplierId == supplierId);
        if (q.ValidUntil < _clock.Today) throw new BizException($"行{lineNo} 报价已过期，不能选定");
        q.IsSelected = true;
    }
    var rfq = await _db.Rfqs.FirstAsync(r => r.RfqNo == rfqNo);
    rfq.Status = 已选定;
    await _db.SaveChangesAsync();
}
```

> **为什么名次是建议、不是自动选最低？** 最低价不一定最优：交期赶不上、起订量不匹配、质量历史差，采购都可能选第二低。系统的职责是把可比维度摆清楚（`Rank` + 价/期/MOQ），**决策权留给人**。这也是和"自动取最低价"最大的区别——比价是辅助决策，不是替人做主。

---

## 五、回写采购价表：询价成果的沉淀

选定后，把每条选中报价回写 [`SupplierPrice`](./01-master-data.md)，`Source="rfq"`——这是 RFQ 闭环的关键一步，让询价结果变成下次下单能直接带出的价表。

```csharp
// CP6.Core/Services/Pur/RfqService.cs —— 选中报价回写价表
public async Task WriteBackPricesAsync(string rfqNo, string? user)
{
    var selected = await _db.RfqQuotes
        .Where(q => q.RfqNo == rfqNo && q.IsSelected).ToListAsync();
    var lineItem = await _db.RfqLines.Where(l => l.RfqNo == rfqNo)
        .ToDictionaryAsync(l => l.LineNo, l => l.ItemId);

    foreach (var q in selected)
        await _price.UpsertAsync(new SupplierPrice {               // 复用 01 价表服务
            SupplierId = q.SupplierId, ItemId = lineItem[q.LineNo],
            Price = q.QuotedPrice, CurrencyCd = q.CurrencyCd,
            ValidFrom = _clock.Today, ValidTo = q.ValidUntil,
            Source = "rfq"                                          // ★标明来自询价
        }, user);
}
```

> **价表的双向闭环**：[01 章](./01-master-data.md) 说过价表"既是输入也是 RFQ 的产物"——这里就是产物的来源。`Source=rfq` 让价表能区分"手工维护"和"询价沉淀"，下次同物料下单时 `ResolvePriceAsync` 直接命中，不必再询。**询一次，沉淀一次，复用多次。**

---

## 六、RFQ → PO：选中即可下单

选定的报价转 PO，复用 [02 章 `CreateAsync`](./02-purchase-order.md)，按选中供应商分组（一行一供应商 → 同供应商的行合一张 PO）：

```csharp
public async Task<List<string>> ConvertToPoAsync(string rfqNo, string? user)
{
    var selected = await _db.RfqQuotes.Where(q => q.RfqNo == rfqNo && q.IsSelected).ToListAsync();

    var poNos = new List<string>();
    foreach (var grp in selected.GroupBy(q => q.SupplierId))       // ★按选中供应商拆 PO
        poNos.Add(await _po.CreateAsync(BuildPoDtoFromRfq(rfqNo, grp), user));  // 带价/税/PostingBasis（02）

    return poNos;
}
```

> 价格已在询价时定（选中报价），转 PO 直接带过去，不再走价表取价——**询价单的成交价就是 PO 价**。`BuildPoDtoFromRfq` 把选中报价的 `QuotedPrice/CurrencyCd` 填进 PO 行；`PostingBasis` 仍按 [供应商配置](./02-purchase-order.md) 取（检收/着荷）。

---

## 七、资深视角

**RFQ 是采购"省钱"的核心环节，不是流程负担。** 没有询价，采购价靠人脉和惯性，常年用一个偏高的旧价；有了询价，每次大额采购都能逼出市场价。CP6 做完整可售产品，RFQ 不能省——它是采购降本最直接的工具。

**"按行拆供应商"是 RFQ 比 PR 转 PO 更细的地方。** [05 PR→PO](./05-purchase-request.md) 按"建议供应商"整行分组；RFQ 是**先比价再分组**，同一张询价单的不同行可能花落不同家（纸找 A、油墨找 B）。报价矩阵 `(供应商 × 行)` 就是为这个细粒度准备的。

**报价有效期 `ValidUntil` 不是装饰。** 供应商报价有时效（原材料价格波动），过期报价拿去下单，供应商可以拒单或要求重新报价。系统在"选定"和"转 PO"两道关都校验有效期，把"用废价下单"挡在前面——这是真实采购里反复踩的坑。

**比价排名 ≠ 自动决策。** 见过太多系统把"自动选最低价"当卖点，结果交期、质量、起订量全不管，采购被迫线下绕过系统。正确做法：系统给排序与多维度对比，**人按 `Rank` 参考、按 `IsSelected` 拍板**。辅助决策的系统才会被真正用起来。

---

## 八、与业界对照

| 想理解 | 去看 | 学什么 |
|---|---|---|
| 完整询价比价 | **SAP MM 询价（RFQ / ME41）→ 报价（ME47）→ 比价（ME49）** | 询价单、报价录入、价格比较表、维护采购信息记录 |
| 询价转订单 | **Odoo Purchase RFQ → Purchase Order** | RFQ 即 PO 草稿、确认转单、供应商比价 |
| 价格主数据沉淀 | **SAP 采购信息记录（Info Record）自动更新** | 询价选中价回写信息记录，下单自动带出 |

> SAP 的 ME49"报价比较表"就是本章按行排名的 `Rank`；它的"询价选中 → 更新采购信息记录"就是本章的"回写 `SupplierPrice`（Source=rfq）"——询价喂价表、价表喂下单，全世界一个闭环。

---

## 九、阶段4（RFQ 部分）自检

- [ ] RFQ 在 PR 和 PO 之间补了哪一环？为什么不能在 PO 里随手填价？
- [ ] `RfqQuote` 为什么是 `(供应商 × 行)` 矩阵？"按行拆供应商"靠它怎么实现？
- [ ] 比价排名 `Rank` 是自动选最低价吗？为什么选定要留给人（`IsSelected`）？
- [ ] 报价有效期 `ValidUntil` 在哪两道关被校验？不校验会出什么事？
- [ ] 选中报价怎么回写价表？`Source=rfq` 有什么用？询价和价表怎么形成闭环？
- [ ] RFQ→PO 的价从哪来（价表取价 还是 询价成交价）？按什么分组拆 PO？

全部能答 → 采购有了价格发现：询价→报价→比价→选定→回写价表→转 PO 闭环跑通，价表从"手工维护"升级为"询价自动沉淀"。下一步 [07 外注加工 + 有償支給](./07-subcontract.md)：纸箱厂的委外印刷/模切，外注 PO 发支給材→收成品→成本核算。

---

*实现：新建 `CP6.Entity/DomainModels/Pur/{Rfq,RfqLine,RfqSupplier,RfqQuote}.cs` + `CP6.Core/Services/Pur/RfqService.cs`；复用 `BusinessPartner` 发注先（询价对象）、[01 `SupplierPriceService`](./01-master-data.md)（回写价表）、[02 `PurchaseOrderService.CreateAsync`](./02-purchase-order.md)（转 PO）、[05 PR](./05-purchase-request.md)（未定供应商的行发起询价）。*
