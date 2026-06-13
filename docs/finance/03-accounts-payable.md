# 03 · 应付 AP：供应商发票 → 付款 → 核销

> **阶段 2 · ★MVP 第一个落地的子账。** 本章让 CP6 第一次把"业务动作"自动变成"会计凭证"：录一张原纸/油墨的供应商发票 → 付款 → 核销，AP 子账余额自动与总账"应付账款"控制科目对上。本章结束时，你能向纸箱厂演示"欠供应商多少、付了多少、还欠多少，账自动平"。
>
> 上游：[01 总账内核](./01-gl-kernel.md)、[02 期间锁期](./02-period-close.md)。配套：[05 自动凭证引擎](./05-auto-voucher.md)（本章的凭证由它生成）。

---

## 一、AP 是什么，为什么它是 MVP

**AP（Accounts Payable，应付账款）= 我欠供应商多少钱。** 纸箱厂每天买原纸、油墨、外包加工，钱不是当场付清，而是"先收货、月底/账期再结"。这中间的"欠款"就是 AP。

选它做 MVP 的理由（[总纲](./README.md)已定）：纸箱厂**采购最频繁、欠款金额最大**，"管住欠谁多少、别漏付别重付"是老板最先要的财务能力。而且 AP 能**先以手工录发票起步**，不依赖还没建的采购模块——是阻力最小的第一块。

### AP 的三段式生命周期

```
  ① 供应商发票（ApInvoice）        ② 付款（Payment）         ③ 核销（ApSettlement）
  "供应商发来一张 10,000 的发票"   "我付出去 6,000"         "这 6,000 抵哪张发票"
   → 应付 +10,000                  → 银行 −6,000             → 该发票 已付6,000/欠4,000
```

三段都各自生成凭证、各自影响 AP 余额。把它们缝对，就是本章的全部。

---

## 二、数据模型

```csharp
// CP6.Entity/DomainModels/Fin/ApInvoice.cs —— 采购发票（头）
public class ApInvoice : BaseEntity
{
    public int TenantId { get; set; }
    public string No { get; set; } = "";              // 采购发票号（系统采番）
    public string SupplierInvoiceNo { get; set; } = "";// 供应商原始发票号（防重录的关键）
    public string SupplierId { get; set; } = "";       // → BusinessPartner（复用取引先）
    public DateTime InvoiceDate { get; set; }          // 发票日期（决定落哪个会计期间）
    public DateTime DueDate { get; set; }              // 到期日（账龄/付款计划用）

    public string CurrencyCd { get; set; } = "CNY";    // 币种（复用 FxRate）
    public decimal FxRate { get; set; } = 1m;          // 开票时冻结汇率
    public decimal NetAmount { get; set; }             // 不含税金额（本位币）
    public decimal TaxAmount { get; set; }             // 税额
    public decimal GrossAmount { get; set; }           // 含税合计 = Net + Tax

    public decimal SettledAmount { get; set; }         // 已核销（已付）金额
    public ApInvoiceStatus Status { get; set; }         // 待付/部分/已付/已红冲

    public Guid? PurchaseOrderId { get; set; }         // 预留：采购模块落地后做三单匹配，现在可空
    public Guid? JournalEntryId { get; set; }          // 过账后回填它生成的凭证（追溯）

    public List<ApInvoiceLine> Lines { get; set; } = new();
}
public enum ApInvoiceStatus { Unpaid = 0, Partial = 1, Paid = 2, Reversed = 9 }

// ApInvoiceLine —— 发票行（税在行级算，你倾向的方案）
public class ApInvoiceLine : BaseEntity
{
    public Guid InvoiceId { get; set; }
    public int LineNo { get; set; }
    public string? ItemId { get; set; }                // 物料（原纸/油墨…），可空（费用类发票无物料）
    public decimal Qty { get; set; }
    public decimal UnitPrice { get; set; }
    public decimal Amount { get; set; }                // = Qty × UnitPrice（不含税）
    public Guid? TaxCodeId { get; set; }               // 税码 → 税率（进项税）
    public decimal TaxAmount { get; set; }
    public Guid ExpenseAccountId { get; set; }         // 这行计入哪个科目（原材料/费用…）
    public Guid? CostCenterId { get; set; }            // 成本中心（机台/工序/部门，可空）
}

// BankAccount —— 银行账户主数据（独立主数据，映射到 GL 科目）
public class BankAccount : BaseEntity
{
    public int TenantId { get; set; }
    public string Code { get; set; } = "";            // 内部代号，如 "BOC-USD"
    public string Name { get; set; } = "";            // 中国银行美元户
    public string BankName { get; set; } = "";        // 开户行
    public string AccountNo { get; set; } = "";       // 银行账号
    public string CurrencyCd { get; set; } = "CNY";   // 账户币种
    public Guid GlAccountId { get; set; }             // 映射到哪个 GL 银行科目（1002 系）
    public bool IsActive { get; set; } = true;
}

// Payment —— 付款单
public class Payment : BaseEntity
{
    public int TenantId { get; set; }
    public string No { get; set; } = "";
    public string SupplierId { get; set; } = "";
    public DateTime PayDate { get; set; }
    public string CurrencyCd { get; set; } = "CNY";
    public decimal FxRate { get; set; } = 1m;
    public decimal Amount { get; set; }                // 付款金额（本位币）
    public PaymentMethod Method { get; set; }          // 现金/银行转账/票据
    public Guid BankAccountId { get; set; }            // → BankAccount 主数据（出款账户）
    public bool IsPrepayment { get; set; }             // 预付款？（先付后开票，挂预付账款）
    public decimal SettledAmount { get; set; }         // 已核销到发票的部分
    public PaymentStatus Status { get; set; }          // 正常 / 已撤销
    public Guid? JournalEntryId { get; set; }
    public List<ApSettlement> Settlements { get; set; } = new();
}
public enum PaymentMethod { Cash = 0, BankTransfer = 1, Note = 2 }
public enum PaymentStatus { Normal = 0, Reversed = 9 }

// ApSettlement —— 核销（一笔付款核销多张发票，多对多）
public class ApSettlement : BaseEntity
{
    public Guid PaymentId { get; set; }
    public Guid ApInvoiceId { get; set; }
    public decimal SettledAmount { get; set; }         // 实际付款抵这张发票多少
    public decimal DiffAmount { get; set; }            // 尾差/现金折扣金额（发票被清掉但少付的部分）
    public SettlementDiffType DiffType { get; set; }    // 尾差write-off / 现金折扣 / 汇差 / 无
    public Guid? DiffAccountId { get; set; }           // 差额写冲到哪个科目
    public DateTime SettledAt { get; set; }
}
public enum SettlementDiffType { None = 0, RoundingWriteOff = 1, CashDiscount = 2, FxDiff = 3 }
```

> **为什么核销要单独一张表（多对多）？** 现实里"一笔 10 万的付款抵了 3 张发票""一张大发票分 2 次付"都很常见。付款和发票是多对多关系，中间必须有 `ApSettlement` 这张桥表记"谁抵了谁多少"。用一对一字段硬塞会在第一个分次付款场景就崩。

> **防重录是 AP 第一道内控。** 同一张供应商发票被录两次，会虚增应付、可能重复付款——这是 AP 最高频的事故。对 `(TenantId, SupplierId, SupplierInvoiceNo)` 建唯一索引，录入时撞库即拒。`No`（系统采番）是内部主键，`SupplierInvoiceNo`（供应商原始票号）才是防重的业务键，两者都要。

> **⚠️ MVP 范围：本位币为主，外币 AP 延后到 [07 章](./07-multi-currency.md)。** `CurrencyCd/FxRate` 字段先预留，但 MVP 只跑本位币发票/付款。**外币 AP 的真正难点**是：欠款与付款匹配要按**原币**（你欠 $10,000、付 $10,000，与汇率无关），而 GL 记**本位币**——两套金额会随汇率漂移。完整方案（`ApInvoice/Payment` 加原币金额字段、原币追欠款、本位币记 GL、期末未结余额重估、结算时点汇差入损益）放 07 章，避免 MVP 踩多币种坑。本章下面所有金额按本位币口径讲。

---

## 三、三段如何各自生成凭证

这是 AP 的会计核心。每段动作过账时，由 [05 自动凭证引擎](./05-auto-voucher.md) 生成对应凭证。**科目按 [01 章的 `Role` 锚点](./01-gl-kernel.md#31-模板包机制)取，不写死编码——所以换国别模板包零改动。**

### ① 发票过账：确认欠款

```
供应商发来原纸发票，不含税 10,000，进项税 1,300，含税 11,300：

  借  原材料 (Role=INVENTORY 或行的 ExpenseAccountId)   10,000
  借  应交税费—进项税 (Role=TAX_INPUT)                    1,300
  贷  应付账款 (Role=AP_CONTROL, PartnerId=该供应商)            11,300
```

要点：贷方的"应付账款"是**控制科目**，且 `PartnerId` 必填（[01 章 `RequirePartner`](./01-gl-kernel.md#二glaccount-实体设计)）——这样才能从总账拆出"欠这个供应商多少"。借方按发票行的 `ExpenseAccountId` 走（买原材料进 1401、付运费进费用…），可带 `CostCenterId`。

#### 税码与"可抵扣"（通用税制的关键）

税做成可配置 `TaxCode`（[总纲](./README.md)：不绑国别）。但有个完整性要点：进项税**不一定能抵扣**。可抵扣的进项税挂"应交税费—进项税"（资产性质，将来抵销项税）；**不可抵扣**的（某些国别/品目，如部分招待费、特定免税业务对应进项）则**计入成本/费用**，不进税科目。

```csharp
// CP6.Entity/DomainModels/Fin/TaxCode.cs
public class TaxCode : BaseEntity
{
    public int TenantId { get; set; }
    public string Code { get; set; } = "";            // 如 "P-STD"
    public string Name { get; set; } = "";            // 标准进项税 13%
    public decimal Rate { get; set; }                 // 税率 %
    public TaxDirection Direction { get; set; }        // 进项 / 销项
    public bool Recoverable { get; set; } = true;     // ★可抵扣？不可抵扣则税额并入成本/费用行
    public bool IsActive { get; set; } = true;
}
public enum TaxDirection { Input = 1, Output = 2 }
```

```
可抵扣（Recoverable=true）：
  借  原材料 10,000 / 借 进项税 1,300 / 贷 应付 11,300

不可抵扣（Recoverable=false）：税额并入成本，没有独立税行
  借  原材料 11,300（含不可抵扣税） / 贷 应付 11,300
```

> 在 [05 自动凭证引擎](./05-auto-voucher.md)里：`TAX_INPUT` 固定行只在税码 `Recoverable=true` 时生成；不可抵扣时，税额加进 `DocumentLines` 透传的成本行金额。这样换国别只改税码配置，引擎不变。

### ② 付款过账：钱出去

```
付给供应商 6,000：

  借  应付账款 (Role=AP_CONTROL, PartnerId=该供应商)     6,000
  贷  银行存款 (Payment.BankAccountId)                       6,000
```

付款冲减应付——借应付（负债减少记借方）、贷银行（资产减少记贷方）。

### ③ 核销：把付款对到发票

核销**不产生新凭证**（钱的进出已由①②记过了），它只是**勾稽关系**：标记"这 6,000 付款抵了 INV-001 这张发票"，并更新发票的 `SettledAmount` 和 `Status`。

```csharp
// CP6.Core/Services/Fin/ApSettlementService.cs
public record Alloc(Guid InvoiceId, decimal PayAmt,        // 实付抵这张发票
                    decimal Diff, SettlementDiffType DiffType, Guid? DiffAccountId);

public async Task<Result> SettleAsync(Guid paymentId, List<Alloc> allocations)
{
    var pay = await _db.Payments.FindAsync(paymentId);
    if (allocations.Sum(a => a.PayAmt) > pay.Amount - pay.SettledAmount)
        return Result.Fail("实付核销合计超过付款可用余额");

    foreach (var a in allocations) {
        var inv = await _db.ApInvoices.FindAsync(a.InvoiceId);
        if (inv.SupplierId != pay.SupplierId) return Result.Fail("付款与发票供应商不一致");

        var cleared = a.PayAmt + a.Diff;                  // 这次清掉的发票额 = 实付 + 差额
        if (cleared > inv.GrossAmount - inv.SettledAmount + 0.01m)
            return Result.Fail($"发票 {inv.No} 核销超额");
        if (a.Diff != 0 && a.DiffAccountId is null)
            return Result.Fail("有尾差/折扣时必须指定差额科目");

        _db.ApSettlements.Add(new() {
            PaymentId = paymentId, ApInvoiceId = a.InvoiceId,
            SettledAmount = a.PayAmt, DiffAmount = a.Diff,
            DiffType = a.DiffType, DiffAccountId = a.DiffAccountId,
            SettledAt = DateTime.UtcNow });

        inv.SettledAmount += cleared;                     // 差额也算"清掉"
        inv.Status = inv.SettledAmount >= inv.GrossAmount - 0.01m
            ? ApInvoiceStatus.Paid : ApInvoiceStatus.Partial;
        pay.SettledAmount += a.PayAmt;
    }
    await _db.SaveChangesAsync();
    return Result.Ok();   // 含差额的核销由自动凭证引擎生成"差额写冲"凭证（见下）
}
```

**尾差/现金折扣的凭证**：付 9,998 清掉一张 10,000 的发票，差 2 块（现金折扣）。核销时除了①②已记的钱流，差额要单独写冲——否则应付科目挂着 2 块永远清不掉：

```
  借  应付账款 (AP_CONTROL, 该供应商)   2     ← 把剩的应付清掉
  贷  财务费用/其他收入 (DiffAccountId)      2     ← 折扣计入损益
```

> **为什么尾差必须处理？** 没有它，每次少付几分几块，应付控制科目就挂一堆清不掉的"幽灵余额"，月底 [AP↔GL 勾稽](#四铁律推论落地ap-子账--gl-勾稽)永远差几块对不平。尾差写冲是让账能收口的必需品，不是可选项。

> **`DiffType` 的边界**：MVP 本位币阶段，尾差只有 `RoundingWriteOff`（四舍五入）和 `CashDiscount`（现金折扣）两类。`FxDiff`（汇兑损益）枚举值预留给 [07 章](./07-multi-currency.md)——汇差在**结算这个时点**确实发生（付款日汇率 ≠ 开票日），就在核销这里 crystallize 入损益；而 07 章另管"期末仍未结算余额"的重估。两者时点不同、不重叠。

### 预付款（先付后开票，纳入 MVP）

纸箱厂买原纸常付定金。预付款是**先有付款、后有发票**：

```
① 付定金 5,000（IsPrepayment=true，此时无发票可核销）：
    借  预付账款 (1123, PartnerId=供应商)   5,000
    贷  银行存款                                5,000

② 原纸到货、发票来了 8,000，先冲预付再补差：
    核销时把预付的 5,000 抵到发票，剩 3,000 再正常付款
```

`Payment.IsPrepayment=true` 时，付款凭证贷银行、借**预付账款**（资产，不是冲应付）。发票来了走 `SettleAsync` 把预付款核销到发票上。这样"挂账的定金"在 `1123 预付账款` 里看得见，不会和应付混。

---

## 四、铁律推论落地：AP 子账 ↔ GL 勾稽

[总纲铁律推论](./README.md#二会计的两条铁律它们会约束你所有表结构)：**AP 子账所有未付发票之和，必须永远等于 GL"应付账款"控制科目余额。** 这是月结对账第一刀。

```csharp
// 对账校验：子账 vs 总账
public async Task<ReconResult> ReconcileApAsync(Guid periodId)
{
    // 子账侧：所有未付清发票的剩余应付
    var subLedger = await _db.ApInvoices
        .Where(i => i.Status != ApInvoiceStatus.Reversed)
        .SumAsync(i => i.GrossAmount - i.SettledAmount);

    // 总账侧：AP_CONTROL 科目的贷方余额
    var apAccount = await _accounts.ByRoleAsync("AP_CONTROL");
    var glBalance = await _trial.AccountBalanceAsync(apAccount.Id, periodId);

    return new ReconResult {
        SubLedger = subLedger,
        GlBalance = glBalance,
        IsMatched = subLedger == glBalance,         // ← 对不上 = 账坏了，告警
        Diff = subLedger - glBalance
    };
}
```

> 对不上的常见原因：手工往 AP 控制科目记了凭证（绕过子账）、并发把核销写坏了、红冲没同步子账。所以 [01 章](./01-gl-kernel.md#13-控制科目control-account--子账和总账的接缝)才强调控制科目"别手工乱记"。把这个校验做成月结检查项 + 看板红绿灯。

---

## 五、账龄：老板最关心的一张表

AP 账龄（Aging）按"到期日"把未付款分桶，回答"哪些该付了、逾期多少"：

```
供应商        未到期    1-30天    31-60天   60天以上   合计
大王制纸      50,000    20,000         0         0    70,000
东洋油墨           0    15,000     8,000         0    23,000
...
```

```csharp
public async Task<List<AgingRow>> AgingAsync(DateTime asOf)
{
    var open = await _db.ApInvoices
        .Where(i => i.Status is ApInvoiceStatus.Unpaid or ApInvoiceStatus.Partial)
        .Select(i => new { i.SupplierId, Remain = i.GrossAmount - i.SettledAmount, i.DueDate })
        .ToListAsync();

    return open.GroupBy(x => x.SupplierId).Select(g => new AgingRow {
        SupplierId = g.Key,
        NotDue   = g.Where(x => x.DueDate >= asOf).Sum(x => x.Remain),
        D1_30    = g.Where(x => Bucket(x.DueDate, asOf) is >= 1 and <= 30).Sum(x => x.Remain),
        D31_60   = g.Where(x => Bucket(x.DueDate, asOf) is >= 31 and <= 60).Sum(x => x.Remain),
        Over60   = g.Where(x => Bucket(x.DueDate, asOf) > 60).Sum(x => x.Remain),
    }).ToList();
}
```

> 逾期应付可复用 SignalR 推到看板提醒。账龄表本身**不碰会计逻辑**，纯查询——但它是 AP 模块对老板最有感的产出。

---

## 六、红冲：发票录错了怎么办

发票已过账后发现录错（金额错、供应商错），**不能改不能删**（铁律 2）。流程：
1. 对发票生成的凭证做[红冲](./01-gl-kernel.md#六铁律-2-落地maker-checker-状态机--红冲)（反向凭证）
2. 发票 `Status = Reversed`，从子账剔除
3. 若已有核销，先解除核销（释放付款）
4. 重录一张正确的发票

> 这里和你现成的 **OrderCancel 级联**（Phase 6）是同一种心智——一个动作触发跨实体的反向清理。AP 红冲也走 IntegrationEvent，保证子账/总账/核销同步回滚。

### 6.1 付款撤销（付错 / 支票退票）

付款出错（付错供应商、金额错、支票退票）要能撤销。比发票红冲多一步——**先解核销，再红冲付款凭证**：

```csharp
public async Task<Result> ReversePaymentAsync(Guid paymentId, string reason)
{
    var pay = await _db.Payments.Include(p => p.Settlements).FirstAsync(p => p.Id == paymentId);

    // ① 解开这笔付款做过的所有核销，把发票欠款还原
    foreach (var s in pay.Settlements) {
        var inv = await _db.ApInvoices.FindAsync(s.ApInvoiceId);
        inv.SettledAmount -= (s.SettledAmount + s.DiffAmount);   // 实付+差额都还原
        inv.Status = inv.SettledAmount <= 0.01m
            ? ApInvoiceStatus.Unpaid : ApInvoiceStatus.Partial;
        _db.ApSettlements.Remove(s);
    }
    // ② 红冲付款凭证（原：借应付/贷银行 → 反向）
    if (pay.JournalEntryId is { } jid)
        await _journal.ReverseAsync(jid, "SYSTEM", $"付款 {pay.No} 撤销：{reason}", autoPost: true);
    pay.SettledAmount = 0;
    pay.Status = PaymentStatus.Reversed;
    await _db.SaveChangesAsync();
    return Result.Ok();
}
```

> **顺序不能反**：先还原核销（让发票重新"欠着"）、再红冲付款（让钱"退回"）。反过来会出现发票已清但付款已冲的中间不一致态。若差额是现金折扣，撤销时折扣也要一并退回——所以 `inv.SettledAmount` 减的是 `SettledAmount + DiffAmount`。

### 6.2 采购退货 / 供应商红字（冲减应付，联动 WMS RMA）

退原纸给供应商（来料不良、多送），供应商开**红字发票**冲减应付。本质是一张"负向发票"——借贷与正向发票相反：

```
退回 2,000 原纸（含税 2,260）的供应商红字：
  借  应付账款 (AP_CONTROL, 该供应商)        2,260    ← 冲减欠款
  贷  原材料 (退回的料)                            2,000
  贷  应交税费—进项税 (转出)                         260
```

建模：`ApInvoice` 加 `IsCreditMemo` 标记（金额记负 / 或正值但方向相反），它同样进 AP 子账，余额为**负**（供应商欠我），可与后续正向发票或付款相互核销。

```csharp
public class ApInvoice : BaseEntity   // 复用同一张表
{
    // ... 既有字段 ...
    public bool IsCreditMemo { get; set; }            // 供应商红字（采购退货）
    public Guid? OriginInvoiceId { get; set; }        // 冲哪张原发票（可空，整票退/部分退）
    public Guid? RmaId { get; set; }                  // 联动 WMS RMA（退货出库单）
}
```

> **联动你现成的 WMS RMA**：退货出库（`RmaHeader/RmaDetail` 已有）确认时，发 IntegrationEvent → `FinBridgeHook` 自动生成供应商红字发票 + 冲减应付凭证。这和 [05 章](./05-auto-voucher.md)出货自动开 AR 发票是同一台引擎、对称的方向——RMA 出库（退供应商）对应 AP 红字，正常出货（发客户）对应 AR 发票。

---

## 七、它怎么嵌进 CP6

| AP 需要 | CP6 现成的 | 怎么用 |
|---|---|---|
| 供应商主数据 | `BusinessPartner`（取引先已有） | `SupplierId` 直接引用，加"默认应付科目"配置 |
| 银行账户主数据 | **新建 `BankAccount`**（映射 GL 银行科目） | 多银行/多币种出款账户，支撑日后银行对账 |
| 物料主数据 | `ProductMaster`/物料（已有） | 发票行 `ItemId` 引用 |
| 多币种 + 冻结汇率 | `FxRate`（Gap 4.3 已做） | 开票/付款冻结汇率，结算汇差见 [07 章](./07-multi-currency.md) |
| 凭证生成 | [05 自动凭证引擎](./05-auto-voucher.md) | 发票/付款过账 → 事件 → 自动凭证（直过） |
| 采番 | MES `MesSequence`（已有） | 仿 `FinSequence` 生成发票号/付款号 |
| 逾期提醒 | SignalR Hub（已有） | 账龄逾期推看板 |
| 采购三单匹配（未来） | 采购模块（**未建**） | `PurchaseOrderId` 预留，落地后开启 PO↔收货↔发票匹配 |

落点：`CP6.Entity/DomainModels/Fin/{ApInvoice,ApInvoiceLine,Payment,ApSettlement}`、`CP6.Core/Services/Fin/{ApInvoiceService,PaymentService,ApSettlementService}`、`CP6.WebApi/Controllers/Fin/{ApInvoiceController,PaymentController}`、`cp6.web/src/views/fin/{ApInvoiceView,PaymentView,ApAgingView}.vue`。

---

## 八、阶段 2 完成自检（MVP 里程碑）

- [ ] 录一张含税供应商发票，过账后生成了"借原材料+进项税 / 贷应付"的凭证吗？
- [ ] 同一张供应商发票号录第二次，被唯一约束挡住了吗？（防重录）
- [ ] 一笔付款核销两张发票，`ApSettlement` 记对了、两张发票状态都更新了吗？
- [ ] 付 9,998 清 10,000 的发票，差额走了"借应付/贷财务费用"的写冲凭证、发票变 `Paid` 了吗？
- [ ] 预付定金（`IsPrepayment`）进了"预付账款"而不是冲应付吗？发票来了能把预付核销掉吗？
- [ ] 部分付款后，发票变成 `Partial`、账龄表里剩余金额对吗？
- [ ] **AP 子账未付合计 == GL 应付控制科目余额吗？**（核心勾稽，含尾差写冲后仍平）
- [ ] 录错的发票，我是走红冲而不是删除吗？红冲后子账同步剔除了吗？
- [ ] 付款撤销时，是先解核销（发票还原欠款）再红冲付款凭证吗？顺序反了会怎样？
- [ ] 退原纸给供应商，生成的红字发票让应付变负、能与后续发票互抵吗？它和 WMS RMA 联动了吗？
- [ ] 这套凭证用 `Role` 取科目，换成 INTL 模板包后还跑得通吗？

全部能答 → **MVP 达成**：CP6 第一次有了"会自动记账的子账"。可以向纸箱厂演示完整 AP 闭环了。下一步可做 [04 应收 AR](./04-accounts-receivable.md)（对称，且能吃你现成的出货数据自动开票），或先把 [05 自动凭证引擎](./05-auto-voucher.md) 读透——本章所有凭证都靠它生成。

---

*生成于 2026-06-10。需求基线：MVP=AP / 发票手工录起步 / 税行级 / Role 锚点取科目。配套实现落于 `CP6.*/.../Fin`。*
