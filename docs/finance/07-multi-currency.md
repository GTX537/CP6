# 07 · 多币种与汇兑损益：复用 FxRate，结算算汇差

> **补 MVP 延后的外币部分。** [03 章](./03-accounts-payable.md#防重录是-ap-第一道内控)把外币 AP/AR 推到了这里。本章把它补上：外币发票怎么存（原币 + 本位币双金额）、本位币怎么记账、结算时怎么算汇兑损益、期末未结余额怎么重估。复用 CP6 现成的 `FxRate`（Gap 4.3）。
>
> 上游：[01 总账](./01-gl-kernel.md)、[03 AP](./03-accounts-payable.md)/[04 AR](./04-accounts-receivable.md)。

---

## 一、多币种的一句话原则

> **欠款/付款的匹配，永远按原币；记账/报表，永远按本位币。汇率变动产生的差，叫汇兑损益。**

你欠日本供应商 ¥1,000,000，不管汇率怎么动，你**就是欠 ¥1,000,000**（原币恒定）。但你的账是人民币记的，开票那天 ¥1=0.05、付款那天 ¥1=0.048——同样的 ¥1,000,000，本位币从 50,000 变成 48,000，**差的 2,000 就是汇兑收益**（你少付了人民币）。

这就是为什么外币单据必须存**两套金额**：
- **原币金额**（`OrigAmount`）：追欠款、做核销匹配，恒定
- **本位币金额**（`Debit/Credit`）：记 GL、出报表，按交易时汇率折算

---

## 二、三个时点，三件事

外币业务的汇率，在三个时点各做一件事：

| 时点 | 用什么汇率 | 干什么 | 凭证 |
|---|---|---|---|
| **交易时**（开票） | 当日汇率，**冻结** | 原币记应收应付，本位币折算入账 | 正常 AR/AP 凭证 |
| **结算时**（收付款） | 收付款当日汇率 | 算"已实现汇兑损益"（realized） | 差额入汇兑损益科目 |
| **期末**（月结） | 期末汇率 | 对未结清的外币余额"重估"（unrealized） | 重估凭证 |

CP6 的 `FxRate` 表 + `Order` 的汇率冻结（Gap 4.3）已经把"交易时冻结"这件事做了——本章复用它，扩展到 AP/AR。

### 外币发票的双金额

```csharp
// 外币时，ApInvoice/ArInvoice 扩展（MVP 之后启用）
public class ApInvoice  // 增量字段
{
    // 既有 NetAmount/GrossAmount = 本位币（入 GL）
    public decimal OrigGrossAmount { get; set; }       // 原币含税额（追欠款、核销匹配用）
    public decimal OrigSettledAmount { get; set; }     // 原币已核销
    // CurrencyCd + FxRate 已有：开票日冻结汇率
}
```

> **关键：开账余额（未结欠款）按原币判定。** `OrigGrossAmount - OrigSettledAmount == 0` 才算付清，**不是**看本位币。本位币随汇率浮动，用它判付清会出错。

---

## 三、结算时：已实现汇兑损益

付一张外币发票，按**付款日汇率**折本位币付出，与**开票日汇率**折的应付本位币之间的差，就是已实现汇兑损益：

```
开票：欠 $10,000，开票日 1$=7.0 → 应付本位币 70,000
付款：付 $10,000，付款日 1$=6.8 → 银行付出本位币 68,000

  借  应付账款 (AP_CONTROL)   70,000   ← 按开票时冻结的本位币冲掉应付
  贷  银行存款                     68,000   ← 按付款日实付的本位币
  贷  汇兑收益 (Role=FX_GAIN)        2,000   ← 差额（少付了，收益）
```

这正是 [03 章](./03-accounts-payable.md#为什么尾差必须处理)埋的 `ApSettlement.DiffType=FxDiff` 的归属——**汇差在结算时点确实发生（realized），就在核销这里入损益**。科目用 Role `FX_GAIN`（4401）/`FX_LOSS`（6004），换国别模板包照样取得到。

```csharp
public decimal RealizedFxDiff(ApInvoice inv, Payment pay, decimal origSettled)
{
    var apBookValue   = origSettled * inv.FxRate;     // 应付侧本位币（开票冻结汇率）
    var paidBookValue = origSettled * pay.FxRate;     // 银行侧本位币（付款日汇率）
    return apBookValue - paidBookValue;               // >0 收益(FX_GAIN)，<0 损失(FX_LOSS)
}
```

---

## 四、期末：未结余额重估（unrealized）

月结时，还没付清的外币应付/应收/银行存款，要按**期末汇率**重新折算一遍，把账面调到"如果现在结算会是多少"。这叫重估（revaluation）：

```
期末仍欠 $5,000，开票日折 35,000，期末汇率 1$=6.6 → 应折 33,000
  借  应付账款（重估调整）    2,000
  贷  汇兑收益 (FX_GAIN)          2,000     ← 账面应付调低 2,000（未实现收益）
```

```csharp
public async Task RevalueAsync(Guid periodId, decimal periodEndRate)
{
    var openFx = await _db.ApInvoices
        .Where(i => i.CurrencyCd != FunctionalCurrency && i.Status != ApInvoiceStatus.Paid)
        .ToListAsync();
    foreach (var inv in openFx) {
        var origOpen   = inv.OrigGrossAmount - inv.OrigSettledAmount;
        var bookValue  = origOpen * inv.FxRate;        // 账面本位币
        var marketValue= origOpen * periodEndRate;     // 期末重估本位币
        var diff = bookValue - marketValue;
        if (diff != 0) await GenerateRevalVoucher(inv, diff, periodId);  // 自动凭证
    }
}
```

> **未实现 vs 已实现**：重估是"账面调整"（钱还没动，unrealized），下期初通常**冲回**（reverse），等真结算了再认已实现汇差——避免重复计。这是和[结算汇差](#三结算时已实现汇兑损益)的边界（[03 章 FxDiff 注释](./03-accounts-payable.md#为什么尾差必须处理)说的"时点不同、不重叠"）。MVP 不做时，外币余额就不重估；做外币时这步不能省，否则期末报表的外币资产负债是失真的。

---

## 五、它怎么嵌进 CP6

| 多币种需要 | CP6 现成的 | 怎么用 |
|---|---|---|
| 汇率表 + 交易冻结 | `FxRate` + `Order.CurrencyCd/FxRate`（Gap 4.3） | 直接复用，扩展到 AP/AR 发票 |
| 汇兑损益科目 | `Role=FX_GAIN/FX_LOSS`（01 章科目表已留） | 4401 汇兑收益 / 6004 汇兑损失 |
| 期末重估 | 月结流程（02 章）挂一步 | 结账前对未结外币余额跑 `RevalueAsync` |
| 重估自动凭证 | [05 引擎](./05-auto-voucher.md) | 重估/冲回都走自动凭证 |

---

## 六、本章自检

- [ ] 为什么外币欠款付清要看原币、不能看本位币？
- [ ] 三个时点（交易/结算/期末）各用什么汇率、各做什么？
- [ ] 结算时的汇差（已实现）和期末重估的汇差（未实现）有什么区别？为什么重估下期要冲回？
- [ ] 汇兑损益科目我用的是 Role 还是写死编码？换日本模板包还取得到吗？
- [ ] MVP 不做多币种时，少了哪一步会让外币报表失真？（答：期末重估）

全部能答 → 多币种闭环。下一章 [08 财务报表](./08-financial-statements.md)：三大报表一键从凭证生成。

---

*生成于 2026-06-10。需求基线：通用税制/多币种、复用 FxRate、Role 锚点取汇兑科目。配套实现落于 `CP6.*/.../Fin`。*
