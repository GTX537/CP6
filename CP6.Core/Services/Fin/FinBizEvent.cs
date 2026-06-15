using CP6.Entity.DomainModels.Erp;
using CP6.Entity.DomainModels.Fin;

namespace CP6.Core.Services.Fin;

/// <summary>
/// 财务业务事件载荷（章05 §2）。AP/AR 各生命周期段（发票过账/收付款/成本结转）构造一个事件，
/// 交给 <c>IAutoVoucherEngine.GenerateAsync</c> 据 <see cref="PostingRule"/> 拼凭证并直过。
/// 非持久化实体——只是引擎的入参。
/// </summary>
/// <remarks>
/// 取值采用"按字段名查表"：规则行配 <c>AmountField="GrossAmount"</c>，引擎调 <see cref="GetAmount"/>("GrossAmount")
/// 从 <see cref="HeaderAmounts"/> 取值；这样引擎对各事件类型的具体字段零依赖（规则即数据）。
/// 全外币（F2-D1）：<see cref="CurrencyCd"/>/<see cref="FxRate"/> 描述原币与记账汇率，引擎按汇率换算本位币。
/// </remarks>
public class FinBizEvent
{
    /// <summary>业务事件类型，匹配 <see cref="PostingRule.EventType"/>（如 "AP.InvoicePosted"）</summary>
    public string EventType { get; set; } = string.Empty;

    /// <summary>生成凭证的来源（落 JournalEntry.Source）。幂等判定据此区分手工/自动</summary>
    public VoucherSource Source { get; set; } = VoucherSource.Manual;

    /// <summary>来源单据号（如 AP 发票号）。幂等键 (Source,SourceDocNo)</summary>
    public string SourceDocNo { get; set; } = string.Empty;

    /// <summary>业务/记账日期（决定凭证归期）</summary>
    public DateTime BizDate { get; set; }

    /// <summary>往来单位（应收应付科目带 Partner 的来源）</summary>
    public string? PartnerId { get; set; }

    /// <summary>成本中心（CarryCostCenter 行的来源）</summary>
    public Guid? CostCenterId { get; set; }

    /// <summary>凭证摘要</summary>
    public string Description { get; set; } = string.Empty;

    // —— 全外币（F2-D1）——
    /// <summary>原币种，null/JPY=本位币</summary>
    public string? CurrencyCd { get; set; }

    /// <summary>记账汇率（外币1单位→本位币JPY）。null/本位币时按 1 处理</summary>
    public decimal? FxRate { get; set; }

    /// <summary>事件头金额（字段名→原币金额）。FixedRole 行据 AmountField 取</summary>
    public Dictionary<string, decimal> HeaderAmounts { get; } = new();

    /// <summary>事件头 Guid（字段名→Guid，如兜底科目）。备用</summary>
    public Dictionary<string, Guid> HeaderGuids { get; } = new();

    /// <summary>单据行（DocumentLines 行据此炸开）</summary>
    public List<FinBizEventLine> DocLines { get; } = new();

    /// <summary>是否本位币（CurrencyCd 空或 JPY）</summary>
    public bool IsBaseCurrency => FxConstants.IsBase(CurrencyCd);

    /// <summary>有效记账汇率：本位币=1，外币取 FxRate（缺失视为 1 兜底）</summary>
    public decimal EffectiveRate => IsBaseCurrency ? 1m : (FxRate ?? 1m);

    /// <summary>按字段名取事件头原币金额（FixedRole 用）。缺失返回 0</summary>
    public decimal GetAmount(string? field)
        => string.IsNullOrEmpty(field) ? 0m : HeaderAmounts.GetValueOrDefault(field);

    /// <summary>按字段名取事件头 Guid。缺失返回 null</summary>
    public Guid? GetGuid(string? field)
        => !string.IsNullOrEmpty(field) && HeaderGuids.TryGetValue(field, out var g) ? g : null;
}

/// <summary>
/// 财务业务事件的单据行（章05 §2）。DocumentLines 规则行据 <c>LineAccountField</c>(科目)+成本中心分组、
/// 按 <c>LineAmountField</c> 求和炸成多条分录（如混行发票各原材料/运费行各进各费用科目）。
/// </summary>
public class FinBizEventLine
{
    /// <summary>行 Guid 字段（字段名→Guid，如 "ExpenseAccountId"→科目 Id）</summary>
    public Dictionary<string, Guid> Guids { get; } = new();

    /// <summary>行金额字段（字段名→原币金额，如 "Amount"→100）</summary>
    public Dictionary<string, decimal> Amounts { get; } = new();

    /// <summary>本行成本中心（CarryCostCenter / 分组维度）</summary>
    public Guid? CostCenterId { get; set; }

    /// <summary>按字段名取行科目 Id。缺失返回 null</summary>
    public Guid? GetGuid(string? field)
        => !string.IsNullOrEmpty(field) && Guids.TryGetValue(field, out var g) ? g : null;

    /// <summary>按字段名取行原币金额。缺失返回 0</summary>
    public decimal GetAmount(string? field)
        => string.IsNullOrEmpty(field) ? 0m : Amounts.GetValueOrDefault(field);
}
