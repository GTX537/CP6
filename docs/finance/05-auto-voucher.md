# 05 · 自动凭证引擎：业务事件 → 凭证（复用 BridgeHook）

> **贯穿全程的枢纽。** 前面几章一直说"过账时由自动凭证引擎生成凭证"——本章就是那台引擎。它监听业务事件（发票过账、付款、出货、领料），按"入账规则"翻译成借贷凭证、直接过账。**它不是新造的轮子，而是复用你 Phase 6 的 `IntegrationEvent` + `BridgeHook` + 重试/死信基建。** 本章结束时，你能讲清"出一次货，账是怎么自己记上的"。
>
> 上游：[01 总账](./01-gl-kernel.md)（凭证 + Role 锚点 + AutoPostAsync）。被依赖：[03 AP](./03-accounts-payable.md)、[04 AR](./04-accounts-receivable.md)、[06 成本](./06-cost-accounting.md) 的凭证全由本引擎生成。

---

## 一、题眼：把"翻译规则"从代码里抽出来

回到[总纲题眼](./README.md#一先记住这一句话整套书的题眼)：财务模块 = 一台"业务事件 → 凭证"的翻译机。这台翻译机最忌讳的，是把翻译规则**写死在代码里**：

```csharp
// ❌ 反面教材：规则写死在出货逻辑里
if (shipment.Confirmed) {
    var entry = new JournalEntry();
    entry.Lines.Add(new() { AccountId = HARDCODED_AR_ID, Debit = amount });   // 写死科目
    entry.Lines.Add(new() { AccountId = HARDCODED_REVENUE_ID, Credit = amount });
    // 换个客户的科目表，这段就废了；会计想调分录，得改代码重新发版
}
```

正确做法：规则是**数据**（`PostingRule` 表），引擎是**通用解释器**。会计在界面上配"出货事件 → 借应收、贷收入"，引擎运行时读规则、按 [`Role` 锚点](./01-gl-kernel.md#31-模板包机制)解析出具体科目、拼出凭证。**这和你 docs/oa 里"拖拽产出 JSON、运行时解释 JSON"是同一个心智**——规则即配置，引擎即解释器。

---

## 二、入账规则模型（PostingRule）

```csharp
// CP6.Entity/DomainModels/Fin/PostingRule.cs
public class PostingRule : BaseEntity
{
    public int TenantId { get; set; }
    public string EventType { get; set; } = "";       // 业务事件类型，如 "AP.InvoicePosted"
    public string Name { get; set; } = "";
    public bool IsActive { get; set; } = true;
    public List<PostingRuleLine> Lines { get; set; } = new();
}

public class PostingRuleLine : BaseEntity
{
    public Guid RuleId { get; set; }
    public int LineNo { get; set; }
    public PostingSide Side { get; set; }              // 借 / 贷
    public RuleLineSource Source { get; set; }          // ★固定角色行 / 单据行透传

    // —— Source=FixedRole 时用：金额取头字段、科目按角色 ——
    public string? AccountRole { get; set; }           // 按角色取科目，如 "AP_CONTROL"（换模板包零改动）
    public string? AmountField { get; set; }           // 取事件头的哪个字段做金额，如 "TaxAmount"
    public bool CarryPartner { get; set; }             // 是否带往来单位
    public bool CarryCostCenter { get; set; }          // 是否带成本中心
    public Guid? FallbackAccountId { get; set; }       // Role 取不到时的兜底科目

    // —— Source=DocumentLines 时用：把单据自己的行炸开，各进各的科目 ——
    public string? LineAccountField { get; set; }      // 单据行里哪个字段是科目，如 "ExpenseAccountId"
    public string? LineAmountField { get; set; }       // 单据行里哪个字段是金额，如 "Amount"
}
public enum PostingSide { Debit = 1, Credit = 2 }
public enum RuleLineSource { FixedRole = 0, DocumentLines = 1 }
```

一条规则 + 多行 = 一个凭证模板。例如"AP 发票过账"规则：

| Side | Source | 科目来源 | 金额来源 | CarryPartner |
|---|---|---|---|---|
| 借 | **DocumentLines** | 发票行的 `ExpenseAccountId`（各行各进各科目，带成本中心） | 发票行的 `Amount` | 否 |
| 借 | FixedRole | `TAX_INPUT` | 头 `TaxAmount` | 否 |
| 贷 | FixedRole | `AP_CONTROL` | 头 `GrossAmount` | **是** |

> 第一行是 **`DocumentLines` 透传**：一张混行发票（3 行原纸进"原材料"、1 行运费进"费用"、各带不同成本中心）会被炸成多条借方分录、各进各科目——而不是塞进一个固定"原材料"科目。税行、应付控制行则是 **`FixedRole` 固定行**，金额取发票头。这就是真实 ERP 处理异构单据的方式。

> **为什么用 `Role` 不用科目 Id？** 这是 [01 章多模板包](./01-gl-kernel.md#31-模板包机制)设计的兑现点。规则说"贷 AP_CONTROL"，引擎在当前租户的科目表里按 Role 找到那张应付科目——中国模板找到 2202、国际模板找到 2100、日本模板找到買掛金，**规则一字不改**。这就是"给美国/日本用户灵活配置"在工程上的落地。

---

## 三、引擎主干：事件 → 凭证

```csharp
// CP6.Core/Services/Fin/AutoVoucherEngine.cs
public async Task<Result> GenerateAsync(FinBizEvent evt)
{
    // ① 幂等：同一业务事件不能重复生成凭证（见第四节）
    if (await _db.JournalEntries.AnyAsync(e =>
            e.Source != VoucherSource.Manual && e.SourceDocNo == evt.SourceDocNo
            && e.Status == JournalStatus.Posted))
        return Result.Ok("凭证已存在，跳过（幂等）");

    // ② 找规则
    var rule = await _db.PostingRules
        .Include(r => r.Lines)
        .FirstOrDefaultAsync(r => r.EventType == evt.EventType && r.IsActive);
    if (rule is null) return Result.Fail($"无入账规则：{evt.EventType}");

    // ③ 按规则拼凭证
    var entry = new JournalEntry {
        Source = evt.Source,                            // AP/AR/Cost
        SourceDocNo = evt.SourceDocNo,                  // 发票号/出货号，追溯+幂等用
        VoucherDate = evt.BizDate,
        PeriodId = await _period.ResolveAsync(evt.BizDate),
        Description = evt.Description,
    };
    foreach (var rl in rule.Lines) {
        if (rl.Source == RuleLineSource.FixedRole) {
            // 固定角色行：金额取事件头字段，科目按 Role 解析
            var acc = await _accounts.ByRoleAsync(rl.AccountRole!)
                      ?? await _accounts.ByIdAsync(rl.FallbackAccountId);
            if (acc is null) return Result.Fail($"角色 {rl.AccountRole} 找不到科目");
            var amount = evt.GetAmount(rl.AmountField!);
            if (amount == 0) continue;                   // 0 额行跳过（如无税发票的税行）
            entry.Lines.Add(NewLine(rl.Side, acc.Id, amount,
                rl.CarryPartner ? evt.PartnerId : null,
                rl.CarryCostCenter ? evt.CostCenterId : null));
        }
        else {  // DocumentLines 透传：炸开单据自己的行，各进各科目，按科目+成本中心合并
            var grouped = evt.DocLines
                .GroupBy(dl => (Acc: dl.GetGuid(rl.LineAccountField!), dl.CostCenterId))
                .Select(g => (g.Key.Acc, g.Key.CostCenterId,
                              Amt: g.Sum(x => x.GetAmount(rl.LineAmountField!))));
            foreach (var (acc, cc, amt) in grouped) {
                if (amt == 0) continue;
                entry.Lines.Add(NewLine(rl.Side, acc, amt, null, cc));
            }
        }
    }

    // ④ 直过（自动凭证可信直过，见 01 章决策）
    return await _journal.AutoPostAsync(entry);          // 内含借贷恒等校验 + 锁期校验
}
```

四步：**幂等 → 找规则 → 拼凭证 → 直过**。`AutoPostAsync`（[01 章](./01-gl-kernel.md#六铁律-2-落地maker-checker-状态机--红冲)）会再过一遍借贷恒等和锁期，所以引擎拼错了也落不了库——双保险。

> 其中 `evt.DocLines` 是事件携带的**源单据行**（如发票的 `ApInvoiceLine`），`NewLine(side, accId, amt, partner, cc)` 是个按借贷方向组装 `JournalLine` 的小helper。`FinBizEvent` 把"头字段 + 单据行 + 往来单位 + 成本中心"统一打包，引擎不关心源头是 AP 发票还是出货单——**同一台引擎，喂不同事件和规则即可**。

---

## 四、幂等：自动凭证最容易出的事故

业务事件可能**重复触发**（重试、用户重复点、消息重投）。如果不防，一次出货生成两张收入凭证，账就虚增了。这是自动凭证最危险的坑。

防法：**每张自动凭证带 `SourceDocNo`（来源单据号），同一来源 + 已过账 → 跳过**（见上 ① 步）。配合数据库唯一约束兜底：

```csharp
// 迁移里加唯一索引：自动凭证的 (Source, SourceDocNo) 不重复
modelBuilder.Entity<JournalEntry>()
    .HasIndex(e => new { e.Source, e.SourceDocNo })
    .HasFilter("[Source] <> 0 AND [Status] = 2")     // 非手工 且 已过账
    .IsUnique();
```

> 你的 `IntegrationEvent`（Phase 6）**本身就是幂等设计的**——带 `CorrelationId`、重试不重复执行。自动凭证引擎挂在这套上，天然继承幂等性。这是复用 Phase 6 最大的便宜：**不用自己重造幂等/重试/死信**。

---

## 五、它怎么挂上 Phase 6 的 BridgeHook

你已有的跨模块联动是这条链（[project_closed_loop 记忆](./README.md#七它怎么嵌进-cp6你已有的便宜可占)）：业务动作 → `IntegrationEvent` 持久化 → `BridgeHook` 消费 → 重试/死信兜底。财务自动凭证就再加一个 Hook：

```csharp
// CP6.Core/Services/Fin/FinBridgeHook.cs —— 仿 IWmsBridgeHook 的写法
public class FinBridgeHook : BridgeHookBase, IFinBridgeHook
{
    // 出货确认 → 生成 AR 收入凭证（复用现有 IErpBridgeHook 出货回写那条链上挂一刀）
    public async Task OnShipmentConfirmedAsync(ShipmentConfirmedEvent e)
    {
        await PersistEventAsync(
            sourceModule: "WMS", targetModule: "FIN",
            hookName: "FinBridgeHook.OnShipmentConfirmed",
            sourceNo: e.ShipmentNo, correlationId: e.CorrelationId,
            status: "Pending", payload: e);

        var evt = FinBizEvent.FromShipment(e);          // 翻译成财务事件
        var r = await _engine.GenerateAsync(evt);
        // 成功/失败回写 IntegrationEvent，失败自动进重试→死信（Phase 6 现成）
    }

    // AP 发票过账 / 付款 / 订单取消（红冲）…… 同样挂这里
}
```

**关键：财务不内联进业务事务。** 出货该提交提交，财务凭证通过事件**异步**生成——失败了走 Phase 6 重试/死信，不阻塞业务、不污染业务事务。这是[总纲](./README.md)说的"最终一致"，也是 docs/oa 第 09 章同一个集成原则。

### 已有 Hook 的复用对照

| 财务事件 | 挂在哪条已有链上 | 生成凭证 |
|---|---|---|
| 出货确认 → AR | `IErpBridgeHook`（出库→订单回写，已有） | 借应收/贷收入 + 借成本/贷库存 |
| 订单取消 → 红冲 | `IOrderCancelBridgeHook`（Phase 6 级联，已有） | 对已生成凭证做反向红冲 |
| AP 发票/付款 | 新建 `IFinBridgeHook` | 见 [03 章三段凭证](./03-accounts-payable.md#三三段如何各自生成凭证) |
| 工单完工 → 成本 | 接 MES 完工事件 | 料工费归集结转（[06 章](./06-cost-accounting.md)） |

---

## 六、补偿：业务被撤销，凭证要红冲

业务可逆（你已有 OrderCancel 级联），凭证也要跟着逆——但凭证不能删，只能**红冲**（铁律 2）。所以补偿链是：

```
出货取消事件 → FinBridgeHook → 找到该出货生成的凭证(by SourceDocNo) → ReverseAsync 红冲
```

```csharp
public async Task OnShipmentCancelledAsync(ShipmentCancelledEvent e)
{
    var origin = await _db.JournalEntries.FirstOrDefaultAsync(x =>
        x.SourceDocNo == e.ShipmentNo && x.Status == JournalStatus.Posted);
    if (origin != null)
        await _journal.ReverseAsync(origin.Id, "SYSTEM", $"出货 {e.ShipmentNo} 取消", autoPost: true);
}
```

> 这正好复用你 Phase 6 最成熟的能力——**级联撤销的最终一致**。财务红冲不是新机制，是给已有的 OrderCancel 级联再挂一个"红冲凭证"的下游动作。
>
> **红冲跟随触发源**（[01 章 `ReverseAsync(autoPost)`](./01-gl-kernel.md#六铁律-2-落地maker-checker-状态机--红冲)）：系统触发的红冲（出货取消、付款撤销）`autoPost: true` **直接过账**，和正向自动凭证同一原则——源业务（取消）本身已审批，红冲不该再卡在人工待复核（否则原凭证已 Reversed 但红冲没过账，GL 没真红冲、子账与 GL 漂移）。只有**会计手工红冲**（发现录错）才走人工复核。

---

## 七、为什么这套设计值钱（对照业界）

| 设计选择 | 好处 | 业界印证 |
|---|---|---|
| 规则即数据（PostingRule） | 会计改分录不改代码、不发版 | SAP 的 account determination、Odoo 的会计配置都是规则驱动 |
| Role 锚点取科目 | 换国别模板包零改动 | 多账套/多准则 ERP 的标准做法 |
| 幂等 + 异步 + 死信 | 不重复记账、不阻塞业务、失败可补偿 | **复用你自己的 Phase 6，已生产验证** |
| 自动直过 + 手工复核分离 | 自动化不被人工卡死，内控只管手工凭证 | 01 章决策的兑现 |

---

## 八、本章自检

- [ ] 我能讲清"规则即数据、引擎即解释器"，并说出写死科目的坏处吗？
- [ ] 同一张出货单触发两次事件，为什么不会生成两张凭证？（幂等键是什么）
- [ ] 规则里写 `AP_CONTROL`，换成国际模板包后引擎为什么不用改？
- [ ] 自动凭证失败了，靠什么兜底？（答：Phase 6 重试 → 死信）
- [ ] 出货取消，凭证是被删除还是被红冲？补偿链怎么走？
- [ ] 为什么财务凭证不能内联进出货的数据库事务？

全部能答 → 你掌握了整个财务模块的"心脏"。有了它，[03 AP](./03-accounts-payable.md) / [04 AR](./04-accounts-receivable.md) / [06 成本](./06-cost-accounting.md) 都只是"定义不同的 PostingRule"而已——引擎一套，规则千变。

---

*生成于 2026-06-10。需求基线：自动凭证可信直过 / Role 锚点 / 复用 Phase 6 IntegrationEvent+BridgeHook。配套实现落于 `CP6.*/.../Fin`。*
