# 02 · 会计期间与期末结账：试算平衡 / 锁期

> **阶段 1。** 上一章让总账能记一笔、能过账。本章让这本账能"按月收口"：会计期间（`FiscalPeriod`）、试算平衡表（证明账没记歪）、月结锁期（让历史不可改）。本章结束时，一个月的凭证能结平、能锁，锁后任何人都记不进去。
>
> 上游：[01 总账内核](./01-gl-kernel.md)。下游：[03 应付 AP](./03-accounts-payable.md) 的凭证都要落在某个 Open 期间里。

---

## 一、为什么要有"会计期间"这个东西

业务系统里时间就是时间。但财务要把时间切成**一段一段的"会计期间"**（通常一个月一段），原因是：

1. **报表要按期出**——6 月的损益表，就是 6 月这段期间所有损益类凭证的汇总。没有"期间"，无从谈"这个月赚了多少"。
2. **报完要锁死**——6 月报表报出去、税报完了，6 月就**不能再改**。否则今天往 6 月补一笔，历史报表就和账对不上了 = 做假账。
3. **余额要结转**——6 月末的科目余额，是 7 月的期初余额。期间是余额滚动的刻度。

你拍板"月结起步"，所以期间粒度 = 年 + 月。

```csharp
// CP6.Entity/DomainModels/Fin/FiscalPeriod.cs
public class FiscalPeriod : BaseEntity
{
    public int TenantId { get; set; }
    public int FiscalYear { get; set; }            // 财年（可 ≠ 日历年，见下）
    public int Year { get; set; }                  // 日历年 2026
    public int Month { get; set; }                 // 日历月 1..12
    public int PeriodNo { get; set; }              // 财年内第几期（1..12）
    public DateTime PeriodStart { get; set; }      // 期间起日（含），算期初余额的分界
    public DateTime PeriodEnd { get; set; }        // 期间止日（含）
    public PeriodStatus Status { get; set; }        // Open / Closed
    public DateTime? ClosedAt { get; set; }
    public string? ClosedBy { get; set; }
}
public enum PeriodStatus { Open = 0, Closed = 1 }
```

> 一张凭证 `VoucherDate=2026-06-15`，它就归属 `Year=2026, Month=6` 这个期间。`JournalEntry.PeriodId` 在保存时按记账日期自动算出来。

### 1.1 财年起始月可配（卖给日本/美国用户的前提）

不是所有企业财年都 1 月起：**日本企业普遍 4 月起**、美国/不少企业各异。所以财年起始月必须可配（公司级设置 `FiscalYearStartMonth`，默认 1）。

```
FiscalYearStartMonth = 4（日本）：
  2026-04 → FiscalYear=2026, PeriodNo=1   （财年第 1 期）
  2026-05 → FiscalYear=2026, PeriodNo=2
  ...
  2027-03 → FiscalYear=2026, PeriodNo=12  （财年第 12 期，跨日历年）
  2027-04 → FiscalYear=2027, PeriodNo=1   （新财年）
```

> `PeriodNo`（财年内第几期）和 `FiscalYear` 让年结、年度报表按**财年**口径汇总，而非日历年。日历的 `Year/Month` 仍保留，凭证按记账日期归期不变。这条和 [01 章多国别模板包](./01-gl-kernel.md#31-模板包机制)是一套目标——**结构通用、按国别配置**。

---

## 二、试算平衡表：账有没有记歪，它一眼看穿

### 2.1 它是什么

试算平衡表（Trial Balance）把**每个科目**在某期间的"借方发生额合计""贷方发生额合计""期末余额"列成一张表。它的底部有一行总计：

> **所有科目的借方发生额总和，必须等于贷方发生额总和。**

```
科目              借方发生额    贷方发生额    期末余额(方向)
1002 银行存款        12,000        8,000      4,000 (借)
1122 应收账款        50,000       30,000     20,000 (借)
2202 应付账款        20,000       35,000     15,000 (贷)
4001 主营业务收入         0       50,000     50,000 (贷)
5001 主营业务成本    30,000            0     30,000 (借)
...
─────────────────────────────────────────
合计               112,000      112,000     ← 必须相等！
```

### 2.2 它为什么"一定平"——和借贷恒等的关系

这是本章最重要的一个洞察：

> **试算平衡表一定平，不是因为你算得准，而是因为每一张凭证都借贷相等（铁律 1）。**

每张凭证 `Σ借 = Σ贷`。把全部凭证的借方加总、贷方加总，自然 `Σ全部借 = Σ全部贷`。所以试算表**不平 = 系统有 bug**（凭证没校验住、并发写坏了、迁移漏了数据），不是会计算错。**它是你的数据完整性探针。**

### 2.3 三栏结构：期初 + 本期发生 + 期末（别只算本期）

> ⚠️ **最容易写错的地方**：余额不等于本期发生额。资产负债类科目（应收/应付/银行）的余额是**开账至今的累计**——应收账款 6 月余额，含 1~5 月留下来的欠款，绝不是只看 6 月。所以试算表必须三栏：**期初余额（截止上期末累计）+ 本期发生（仅本期借贷）+ 期末余额（=期初+本期）**。只算本期是初学者第一坑。

```csharp
// CP6.Core/Services/Fin/TrialBalanceService.cs
public async Task<TrialBalance> BuildAsync(Guid periodId)
{
    var period = await _db.FiscalPeriods.FindAsync(periodId);

    // 本期发生额（仅本期间，已过账）
    var movement = await SumByAccountAsync(l =>
        l.Entry.PeriodId == periodId && l.Entry.Status == JournalStatus.Posted);

    // 期初余额 = 开账至今、本期开始日之前的全部累计（★关键：含历史，B/S 科目靠它才对）
    var opening = await SumByAccountAsync(l =>
        l.Entry.Status == JournalStatus.Posted && l.Entry.VoucherDate < period.PeriodStart);

    var tb = new TrialBalance { PeriodId = periodId };
    foreach (var accId in opening.Keys.Union(movement.Keys)) {
        var acc = _accounts[accId];
        var (oD, oC) = opening.GetValueOrDefault(accId);
        var (mD, mC) = movement.GetValueOrDefault(accId);
        int sign = acc.NormalSide == AccountSide.Debit ? 1 : -1;
        var openBal  = sign * (oD - oC);                 // 期初余额（按科目正常方向带号）
        var closeBal = sign * ((oD + mD) - (oC + mC));   // 期末余额 = 期初 + 本期
        tb.Rows.Add(new(acc.Code, acc.Name, openBal, mD, mC, closeBal, acc.NormalSide));
    }

    // 试算"平"有两层，都由借贷恒等保证：
    tb.MovementBalanced = tb.Rows.Sum(r => r.PeriodDebit) == tb.Rows.Sum(r => r.PeriodCredit);
    tb.ClosingBalanced  = tb.Rows.Where(r => r.CloseBal > 0).Sum(r => r.CloseBal)   // 借方余额合计
                        == tb.Rows.Where(r => r.CloseBal < 0).Sum(r => -r.CloseBal);// == 贷方余额合计
    return tb;
}
```

> **两层都平，都靠借贷恒等**：①本期借方发生合计 == 本期贷方发生合计（每张凭证平 → 全部发生额平）；②期末借方余额合计 == 期末贷方余额合计（资产+费用余额 == 负债+权益+收入余额，即会计恒等式）。任一层不平就 **触发告警**（复用 `DeadLetterNotifier`/SignalR）——账坏了，比任何业务异常都严重。
>
> **损益类科目的期初**：MVP 只做月结、不做年结，所以损益类（收入/成本/费用）的"期初"是**本年累计**（年初至上月）。等 [年结](#四年结暂缓但先理解)落地后，年初损益清零，期初才归 0。月结阶段这样算是对的。

---

## 三、月结：把一个月收口、锁死

### 3.1 月结前的检查清单（Close Checklist）

锁一个期间前，系统要挡住"还没处理干净"的情况：

```csharp
public async Task<Result> PreCloseCheckAsync(Guid periodId)
{
    var p = await _db.FiscalPeriods.FindAsync(periodId);
    if (p.Status == PeriodStatus.Closed) return Result.Fail("期间已结账");

    // ① 不能有未过账的凭证（草稿/待复核）赖在这个月
    var pending = await _db.JournalEntries.CountAsync(e =>
        e.PeriodId == periodId &&
        (e.Status == JournalStatus.Draft || e.Status == JournalStatus.PendingReview));
    if (pending > 0) return Result.Fail($"还有 {pending} 张凭证未过账，不能结账");

    // ② 试算必须平
    var tb = await _trial.BuildAsync(periodId);
    if (!tb.IsBalanced) return Result.Fail("试算不平，账有问题，禁止结账");

    // ③ 上一期间必须已结（不能跳月结账）
    var prev = await _periods.PreviousAsync(periodId);
    if (prev is { Status: PeriodStatus.Open })
        return Result.Fail("上一会计期间尚未结账");

    return Result.Ok();
}
```

### 3.2 结账动作 = 锁期

月结起步阶段，"结账"本质就是**把期间状态改成 Closed**——之后任何凭证想落在这个期间都被拒。回看 [01 章 `PostAsync`](./01-gl-kernel.md#六铁律-2-落地maker-checker-状态机--红冲) 里那句 `if (!await _period.IsOpenAsync(...)) return Fail("期间已结账")`，锁就生效在那。

```csharp
public async Task<Result> CloseAsync(Guid periodId, string userId)
{
    var check = await PreCloseCheckAsync(periodId);
    if (!check.Ok) return check;

    var p = await _db.FiscalPeriods.FindAsync(periodId);
    p.Status = PeriodStatus.Closed;
    p.ClosedAt = DateTime.UtcNow;
    p.ClosedBy = userId;
    await _db.SaveChangesAsync();
    // 下一期间自动置 Open（若不存在则创建）
    await _periods.EnsureOpenAsync(p.Year, p.Month + 1);
    return Result.Ok();
}
```

> **锁错了怎么办？** 提供"反结账（reopen）"，但要高权限 + 留审计痕迹（`Sys_OperLog`）。反结账是危险动作——已报税的月份重开等于改历史，所以默认只有财务主管能做，且系统记录谁在何时重开了哪个月。

### 3.3 余额结转：期末 → 下期期初

7 月的期初余额 = 6 月的期末余额。两种实现：

| 做法 | 怎么做 | 取舍 |
|---|---|---|
| **实时滚算**（推荐 MVP） | 不存期初余额，查"开账至今所有已过账凭证"实时累计 | 简单、不会错；数据量大时慢 |
| **结转快照** | 月结时把各科目期末余额写一张 `PeriodBalance` 快照表 | 查询快；要维护快照一致性 |

MVP 用实时滚算，等凭证量大了（百万行级）再加 `PeriodBalance` 快照做加速。**别过早优化**。

---

## 四、年结（暂缓，但先理解）

你选了"月结起步"，年结延后。但理解它，月结才完整：

- **月结**：只锁期，损益类科目余额**继续累计**（6 月收入 + 7 月收入 = 年累计收入）。
- **年结**：年末要把**损益类科目（收入/成本/费用）清零**，差额（净利润）结转到权益类"本年利润 → 未分配利润"。这样下一年损益从 0 开始。

```
年结结转分录（概念，阶段 5/年结时才做）：
  借  主营业务收入  500,000
  贷  本年利润           500,000     ← 收入类清零
  借  本年利润    420,000
  贷  主营业务成本       300,000     ← 成本/费用类清零
  贷  管理费用           120,000
  → 本年利润净额 80,000 再转入"未分配利润"
```

> MVP 不实现年结，但**科目表里 `3103 本年利润`/`3104 未分配利润` 已经预留**（见 01 章 3.2），到时直接用。

---

## 五、它怎么嵌进 CP6

| 本章用到 | CP6 现成的 | 怎么接 |
|---|---|---|
| 试算不平告警 | `DeadLetterNotifier` + SignalR（Phase 6/已有） | `IsBalanced=false` 推告警到财务看板 |
| 结账/反结账留痕 | `Sys_OperLog` | 谁在何时结/反结哪个月，全程审计 |
| 定时提醒月结 | `BackgroundServices`（已有多个 HostedService） | 月初自动提醒"上月待结账"，可选 |
| 权限分级 | `Sys_Role` RBAC | 结账/反结账设独立高权限点 |

落点：`CP6.Core/Services/Fin/{FiscalPeriodService, TrialBalanceService}`、`CP6.WebApi/Controllers/Fin/PeriodController`、`cp6.web/src/views/fin/PeriodCloseView.vue`、`TrialBalanceView.vue`。

---

## 六、阶段 1 完成自检

- [ ] 我能讲清"为什么试算平衡表一定平"，并知道它不平意味着什么吗？
- [ ] 一张凭证想落在已结账的 6 月，被拒绝了吗？
- [ ] 6 月还有一张待复核凭证时，结账被挡住了吗？
- [ ] 跳过 5 月直接结 6 月，被挡住了吗？
- [ ] 反结账有没有限高权限 + 留审计痕迹？
- [ ] 我知道"月结只锁期、年结才清损益"的区别吗？

全部能答 → 这本账能按月收口了。下一章 [03 应付 AP](./03-accounts-payable.md)：MVP 主角，供应商发票 → 付款 → 核销，第一次让"业务"自动变成"凭证"。

---

*生成于 2026-06-10。需求基线：月结起步。配套实现落于 `CP6.*/.../Fin`。*
