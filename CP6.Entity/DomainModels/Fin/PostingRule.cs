using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace CP6.Entity.DomainModels.Fin;

/// <summary>
/// 记账规则（章05 §2）。自动凭证引擎的"规则即数据"——一种业务事件（<see cref="EventType"/>）
/// 对应一条规则，规则的若干 <see cref="Lines"/> 描述"借/贷哪个科目角色、取哪个金额字段"。
/// 引擎按 EventType 找到规则后据 Lines 拼凭证，换会计准则/科目编码只改 seed，引擎零改动。
/// </summary>
/// <remarks>
/// 设计铁则：规则只配"角色锚点（Role）/金额字段名"，不写死科目 Id；科目由 <see cref="GlAccount.Role"/> 解析。
/// 本阶段不引入 TenantId（统一在 OA 阶段4 系统级多租户收口）。
/// </remarks>
[Table("Fin_PostingRule")]
public class PostingRule : BaseEntity
{
    /// <summary>业务事件类型，如 "AP.InvoicePosted"/"AP.Payment"/"AR.Revenue"/"AR.Cogs"。与 FinBizEvent.EventType 对应</summary>
    [Required, MaxLength(50)]
    public string EventType { get; set; } = string.Empty;

    /// <summary>规则名称（人读）</summary>
    [Required, MaxLength(100)]
    public string Name { get; set; } = string.Empty;

    /// <summary>生成凭证的来源类型（落到 JournalEntry.Source）</summary>
    public VoucherSource VoucherSource { get; set; } = VoucherSource.Manual;

    /// <summary>停用的规则不再被引擎采用（财务里"删"是禁词，只停用）</summary>
    public bool IsActive { get; set; } = true;

    /// <summary>规则行（描述每条分录怎么拼）</summary>
    public List<PostingRuleLine> Lines { get; set; } = new();
}

/// <summary>
/// 记账规则行（章05 §2）。两种取数模式见 <see cref="Source"/>：
/// ①FixedRole 固定行——按 <see cref="AccountRole"/> 角色锚点取科目，金额取事件头的 <see cref="AmountField"/>；
/// ②DocumentLines 透传行——把事件的单据行按 <see cref="LineAccountField"/>(科目)+成本中心分组炸开，金额取 <see cref="LineAmountField"/>。
/// </summary>
[Table("Fin_PostingRuleLine")]
public class PostingRuleLine : BaseEntity
{
    /// <summary>所属规则 Id</summary>
    public Guid RuleId { get; set; }

    /// <summary>行序</summary>
    public int LineNo { get; set; }

    /// <summary>借/贷</summary>
    public PostingSide Side { get; set; }

    /// <summary>取数模式：固定角色行 / 单据行透传</summary>
    public RuleLineSource Source { get; set; }

    // —— FixedRole 模式 ——
    /// <summary>科目角色锚点（如 AP_CONTROL/TAX_INPUT/REVENUE/COGS/FG/BANK/FX_GAIN_LOSS）。引擎按它在 GlAccount.Role 找科目</summary>
    [MaxLength(30)]
    public string? AccountRole { get; set; }

    /// <summary>事件头金额字段名（如 "GrossAmount"/"TaxAmount"/"NetAmount"/"Amount"）。引擎调 FinBizEvent.GetAmount(field) 取值</summary>
    [MaxLength(50)]
    public string? AmountField { get; set; }

    /// <summary>该行是否带往来单位（应收应付科目=true，引擎从事件头 PartnerId 盖章到分录行）</summary>
    public bool CarryPartner { get; set; }

    /// <summary>该行是否带成本中心</summary>
    public bool CarryCostCenter { get; set; }

    /// <summary>角色解析失败时的兜底科目 Id（可空，null 则解析失败抛错）</summary>
    public Guid? FallbackAccountId { get; set; }

    // —— DocumentLines 模式 ——
    /// <summary>单据行里"科目 Id"字段名（如 "ExpenseAccountId"）。引擎调 line.GetGuid(field) 取科目</summary>
    [MaxLength(50)]
    public string? LineAccountField { get; set; }

    /// <summary>单据行里"金额"字段名（如 "Amount"）。引擎调 line.GetAmount(field) 取金额</summary>
    [MaxLength(50)]
    public string? LineAmountField { get; set; }
}

/// <summary>记账规则行的借贷方向（章05）。语义同凭证分录的借/贷</summary>
public enum PostingSide
{
    /// <summary>借</summary>
    Debit = 1,
    /// <summary>贷</summary>
    Credit = 2,
}

/// <summary>记账规则行取数模式（章05）</summary>
public enum RuleLineSource
{
    /// <summary>固定角色行：按 Role 锚点取科目 + 取事件头某金额字段（如 贷 AP_CONTROL 取 GrossAmount）</summary>
    FixedRole = 1,
    /// <summary>单据行透传：把事件 DocLines 按 科目+成本中心 分组炸成多条分录（如 借各 ExpenseAccount）</summary>
    DocumentLines = 2,
    /// <summary>事件头取科目：科目 Id 来自事件头某 Guid 字段（<see cref="LineAccountField"/> 命名该字段，
    /// 如付款的银行存款科目"BankGlAccountId"）+ 金额取 <see cref="AmountField"/>。银行/现金等"运行时才定科目"的行用。</summary>
    HeaderAccount = 3,
}
