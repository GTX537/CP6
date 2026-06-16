using CP6.Entity.DomainModels.Pur;

namespace CP6.Core.Services.Pur;

/// <summary>
/// 询价服务（RFQ，采购 章06 §2/§3）。价格发现机制：从 PR 发起询价 + 邀供应商 + 收报价。
/// 本接口（B-1）只覆盖"建/邀/收报价"；比价排名/选定/回写价表/转 PO 在后续任务（B-2/B-3）。
/// </summary>
public interface IRfqService
{
    /// <summary>取 RFQ（含行 + 被邀供应商 + 报价矩阵）。</summary>
    Task<Rfq?> GetAsync(string rfqNo);

    /// <summary>列出 RFQ（可按状态过滤）。</summary>
    Task<List<Rfq>> ListAsync(RfqStatus? status = null);

    /// <summary>
    /// 从 PR 发起询价（章06 §3.1）：把指定 PR 中**未定供应商**（SuggestSupplierId 为空）且有效、未转出的行汇成一张 RFQ。
    /// 拷贝 ItemId/Qty/UnitCd/RequiredDate；RfqLine.SourcePrNo + SourcePrLineNo 行级追溯回 PR。状态=草稿。
    /// 无可询价行 → 抛 E-PUR-060。
    /// </summary>
    Task<Rfq> CreateFromPrAsync(string prNo, string? userName);

    /// <summary>
    /// 邀请供应商（章06 §3.2）：每个供应商须存在且 SupplierFlg=true（发注先）；加 RfqSupplier 行（InviteStatus=Invited，幂等不重复加）；
    /// RFQ 状态推到 Inviting。RFQ 不存在 → E-PUR-061；供应商不存在 → E-PUR-062；非发注先 → E-PUR-063。
    /// </summary>
    Task<Rfq> AddSuppliersAsync(string rfqNo, IEnumerable<string> supplierIds, string? userName);

    /// <summary>
    /// 收报价录入（章06 §3.3）：对**已被邀请**的供应商，按行 upsert RfqQuote（QuotedPrice/CurrencyCd/LeadDays/ValidUntil）；
    /// 该供应商 InviteStatus → Quoted；RFQ 状态推到 Quoting。供应商未被邀请 → E-PUR-064；报价行非询价行 → E-PUR-065；无报价行 → E-PUR-066。
    /// </summary>
    Task<Rfq> RecordQuoteAsync(string rfqNo, string supplierId, IEnumerable<RfqQuoteLineDto> quoteLines, string? userName);
}

/// <summary>收报价单行入参（一行对应一个 RfqLine.LineNo）。</summary>
public class RfqQuoteLineDto
{
    /// <summary>对应询价行号（= RfqLine.LineNo）</summary>
    public int LineNo { get; set; }

    /// <summary>报价单价（原币）</summary>
    public decimal QuotedPrice { get; set; }

    /// <summary>币种 CD（null=本位币）</summary>
    public string? CurrencyCd { get; set; }

    /// <summary>交期（天）</summary>
    public int? LeadDays { get; set; }

    /// <summary>报价有效期</summary>
    public DateTime? ValidUntil { get; set; }
}
