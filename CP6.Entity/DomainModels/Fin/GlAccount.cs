using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace CP6.Entity.DomainModels.Fin;

/// <summary>
/// 会计科目（Chart of Accounts 的一行）。财务模块的"字典"，所有凭证行只能引用本表科目。
/// 章01 §1-2。多国别模板包共享结构（五大类+借贷方向+控制科目），只换编码/命名；
/// 自动凭证按 <see cref="Role"/> 角色锚点找科目，换模板包零改动。
/// </summary>
/// <remarks>
/// 设计铁则：①只有末级（<see cref="IsLeaf"/>）能记凭证 ②科目不删只停用（<see cref="IsActive"/>=false）。
/// 本阶段不引入 TenantId（统一在 OA 阶段4 系统级多租户收口，届时接 BaseTenantEntity）。
/// </remarks>
[Table("Fin_GlAccount")]
public class GlAccount : BaseTenantEntity, IAuditable
{
    /// <summary>科目编码，如 "1122"（应收账款）。同一模板包内唯一</summary>
    [Required, MaxLength(30)]
    public string Code { get; set; } = string.Empty;

    /// <summary>科目名称，如 "应收账款"</summary>
    [Required, MaxLength(100)]
    public string Name { get; set; } = string.Empty;

    /// <summary>大类：资产/负债/权益/收入/费用</summary>
    public AccountType Type { get; set; }

    /// <summary>正常方向（增加记哪方）：借/贷。算余额时定符号</summary>
    public AccountSide NormalSide { get; set; }

    /// <summary>树形：上级科目 Id（一级科目为 null）</summary>
    public Guid? ParentId { get; set; }

    /// <summary>层级，1=一级科目</summary>
    public int Level { get; set; } = 1;

    /// <summary>是否末级（只有末级能记凭证）</summary>
    public bool IsLeaf { get; set; } = true;

    /// <summary>控制科目？余额由子账勾稽（应付/应收/库存/在制品），不允许直接乱记</summary>
    public bool IsControl { get; set; }

    /// <summary>控制科目对应的子账类型："AP"/"AR"/"INV"/"COST"，非控制科目为 null</summary>
    [MaxLength(10)]
    public string? SubLedgerType { get; set; }

    /// <summary>记账时是否必须带往来单位（应收应付科目为 true）</summary>
    public bool RequirePartner { get; set; }

    /// <summary>★跨模板恒定的角色锚点（AP_CONTROL/AR_CONTROL/REVENUE/COGS...）。自动凭证按它找科目</summary>
    [MaxLength(30)]
    public string? Role { get; set; }

    /// <summary>来自哪套模板包：CN-GAAP/INTL/JP/US-GAAP</summary>
    [Required, MaxLength(20)]
    public string StandardScheme { get; set; } = "CN-GAAP";

    /// <summary>停用的科目不能再记新凭证（财务里"删"是禁词，只停用）</summary>
    public bool IsActive { get; set; } = true;

    /// <summary>外币专户可锁币种，null=本位币</summary>
    [MaxLength(10)]
    public string? CurrencyCd { get; set; }
}

/// <summary>会计大类。五大类各有固定的"增加方向"（复式记账的数学结构）</summary>
public enum AccountType
{
    /// <summary>资产（借方增）</summary>
    Asset = 1,
    /// <summary>负债（贷方增）</summary>
    Liability = 2,
    /// <summary>权益（贷方增）</summary>
    Equity = 3,
    /// <summary>收入（贷方增）</summary>
    Revenue = 4,
    /// <summary>费用/成本（借方增）</summary>
    Expense = 5,
}

/// <summary>借贷方向</summary>
public enum AccountSide
{
    /// <summary>借</summary>
    Debit = 1,
    /// <summary>贷</summary>
    Credit = 2,
}
