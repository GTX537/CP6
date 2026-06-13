# 01 · 总账内核：科目表 + 凭证 + 借贷恒等

> **阶段 0 · 从这里入门。** 本章把整个财务模块的地基浇出来：会计科目表（`GlAccount`）、记账凭证（`JournalEntry` + `JournalLine`）、借贷恒等校验、以及强制双人复核（maker-checker）的凭证状态机。本章结束时，你能手工录一张平衡凭证、存进去、查出来，不平衡的被拒绝、过账后改不动。
>
> 上游：[总纲](./README.md) 的两条铁律。下游：[02 期末结账](./02-period-close.md)、[03 应付 AP](./03-accounts-payable.md) 的凭证都从这里长出来。

---

## 一、科目表：会计的"字典"

### 1.1 什么是科目

会计不记"出了一次货"，它记"哪个**科目**增加了、哪个**科目**减少了"。科目（Account）就是钱的分类抽屉：

- **应收账款**（客户欠我的钱）
- **库存商品**（仓库里的成品价值）
- **主营业务收入**（卖货赚的）
- **应付账款**（我欠供应商的）

一个企业的全部抽屉，列成一张表，就是**科目表（Chart of Accounts, CoA）**。它是财务的"字典"——所有凭证都只能引用这张表里的科目。

### 1.2 五大类 + 借贷方向（这是会计的"物理定律"）

每个科目属于五大类之一，**每一类有固定的"增加方向"**（NormalSide）。这不是约定，是复式记账的数学结构：

| 大类 | 英文 | 增加记哪方 | 减少记哪方 | 例子 |
|---|---|---|---|---|
| **资产** Asset | Asset | **借** | 贷 | 现金、应收、库存、设备 |
| **负债** Liability | Liability | **贷** | 借 | 应付、借款、预收 |
| **权益** Equity | Equity | **贷** | 借 | 实收资本、未分配利润 |
| **收入** Revenue | Revenue | **贷** | 借 | 主营收入、其他收入 |
| **费用/成本** Expense | Expense | **借** | 贷 | 主营成本、管理费用 |

记忆口诀：**资产费用借方增，负债权益收入贷方增**。会计恒等式 `资产 = 负债 + 权益` 的两边永远靠这套方向自动平衡——这就是"借贷恒等"的根。

> **为什么要存 `NormalSide`？** 因为做报表和算余额时要用："这个科目余额 = 借方合计 − 贷方合计（资产/费用类）"还是"贷方合计 − 借方合计（负债/权益/收入类）"，取决于它的正常方向。把它存进 `GlAccount`，余额计算就不用 if-else 满天飞。

### 1.3 控制科目（Control Account）—— 子账和总账的接缝

有几个科目很特殊，它们的余额**不允许直接记凭证**，而是由子账汇总而来：

- **应付账款**：余额 = AP 子账里所有供应商未付发票之和
- **应收账款**：余额 = AR 子账里所有客户未收发票之和
- **库存商品 / 在制品**：余额 = 库存/成本子账汇总

这些叫**控制科目（`IsControl = true`）**。铁律推论就在这里落地：**控制科目的总账余额，必须永远等于对应子账的合计**。月结对账第一件事就是对它们。所以 `GlAccount` 要有个 `IsControl` 标记，提醒"这个科目别手工乱记，它归子账管"。

---

## 二、GlAccount 实体设计

```csharp
// CP6.Entity/DomainModels/Fin/GlAccount.cs
namespace CP6.Entity.DomainModels.Fin;

/// <summary>会计科目（Chart of Accounts 的一行）</summary>
public class GlAccount : BaseEntity   // 复用 CP6 现有 BaseEntity（Id/CreateTime/...）
{
    public int TenantId { get; set; }            // 多租户预留（即使现在单租户）

    public string Code { get; set; } = "";       // 科目编码，如 "1122"，全局唯一
    public string Name { get; set; } = "";        // 科目名称，如 "应收账款"
    public AccountType Type { get; set; }         // 资产/负债/权益/收入/费用
    public AccountSide NormalSide { get; set; }    // 借 / 贷（增加方向）

    public Guid? ParentId { get; set; }           // 树形：上级科目（一级/明细）
    public int Level { get; set; }                // 层级，1=一级科目
    public bool IsLeaf { get; set; }              // 是否末级（只有末级能记凭证）

    public bool IsControl { get; set; }           // 控制科目？（余额由子账勾稽）
    public string? SubLedgerType { get; set; }    // 控制科目对应的子账："AP"/"AR"/"INV"/"COST"
    public bool RequirePartner { get; set; }      // 记账时是否必须带往来单位（应收应付为 true）

    public string? Role { get; set; }             // ★跨模板恒定的角色锚点，如 "AP_CONTROL"/"REVENUE"/"COGS"
                                                  //   自动凭证按 Role 找科目，换国别模板包零改动（见 3.1）
    public string StandardScheme { get; set; } = "CN-GAAP";  // 来自哪套模板包：CN-GAAP/INTL/JP/US-GAAP

    public bool IsActive { get; set; } = true;     // 停用的科目不能再记新凭证
    public string? CurrencyCd { get; set; }        // 外币专户可锁币种，null=本位币
}

public enum AccountType { Asset = 1, Liability = 2, Equity = 3, Revenue = 4, Expense = 5 }
public enum AccountSide { Debit = 1, Credit = 2 }   // 借 / 贷
```

**几个不踩坑的设计点：**

- **只有末级科目（`IsLeaf`）能记凭证。** 上级科目是汇总用的，往它身上记账会导致明细对不上。保存凭证行时校验 `account.IsLeaf == true`。
- **`RequirePartner`**：应收/应付科目记账必须知道"是哪个客户/供应商"，否则子账拆不出来。这个标记让校验有据可依。
- **科目不能删，只能停用（`IsActive=false`）。** 已经有凭证引用的科目删了会成孤儿。和凭证一样，财务里"删"基本是禁词。

---

## 三、默认科目表模板（多国别/准则模板包）

> 你拍板"按国别灵活配置"：默认科目表做成**多套模板包**，部署时按客户所在准则选一套导入，之后可停用/增补。模板包之间**科目结构（五大类 + 借贷方向 + 控制科目）完全一致，只有编码体系和命名不同**——这正是会计的普适性。

### 3.1 模板包机制

每套模板包是一个独立 seed（科目编码、名称、本地化命名不同），用 `StandardScheme` 标识，挂在系统设置上：

| 模板包 | `StandardScheme` | 编码风格 | 目标用户 | 状态 |
|---|---|---|---|---|
| 中国企业会计准则 | `CN-GAAP` | 1122/2202/2221（准则标准码） | 中文区企业 | ✅ 本章 3.2 给全量 |
| 国际通用 | `INTL` | 1000–6999 纯区间码 | 国际/不绑准则 | ✅ 本章 3.3 给映射 |
| 日本（中小企业） | `JP` | 売掛金/買掛金，3–4 位日式码 | 日本用户 | ⏳ 模板包待编（结构同，换码+日文名） |
| 美国 US GAAP | `US-GAAP` | 1000s 区间，英文名 | 美国用户 | ⏳ 模板包待编 |

> **关键**：控制科目的"角色"（`SubLedgerType=AP/AR/INV/COST`）跨模板恒定。AP 自动凭证只认"应付控制科目"这个**角色**，不认具体编码——所以换模板包，自动凭证引擎（05 章）零改动。实现上：`GlAccount` 加一个 `Role` 字段（如 `AP_CONTROL`/`AR_CONTROL`/`TAX_INPUT`/`TAX_OUTPUT`/`COGS`/`REVENUE`），凭证规则按 Role 找科目，而非硬编码编码。

### 3.2 中国企业会计准则模板（`CN-GAAP`，~70 科目，全量）

> 编码用《企业会计准则》标准码：1=资产 2=负债 3=权益 4=收入 5=成本 6=费用。**★ 是控制科目；"子账"列即该控制科目的角色锚点（跨模板恒定），完整 `Role` 映射见 [3.3](#33-国际通用区间码模板intl-关键科目映射)。**

### 1 资产类（Asset · 借方增）

| 编码 | 科目名称 | 末级 | 控制 | 子账 | 说明 |
|---|---|---|---|---|---|
| 1000 | 流动资产 | 否 | | | 一级汇总 |
| 1001 | 库存现金 | 是 | | | |
| 1002 | 银行存款 | 是 | | | |
| 1012 | 其他货币资金 | 是 | | | |
| 1101 | 交易性金融资产 | 是 | | | |
| 1122 | **应收账款** | 是 | ★ | AR | 客户欠款，子账勾稽 |
| 1123 | 预付账款 | 是 | | | 预付供应商 |
| 1131 | 应收票据 | 是 | | | |
| 1221 | 其他应收款 | 是 | | | |
| 1231 | 坏账准备 | 是 | | | 备抵，贷方余额 |
| 1401 | 原材料 | 是 | ★ | INV | 原纸/油墨等，库存子账 |
| 1402 | 在途物资 | 是 | | | 已付未到 |
| 1403 | 周转材料 | 是 | | | 版型/托盘等 |
| 1411 | **在制品 WIP** | 是 | ★ | COST | 生产中归集的料工费 |
| 1412 | **库存商品 FG** | 是 | ★ | INV | 完工成品 |
| 1471 | 存货跌价准备 | 是 | | | 备抵 |
| 1601 | 固定资产 | 是 | | | 机器设备 |
| 1602 | 累计折旧 | 是 | | | 备抵，贷方余额 |
| 1701 | 无形资产 | 是 | | | |

### 2 负债类（Liability · 贷方增）

| 编码 | 科目名称 | 末级 | 控制 | 子账 | 说明 |
|---|---|---|---|---|---|
| 2000 | 流动负债 | 否 | | | 一级汇总 |
| 2001 | 短期借款 | 是 | | | |
| 2202 | **应付账款** | 是 | ★ | AP | 欠供应商，子账勾稽（**MVP 主角**） |
| 2203 | 预收账款 | 是 | | | 客户预付 |
| 2211 | 应付职工薪酬 | 是 | | | |
| 2221 | 应交税费 | 否 | | | 一级 |
| 2221.01 | 应交税费—进项税 | 是 | | | 采购可抵扣（AP 用） |
| 2221.02 | 应交税费—销项税 | 是 | | | 销售收取（AR 用） |
| 2221.03 | 应交税费—应交所得税 | 是 | | | |
| 2241 | 其他应付款 | 是 | | | |
| 2231 | 应付票据 | 是 | | | |
| 2501 | 长期借款 | 是 | | | |

### 3 权益类（Equity · 贷方增）

| 编码 | 科目名称 | 末级 | 说明 |
|---|---|---|---|
| 3001 | 实收资本 | 是 | |
| 3002 | 资本公积 | 是 | |
| 3101 | 盈余公积 | 是 | |
| 3103 | 本年利润 | 是 | 年结时损益结转到此 |
| 3104 | 利润分配—未分配利润 | 是 | 年结最终归宿 |

### 4 收入类（Revenue · 贷方增）

| 编码 | 科目名称 | 末级 | 说明 |
|---|---|---|---|
| 4001 | 主营业务收入 | 是 | 卖纸箱的收入（AR 开票生成） |
| 4051 | 其他业务收入 | 是 | 废纸/边角料销售等 |
| 4301 | 营业外收入 | 是 | |
| 4401 | 汇兑收益 | 是 | 多币种结算汇差（见 07 章） |

### 5 成本类（Cost · 借方增）

| 编码 | 科目名称 | 末级 | 说明 |
|---|---|---|---|
| 5001 | 主营业务成本 | 是 | 出货结转成本（料工费） |
| 5051 | 其他业务成本 | 是 | |
| 5101 | 制造费用 | 否 | 一级，归集后转 WIP |
| 5101.01 | 制造费用—折旧 | 是 | |
| 5101.02 | 制造费用—水电 | 是 | |
| 5101.03 | 制造费用—间接人工 | 是 | |
| 5201 | 生产成本—直接材料 | 是 | 工单领料（PaperRoll/InkLot） |
| 5202 | 生产成本—直接人工 | 是 | 工时 × 费率 |
| 5203 | 生产成本—制造费用 | 是 | 分摊 |

### 6 费用类（Expense · 借方增）

| 编码 | 科目名称 | 末级 | 说明 |
|---|---|---|---|
| 6001 | 销售费用 | 是 | |
| 6002 | 管理费用 | 是 | |
| 6003 | 财务费用 | 是 | 利息等 |
| 6004 | 财务费用—汇兑损失 | 是 | 多币种结算汇差（见 07 章） |
| 6601 | 研发费用 | 是 | |
| 6801 | 所得税费用 | 是 | |

### 3.3 国际通用区间码模板（`INTL`）—— 关键科目映射

> 纯区间码（1000–6999），不绑任何国家准则，英文/中文双名。结构与 CN-GAAP 一一对应，只换码。下表给关键科目（尤其控制科目）的映射，全量按此规则铺开：

| Role 角色锚点 | CN-GAAP 码 | INTL 码 | 英文名 | 大类 |
|---|---|---|---|---|
| — | 1001 | 1010 | Cash on Hand | Asset |
| — | 1002 | 1020 | Bank | Asset |
| **AR_CONTROL** ★ | 1122 | 1100 | Accounts Receivable | Asset |
| INVENTORY ★ | 1401 | 1300 | Raw Materials | Asset |
| WIP ★ | 1411 | 1340 | Work in Progress | Asset |
| FG ★ | 1412 | 1350 | Finished Goods | Asset |
| **AP_CONTROL** ★ | 2202 | 2100 | Accounts Payable | Liability |
| TAX_INPUT | 2221.01 | 2210 | Input Tax (VAT recoverable) | Liability |
| TAX_OUTPUT | 2221.02 | 2220 | Output Tax (VAT payable) | Liability |
| EQUITY_CAPITAL | 3001 | 3000 | Share Capital | Equity |
| RETAINED_EARNINGS | 3104 | 3300 | Retained Earnings | Equity |
| REVENUE | 4001 | 4000 | Sales Revenue | Revenue |
| FX_GAIN | 4401 | 4900 | FX Gain | Revenue |
| COGS | 5001 | 5000 | Cost of Goods Sold | Cost |
| DIRECT_MATERIAL | 5201 | 5100 | Direct Material | Cost |
| DIRECT_LABOR | 5202 | 5200 | Direct Labor | Cost |
| MFG_OVERHEAD | 5203 | 5300 | Manufacturing Overhead | Cost |
| FX_LOSS | 6004 | 6900 | FX Loss | Expense |

### 3.4 日本 / 美国模板包（路线图）

结构同上，仅换编码与本地化名称（如日本 `売掛金`/`買掛金`、美国英文名 + US GAAP 区间）。**这两套作为模板包后续编写**，不阻塞 MVP——客户也可先导入 INTL 包再改名。

> **全量实现方式**：每个模板包做成一个 seed 脚本（参考你现有的 `docs/*-i18n-seed.sql` 风格），如 `fin-coa-cn-gaap-seed.sql`、`fin-coa-intl-seed.sql`，部署时按 `StandardScheme` 选一套一键导入。自动凭证引擎只认 `Role`，所以换模板包它零改动。

---

## 四、凭证：JournalEntry + JournalLine

### 4.1 一张凭证的结构

凭证是"头-行"结构：一个头（`JournalEntry`）挂多条分录行（`JournalLine`），每行记一个科目的借或贷。**整张凭证借方合计必须等于贷方合计。**

```csharp
// CP6.Entity/DomainModels/Fin/JournalEntry.cs
public class JournalEntry : BaseEntity
{
    public int TenantId { get; set; }

    public string No { get; set; } = "";          // 凭证号，如 "GL-2026-06-00012"，按期间采番
    public DateTime VoucherDate { get; set; }      // 记账日期（决定落在哪个会计期间）
    public Guid PeriodId { get; set; }             // 所属会计期间（见 02 章）

    public VoucherSource Source { get; set; }      // 来源：手工/AP/AR/成本/结转/红冲
    public string? SourceDocNo { get; set; }       // 来源单据号（如 AP 发票号），便于追溯

    public JournalStatus Status { get; set; }      // 状态机，见下
    public string Description { get; set; } = "";   // 摘要

    // —— maker-checker（你拍板：强制双人复核）——
    public string MakerId { get; set; } = "";      // 制单人
    public DateTime MakerAt { get; set; }
    public string? CheckerId { get; set; }         // 过账人，过账时填，且必须 ≠ MakerId
    public DateTime? CheckerAt { get; set; }
    public string? RejectReason { get; set; }       // 驳回原因
    public bool AutoPosted { get; set; }           // 自动凭证可信直过（CheckerId 记 "SYSTEM"）

    // —— 红冲（凭证不可改不可删，只能红冲）——
    public Guid? ReversedById { get; set; }        // 本凭证被哪张红冲凭证冲掉
    public Guid? ReverseOfId { get; set; }         // 本凭证是哪张原凭证的红冲

    public List<JournalLine> Lines { get; set; } = new();
}

public enum VoucherSource { Manual = 0, AP = 1, AR = 2, Cost = 3, Carryover = 4, Reversal = 5 }
public enum JournalStatus { Draft = 0, PendingReview = 1, Posted = 2, Rejected = 3, Reversed = 4 }
```

```csharp
// CP6.Entity/DomainModels/Fin/JournalLine.cs
public class JournalLine : BaseEntity
{
    public Guid EntryId { get; set; }              // 所属凭证头
    public int LineNo { get; set; }
    public Guid AccountId { get; set; }            // 科目（必须是末级、启用）
    public decimal Debit { get; set; }             // 借方金额（本位币），与 Credit 互斥
    public decimal Credit { get; set; }            // 贷方金额（本位币）

    public string? PartnerId { get; set; }         // 往来单位（应收应付科目必填）
    public string? CostObjectType { get; set; }    // 成本对象类型：工单/订单
    public string? CostObjectId { get; set; }      // 成本对象 Id（成本归集用）

    // —— 成本中心/分析维度（现在就加，MVP 可先不填；回填历史极贵，故先占位）——
    public Guid? CostCenterId { get; set; }        // 机台/工序/部门，分析性会计维度

    // —— 多币种（见 07 章，本位币始终用 Debit/Credit）——
    public string? CurrencyCd { get; set; }        // 原币种，null=本位币
    public decimal? FxRate { get; set; }           // 冻结汇率
    public decimal? OrigAmount { get; set; }       // 原币金额

    public string? Memo { get; set; }
}
```

> **关键约束：金额永远存"本位币"在 `Debit/Credit`。** 原币信息单独存。这样所有报表、试算平衡都在本位币口径算，多币种只是附加信息。这是国际 ERP 的标准做法（Odoo 的 `debit/credit` 也是公司本位币）。

### 4.1.1 成本中心：分析性会计维度（现在就占位）

`CostCenterId` 是一条"分析维度"——它不影响借贷恒等，但让你能按**机台 / 工序 / 部门**切费用，回答"哪台印刷机最烧钱、哪个工序废料成本最高"。这是纸箱厂成本会计的差异化卖点（[06 章](./06-cost-accounting.md) 重度使用）。**MVP 阶段可以先不填**，但维度字段现在就加——否则等几万行凭证落地后再加，要回填历史，极贵。

```csharp
// CP6.Entity/DomainModels/Fin/CostCenter.cs
public class CostCenter : BaseEntity
{
    public int TenantId { get; set; }
    public string Code { get; set; } = "";        // 如 "PRT-01"（印刷机1号）
    public string Name { get; set; } = "";
    public CostCenterType Type { get; set; }       // 机台 / 工序 / 部门
    public Guid? ParentId { get; set; }            // 树形：部门 > 工序 > 机台
    public string? LinkMachineId { get; set; }     // 可关联 MES 的 Machine（机台直接对上）
    public bool IsActive { get; set; } = true;
}
public enum CostCenterType { Department = 1, Process = 2, Machine = 3 }
```

> 复用便宜：CP6 的 MES 已有 `Machine` 实体。机台型成本中心可以直接 `LinkMachineId` 挂上去，OEE/停机数据和成本归集天然对齐。

### 4.2 借方/贷方为什么分两列，不用一列正负？

新手常想"用一个 `Amount`，借正贷负不就行了？"——**不行**，原因：

1. 会计报表要分别列示"借方发生额""贷方发生额"，合一列就拆不出来。
2. 借贷恒等校验 `Σ借 = Σ贷` 比"和为零"更能挡住错误（一行借 100、一行贷 −100 在"和为零"下也成立，但那是错的记法）。
3. 全世界的总账（SAP/Odoo/用友）都是借贷双列。跟标准走，别自作聪明。

约束：**每行 `Debit` 和 `Credit` 必有且仅有一个 > 0**，另一个为 0。

---

## 五、铁律 1 落地：借贷恒等校验

凭证保存（或提交复核）前，强制校验。这是**不可绕过**的关口：

```csharp
// CP6.Core/Services/Fin/JournalEntryService.cs
public Result ValidateBalance(JournalEntry e)
{
    if (e.Lines.Count < 2)
        return Result.Fail("凭证至少要有借贷两行");

    foreach (var ln in e.Lines)
    {
        if (ln.Debit < 0 || ln.Credit < 0)
            return Result.Fail($"行 {ln.LineNo} 金额不能为负");
        if ((ln.Debit > 0) == (ln.Credit > 0))   // 同时>0 或 同时=0 都不行
            return Result.Fail($"行 {ln.LineNo} 必须借贷二选一");

        var acc = _accounts[ln.AccountId];
        if (!acc.IsLeaf)   return Result.Fail($"科目 {acc.Code} 非末级，不能记账");
        if (!acc.IsActive) return Result.Fail($"科目 {acc.Code} 已停用");
        if (acc.RequirePartner && string.IsNullOrEmpty(ln.PartnerId))
            return Result.Fail($"科目 {acc.Code} 必须指定往来单位");
    }

    var totalDebit  = e.Lines.Sum(l => l.Debit);
    var totalCredit = e.Lines.Sum(l => l.Credit);
    if (totalDebit != totalCredit)            // ← 借贷恒等，铁律 1
        return Result.Fail($"借贷不平：借 {totalDebit} ≠ 贷 {totalCredit}");

    return Result.Ok();
}
```

> **用 `decimal` 不用 `double`！** 钱永远 `decimal`，`double` 的浮点误差会让"借贷相等"在 0.01 上对不上，财务零容忍。CP6 现有金额字段（如 `Order` 的单价）已经是 decimal，沿用。

---

## 六、铁律 2 落地：maker-checker 状态机 + 红冲

你拍板"手工凭证强制复核、自动凭证可信直过"，所以凭证按来源分**两条路**：

- **手工凭证（`Source=Manual`）** → 强制走"制单 → 复核过账"的双人流水线，过账人必须 ≠ 制单人。
- **自动凭证（`Source=AP/AR/Cost`，由出货/付款等已审批的业务事件生成）** → 系统即制单人，**直接过账**（`AutoPosted=true`，`CheckerId="SYSTEM"`），不需逐张人工复核——因为源头业务单据本身已经过了业务审批。这样 [05 章自动凭证引擎](./05-auto-voucher.md) 才能真正自动化，不被人工卡死。
- **批量复核开关**（可选）：若某客户内控要求自动凭证也要人审，提供一个配置项让自动凭证落 `PendingReview`、财务每日批量过账。默认关闭。

手工凭证的双人流水线如下：

```
        ┌─────────┐  提交复核   ┌──────────────┐
  录入→ │  Draft  │ ─────────→ │ PendingReview │
        │  草稿   │            │   待复核      │
        └─────────┘            └──────┬───────┘
             ↑                  过账   │   驳回
             │  (修改后重提)    (≠制单人)│ ┌──────────┐
             └──────────────────────────┼→│ Rejected │
                                        ↓ └──────────┘
                                 ┌──────────┐  红冲   ┌──────────┐
                                 │  Posted  │ ──────→ │ Reversed │
                                 │  已过账  │         │  已红冲  │
                                 └──────────┘         └──────────┘
```

**关键规则（代码里都要挡）：**

| 动作 | 前置状态 | 规则 |
|---|---|---|
| 提交复核 | Draft | 先过借贷恒等校验 |
| 过账 Post | PendingReview | **`CheckerId` 必须 ≠ `MakerId`**（双人）；期间必须 Open；过账后写控制科目余额 |
| 驳回 Reject | PendingReview | 回 Draft（或独立 Rejected 态），填驳回原因 |
| 红冲 Reverse | Posted | 生成一张借贷对调的新凭证，原凭证 → Reversed |

```csharp
public async Task<Result> PostAsync(Guid entryId, string checkerId)
{
    var e = await _db.JournalEntries.Include(x => x.Lines)
                     .FirstAsync(x => x.Id == entryId);

    if (e.Status != JournalStatus.PendingReview)
        return Result.Fail("只有待复核凭证能过账");
    if (e.MakerId == checkerId)                       // ← maker-checker 铁则
        return Result.Fail("过账人不能是制单人，需双人复核");
    if (!await _period.IsOpenAsync(e.PeriodId))        // ← 锁期保护（见 02 章）
        return Result.Fail("该会计期间已结账，不能过账");

    var balance = ValidateBalance(e);                  // 过账前再校一次借贷恒等
    if (!balance.Ok) return balance;

    e.Status = JournalStatus.Posted;
    e.CheckerId = checkerId;
    e.CheckerAt = DateTime.UtcNow;
    await _db.SaveChangesAsync();
    // 注意：没有 Update/Delete 接口。Posted 之后这张凭证就冻结了。
    return Result.Ok();
}

/// 自动凭证直过：仅供自动凭证引擎（05章）调用，源头是已审批的业务事件
public async Task<Result> AutoPostAsync(JournalEntry e)
{
    if (e.Source == VoucherSource.Manual)
        return Result.Fail("手工凭证不能直过，必须走双人复核");
    var balance = ValidateBalance(e);
    if (!balance.Ok) return balance;
    if (!await _period.IsOpenAsync(e.PeriodId))
        return Result.Fail("该会计期间已结账");

    e.Status = JournalStatus.Posted;
    e.AutoPosted = true;
    e.MakerId = "SYSTEM";
    e.CheckerId = "SYSTEM";
    e.MakerAt = e.CheckerAt = DateTime.UtcNow;
    _db.JournalEntries.Add(e);
    await _db.SaveChangesAsync();
    return Result.Ok();
}

/// 红冲：不改原凭证，生成一张金额对调的反向凭证。
/// autoPost=true（系统触发，如出货取消/付款撤销）→ 红冲直过；false（手工红冲）→ 待复核。
public async Task<Result> ReverseAsync(Guid entryId, string makerId, string reason, bool autoPost = false)
{
    var origin = await _db.JournalEntries.Include(x => x.Lines)
                          .FirstAsync(x => x.Id == entryId);
    if (origin.Status != JournalStatus.Posted)
        return Result.Fail("只有已过账凭证能红冲");

    var reversal = new JournalEntry {
        Source = VoucherSource.Reversal,
        ReverseOfId = origin.Id,
        Description = $"红冲 {origin.No}：{reason}",
        // ★ 红冲跟随触发源：系统触发自动过账，手工触发走复核（与正向凭证同一原则）
        Status   = autoPost ? JournalStatus.Posted : JournalStatus.PendingReview,
        AutoPosted = autoPost,
        MakerId  = makerId, MakerAt = DateTime.UtcNow,
        CheckerId = autoPost ? "SYSTEM" : null,
        CheckerAt = autoPost ? DateTime.UtcNow : null,
        Lines = origin.Lines.Select(l => new JournalLine {
            AccountId = l.AccountId,
            Debit  = l.Credit,    // ← 借贷对调
            Credit = l.Debit,
            PartnerId = l.PartnerId, CostObjectId = l.CostObjectId, CostCenterId = l.CostCenterId,
        }).ToList()
    };
    origin.ReversedById = reversal.Id;
    origin.Status = JournalStatus.Reversed;
    _db.JournalEntries.Add(reversal);
    await _db.SaveChangesAsync();
    return Result.Ok();
}
```

> **为什么红冲而不是直接删？** 假设你 6 月记错一笔、7 月发现。直接删，6 月的账就变了——可 6 月报表已经报出去、已经报税了，改历史 = 做假账。红冲是在**7 月**做一笔反向分录，6 月报表不动，审计能看到"错了→冲了→改了"的完整轨迹。**这是财务能被信任的根本，不是技术洁癖。**

---

## 七、它怎么嵌进 CP6

| 本章用到 | CP6 现成的 | 怎么接 |
|---|---|---|
| 实体基类 | `BaseEntity`（Id/CreateTime/CreateBy） | `GlAccount`/`JournalEntry` 直接继承 |
| 凭证号采番 | MES 已有 `MesSequence` 采番服务 | 仿一个 `FinSequence`，按"GL-年-月-流水"生成 |
| 审计留痕 | `Sys_OperLog` + Kafka 审计流 | 过账/红冲动作自动落操作日志 |
| 权限 | `Sys_Role`/`Sys_Menu` RBAC | 制单/复核拆成两个权限点，支撑 maker-checker |
| 数据库迁移 | EF Core Migrations（已有 73 个） | 新增 `FinAddGlKernel` 迁移建 4 张表 |

落点目录（沿用 folder=namespace 约定）：
```
CP6.Entity/DomainModels/Fin/   GlAccount, JournalEntry, JournalLine, FiscalPeriod
CP6.Core/Services/Fin/         JournalEntryService, GlAccountService
CP6.WebApi/Controllers/Fin/    JournalEntryController, GlAccountController
cp6.web/src/views/fin/         凭证录入、科目表维护
```

---

## 八、阶段 0 完成自检

- [ ] 我能录一张借贷相等的凭证，存进去、查出来吗？
- [ ] 录一张借 100 / 贷 90 的不平凭证，被拒绝了吗？
- [ ] 往一个非末级科目记账，被挡住了吗？
- [ ] 一张凭证过账后，我还能找到"修改/删除"的入口吗？（应该找不到，只有红冲）
- [ ] 制单人 == 过账人时，过账被拒绝了吗？（maker-checker）
- [ ] 我能讲清"为什么错账要红冲不能直接删"吗？

全部能答 → 总账内核立住了。下一章 [02 期末结账](./02-period-close.md)：会计期间、试算平衡表、锁期——让这本账能"月结"。

---

*生成于 2026-06-10。需求基线：强制 maker-checker / 月结 / 内置默认科目表。配套实现落于 `CP6.*/.../Fin`。*
