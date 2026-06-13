# 04 · 应收 AR：出货 → 发票 → 收款 → 核销

> **阶段 3。** AR 是 AP 的镜像（客户欠我，而非我欠供应商），结构对称。但 AR 有一个 AP 没有的杀手锏：**出货自动开票**——它能直接吃 CP6 现成的 `Order`/`Outbound` 数据，出一次货、收入凭证和成本结转凭证自动生成。本章结束时，你能演示"确认出货 → 账上自动确认收入、结转成本、挂上客户应收"。
>
> 上游：[01 总账](./01-gl-kernel.md)、[03 AP](./03-accounts-payable.md)（对称结构，本章只讲差异）、[05 自动凭证引擎](./05-auto-voucher.md)。

---

## 一、AR 与 AP 的对称（先省掉一半篇幅）

AR 的发票/收款/核销，和 AP 的发票/付款/核销**结构完全对称**，只是方向相反：

| 维度 | AP（应付） | AR（应收） |
|---|---|---|
| 谁欠谁 | 我欠供应商 | 客户欠我 |
| 控制科目 Role | `AP_CONTROL`（负债，贷方余额） | `AR_CONTROL`（资产，借方余额） |
| 单据 | 供应商发票 → 付款 → 核销 | 销售发票 → 收款 → 核销 |
| 实体 | `ApInvoice`/`Payment`/`ApSettlement` | `ArInvoice`/`Receipt`/`ArSettlement` |
| 税 | 进项税 `TAX_INPUT` | 销项税 `TAX_OUTPUT` |
| 红字 | 供应商红字（采购退货） | 信用单（销售退货，**CP6 已有 `CreditNote`**） |

> [03 章](./03-accounts-payable.md) 讲过的：核销多对多、尾差/折扣、防重、账龄、勾稽、红冲、收款撤销——AR **照搬**，把"付"换"收"、"供应商"换"客户"即可。本章不重复这些，只讲 AR 独有的两件事：**出货自动开票** 和 **信用控制**。
>
> **镜像实体（与 AP 对称定义，字段一一对应，不再贴全）**：`ArInvoiceLine`↔`ApInvoiceLine`（行+税+科目+成本中心）、`Receipt`↔`Payment`（收款单，含 `BankAccountId`/`Status`/撤销）、`ArSettlement`↔`ApSettlement`（核销，含 `DiffAmount/DiffType/DiffAccountId` 尾差折扣）。AR 收款无"预付"概念但有"预收账款"（客户预付），与 AP 预付对称。

```csharp
// CP6.Entity/DomainModels/Fin/ArInvoice.cs（与 ApInvoice 镜像，差异字段）
public class ArInvoice : BaseEntity
{
    public int TenantId { get; set; }
    public string No { get; set; } = "";
    public string CustomerId { get; set; } = "";       // → BusinessPartner（客户）
    public DateTime InvoiceDate { get; set; }
    public DateTime DueDate { get; set; }
    public string CurrencyCd { get; set; } = "CNY";    // MVP 本位币（见 03 多币种范围）
    public decimal NetAmount, TaxAmount, GrossAmount;
    public decimal SettledAmount { get; set; }
    public ArInvoiceStatus Status { get; set; }

    public Guid? ShipmentId { get; set; }              // ★ 来自哪张出货单（自动开票的根）
    public Guid? OrderId { get; set; }                 // 关联受注（CP6 Order）
    public bool IsCreditMemo { get; set; }             // 销售退货红字（接 CP6 CreditNote）
    public Guid? JournalEntryId { get; set; }
    public List<ArInvoiceLine> Lines { get; set; } = new();
}
public enum ArInvoiceStatus { Unpaid = 0, Partial = 1, Paid = 2, Reversed = 9 }
```

---

## 二、杀手锏：出货自动开票（吃 CP6 现成数据）

AP 的发票要手工录（采购模块没建）。但 AR **不用**——CP6 已经有完整的 `Order`（受注）和 `Outbound`/出货数据。出货一确认，发票就能自动生成。这是财务模块**最快见效、最有演示力**的一环。

### 出货时生成两张凭证（收入确认 + 成本结转）

一次出货，会计上同时发生两件事，要记两组分录：

```
出货：卖出纸箱，售价 10,000（税 1,300），这批货成本 6,000

① 收入确认（确认应收 + 收入 + 销项税）：
   借  应收账款 (AR_CONTROL, 客户)   11,300
   贷  主营业务收入 (REVENUE)              10,000
   贷  应交税费—销项税 (TAX_OUTPUT)         1,300

② 成本结转（货出库了，库存变成本）：
   借  主营业务成本 (COGS)            6,000
   贷  库存商品 (FG)                       6,000
```

> **为什么必须两张？** 收入和成本要**配比**（matching principle）——确认这笔收入的同时，必须把对应的成本也结转，否则利润虚高。①来自售价（Order 单价），②来自这批货的成本（[06 章成本会计](./06-cost-accounting.md)算出的 FG 单位成本）。**没有 06 章的成本数据，②就只能用估算成本**——这是 AR 和成本会计的耦合点。

### 怎么挂上 CP6 现成的出货链

CP6 已有 `IErpBridgeHook`（出库 → 订单回写）。AR 自动开票就在这条链上再挂一刀（[05 章](./05-auto-voucher.md)的 `FinBridgeHook`）：

```csharp
public async Task OnShipmentConfirmedAsync(ShipmentConfirmedEvent e)
{
    // ① 生成 AR 发票（带 ShipmentId/OrderId，幂等键防重复开票）
    var inv = await _arInvoiceService.CreateFromShipmentAsync(e);
    // ② 收入确认凭证（DocumentLines 透传：每个出货明细按产品收入科目入账）
    await _engine.GenerateAsync(FinBizEvent.ArRevenue(inv));
    // ③ 成本结转凭证（金额取这批货的 FG 成本，来自 06 章）
    await _engine.GenerateAsync(FinBizEvent.ArCogs(e, costFromShipment));
}
```

幂等键 = `ShipmentId`：同一张出货单不会开两张发票（[05 章幂等](./05-auto-voucher.md#四幂等自动凭证最容易出的事故)）。出货取消（你现成的 `OrderCancelBridgeHook`）→ 两张凭证都红冲、发票作废。

---

## 三、信用控制：出货前先看客户欠多少

AR 独有的风控：客户信用额度。**出货前检查"该客户已欠 + 本单金额 > 信用额度"就拦截**，防呆账。

```csharp
public async Task<Result> CheckCreditAsync(string customerId, decimal orderAmount)
{
    var partner = await _db.BusinessPartners.FindAsync(customerId);   // 复用取引先
    if (partner.CreditLimit <= 0) return Result.Ok();                 // 0=不控制

    var openAr = await _db.ArInvoices
        .Where(i => i.CustomerId == customerId && i.Status != ArInvoiceStatus.Paid)
        .SumAsync(i => i.GrossAmount - i.SettledAmount);              // 当前应收余额

    if (openAr + orderAmount > partner.CreditLimit)
        return Result.Fail($"超信用额度：已欠 {openAr} + 本单 {orderAmount} > 额度 {partner.CreditLimit}");
    return Result.Ok();
}
```

> `CreditLimit` 加在现有 `BusinessPartner` 上。这个检查挂在**出货确认前**（或受注确认前），是 ERP→财务的一个反向钩子：财务数据（应收余额）反过来约束业务动作（能不能发货）。可配成"硬拦截"或"仅警告"。

---

## 四、销售退货红字：复用 CP6 已有的 CreditNote

CP6 **已经有 `CreditNote`**（Phase 10a，RMA 逆向冲回）。AR 的销售退货红字直接接它，不用新建——客户退货 → RMA → `CreditNote` → 生成红字 AR 发票（冲减应收）+ 红冲收入/成本凭证。这与 [03 章供应商红字](./03-accounts-payable.md#62-采购退货--供应商红字冲减应付联动-wms-rma)对称，但 AR 侧 CP6 已铺了一半路。

```
销售退货（客户退回 2,260 的货，成本 1,200）：
① 冲收入：借 主营业务收入+销项税 / 贷 应收账款
② 冲成本：借 库存商品(FG，货退回来了) / 贷 主营业务成本
```

---

## 五、它怎么嵌进 CP6

| AR 需要 | CP6 现成的 | 怎么用 |
|---|---|---|
| 出货数据 → 自动开票 | `Order`/`Outbound`/`IErpBridgeHook`（已有） | 出货确认事件挂 `FinBridgeHook` 自动开票 |
| 客户主数据 + 信用额度 | `BusinessPartner`（取引先） | 加 `CreditLimit`，出货前校验 |
| 销售退货红字 | `CreditNote`（Phase 10a 已有） | 接成 AR 红字发票 + 红冲凭证 |
| 出货取消红冲 | `OrderCancelBridgeHook`（Phase 6） | 取消 → 红冲收入/成本凭证 |
| 成本数据（结转用） | [06 章成本会计](./06-cost-accounting.md) | FG 单位成本喂给成本结转凭证 |

落点：`CP6.*/.../Fin/{ArInvoice,Receipt,ArSettlement}`、`cp6.web/src/views/fin/{ArInvoiceView,ReceiptView,ArAgingView}.vue`。

---

## 六、阶段 3 完成自检

- [ ] 确认一张出货，AR 发票自动生成了吗？同一出货单开两次被幂等挡住了吗？
- [ ] 出货同时生成了"收入确认"和"成本结转"两张凭证吗？为什么必须两张？
- [ ] 成本结转的金额从哪来？（答：06 章的 FG 单位成本）没有它怎么办？
- [ ] 超信用额度的客户，出货被拦了吗？这是"财务反向约束业务"的例子吗？
- [ ] 销售退货红字接的是 CP6 现成的 `CreditNote` 吗？
- [ ] AR 子账未收合计 == GL 应收控制科目余额吗？（与 AP 对称的勾稽）

全部能答 → 收入端闭环了。AR + AP 都通，企业的"钱进钱出"账就齐了。下一章 [06 成本会计](./06-cost-accounting.md)：你的差异化卖点——用 PaperRoll/InkLot 算出每单真实成本，也是 AR 成本结转的数据源。

---

*生成于 2026-06-10。需求基线：AR 镜像 AP / 出货自动开票复用 CP6 / 销售红字接 CreditNote / MVP 本位币。配套实现落于 `CP6.*/.../Fin`。*
