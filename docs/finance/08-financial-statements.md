# 08 · 财务报表：试算表 / 资产负债表 / 损益表

> **阶段 5。** 把账变成老板和审计要看的东西。本章讲三大报表怎么从凭证/科目余额一键生成——**报表不是另存的数据，而是科目余额的不同视角**。本章结束时，三大报表能从已过账凭证直接算出来。
>
> 上游：[01 总账](./01-gl-kernel.md)、[02 试算平衡](./02-period-close.md)。

---

## 一、题眼：报表是"算"出来的，不是"存"出来的

新手常以为报表要单独建表存数据。**错。** 三大报表全部来自同一个源——**科目余额**：

```
                  ┌─ 试算平衡表：所有科目的期初/发生/期末（02 章已做，最底层）
  科目余额  ──────┼─ 资产负债表：资产/负债/权益类科目的"期末余额"，按报表行重组
                  └─ 损益表：    收入/成本/费用类科目的"本期发生"，算出利润
```

所以做报表 = **定义"科目 → 报表行"的映射** + 把 02 章的余额按映射汇总。没有新的业务逻辑，只有归类汇总。**报表永远和总账一致**，因为它就是总账的视图。

---

## 二、资产负债表（Balance Sheet）：某一时点的家底

资产负债表回答"此刻有多少资产、欠多少、剩多少是自己的"，铁律是会计恒等式：

> **资产 = 负债 + 所有者权益**

它取**资产/负债/权益**三类科目的**期末余额**（时点数，[02 章](./02-period-close.md#23-三栏结构期初--本期发生--期末别只算本期)算的 closeBal），按报表结构重组：

```
资产                          负债与权益
  流动资产                       流动负债
    货币资金   54,000             应付账款   15,000
    应收账款   20,000             应交税费    3,000
    存货       66,000           负债合计     18,000
  非流动资产                     所有者权益
    固定资产   80,000             实收资本   180,000
                                  未分配利润  22,000
  ─────────────              ─────────────
  资产合计   220,000           负债与权益合计 220,000   ← 必须相等
```

```csharp
public async Task<BalanceSheet> BuildAsync(Guid periodId)
{
    var tb = await _trial.BuildAsync(periodId);                 // 复用 02 章试算表
    var bs = new BalanceSheet();
    foreach (var row in tb.Rows) {
        var acc = _accounts[row.Code];
        switch (acc.Type) {
            case AccountType.Asset:     bs.Assets.Add(MapLine(row)); break;
            case AccountType.Liability: bs.Liabilities.Add(MapLine(row)); break;
            case AccountType.Equity:    bs.Equity.Add(MapLine(row)); break;
            // 收入/费用类不进资产负债表（它们进损益，差额转未分配利润）
        }
    }
    bs.TotalAssets = bs.Assets.Sum(x => x.Amount);
    bs.TotalLiabEquity = bs.Liabilities.Sum(x => x.Amount) + bs.Equity.Sum(x => x.Amount)
                       + await CurrentProfitAsync(periodId);    // 本年利润并入权益
    bs.IsBalanced = bs.TotalAssets == bs.TotalLiabEquity;       // 不平=账坏，告警
    return bs;
}
```

> **为什么一定平？** 同样源于[借贷恒等](./01-gl-kernel.md#五铁律-1-落地借贷恒等校验)：资产（借方余额）= 负债+权益+利润（贷方余额）。月结未做年结时，损益类的净额（本年利润）要并进权益侧，恒等才成立——这就是 [02 章](./02-period-close.md#四年结暂缓但先理解)年结的意义在报表上的体现。

---

## 三、损益表（P&L / 利润表）：一段时间赚了多少

损益表回答"这个月/这一年赚了多少"，取**收入/成本/费用**类科目的**本期发生额**（期间数，不是时点）：

```
主营业务收入          500,000
减：主营业务成本       300,000
    ────────────
毛利               200,000
减：销售费用          40,000
    管理费用          80,000
    财务费用           5,000
    ────────────
营业利润            75,000
加：营业外收入          2,000
减：营业外支出              0
    ────────────
利润总额            77,000
减：所得税费用         19,250
    ────────────
净利润              57,750   → 年结时转入"未分配利润"
```

```csharp
public async Task<IncomeStatement> BuildAsync(Guid fromPeriod, Guid toPeriod)
{
    // 损益是"区间累计"：可跨多个期间汇总（如本年累计 = 1月~当月）
    var movement = await _trial.MovementRangeAsync(fromPeriod, toPeriod);
    var pnl = new IncomeStatement();
    pnl.Revenue   = SumByType(movement, AccountType.Revenue);   // 贷方发生
    pnl.Cost      = SumByType(movement, AccountType.Expense, role: "COGS");
    pnl.GrossProfit = pnl.Revenue - pnl.Cost;
    pnl.OpEx      = SumByType(movement, AccountType.Expense, exclude: "COGS");
    pnl.NetProfit = pnl.GrossProfit - pnl.OpEx + pnl.NonOpNet - pnl.IncomeTax;
    return pnl;
}
```

> **资产负债表用"期末余额"（时点），损益表用"本期发生"（区间）**——这是两张表最本质的区别。搞混了就会算出"本月资产 = 本月新增资产"这种错误。

---

## 四、报表模板：科目→报表行的映射

真实报表行（如"货币资金"）通常**汇总多个科目**（库存现金 + 银行存款 + 其他货币资金）。所以要一张"报表模板"配置映射，而不是硬编码：

```csharp
public class ReportLineMapping : BaseEntity
{
    public string ReportType { get; set; } = "";       // BS / PnL
    public string LineName { get; set; } = "";         // "货币资金"
    public int DisplayOrder { get; set; }
    public string AccountRoles { get; set; } = "";      // 或科目编码区间，逗号分隔
    public string? SubtotalOf { get; set; }            // 小计/合计归属
}
```

> 和 [01 章 Role 锚点](./01-gl-kernel.md#31-模板包机制)、[05 章规则即数据](./05-auto-voucher.md#一题眼把翻译规则从代码里抽出来)一脉相承：**报表结构也是配置，不是代码**。换国别模板包，报表映射跟着模板包走，报表生成逻辑零改动。

---

## 五、现金流量表（进阶，可延后）

三大报表的第三张——现金流量表（经营/投资/筹资活动现金流）比前两张复杂：它要么用"直接法"（按现金科目流水分类），要么用"间接法"（从净利润调整非现金项目倒推）。**MVP 可延后**，先交付资产负债表 + 损益表（这两张是签合同的底线），现金流量表作为 v2。

---

## 六、它怎么嵌进 CP6

| 报表需要 | CP6 现成的 | 怎么用 |
|---|---|---|
| 科目余额 | [02 章试算表](./02-period-close.md) | 三大报表全复用它 |
| 报表导出 | CSV 导出（已有，如 UnshippedOrderCsvExport） | 报表导 Excel/PDF |
| 报表看板 | Vue3 + 图表（前端已有 Dashboard 体系） | 财务看板可视化 |
| 多语言报表行 | `Sys_Lang`（5 语言已有） | 报表行名国际化 |

落点：`CP6.*/.../Fin/{BalanceSheetService,IncomeStatementService,ReportLineMapping}`、`cp6.web/src/views/fin/{BalanceSheetView,IncomeStatementView}.vue`。

---

## 七、阶段 5 完成自检

- [ ] 我能说清"报表是算出来的、不是存出来的"吗？三大报表都来自什么？
- [ ] 资产负债表用期末余额、损益表用本期发生——这个区别我清楚吗？
- [ ] 资产负债表为什么一定平？本年利润为什么要并进权益侧？
- [ ] 一个报表行汇总多个科目，靠的是硬编码还是 `ReportLineMapping` 配置？
- [ ] 换 INTL/日本模板包，报表生成逻辑要不要改？（不该改）

全部能答 → 财务输出端齐了。下一章 [09 与 CP6 集成](./09-cp6-integration.md)：把整个财务模块物理落进 CP6 工程。

---

*生成于 2026-06-10。需求基线：自建完整 GL/报表从科目余额生成/Role 映射。现金流量表延后 v2。配套实现落于 `CP6.*/.../Fin`。*
