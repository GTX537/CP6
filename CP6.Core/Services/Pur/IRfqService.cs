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

    /// <summary>
    /// 比价排名（章06 §4）：按 <see cref="RfqQuote.LineNo"/> 分组横向比价；剔除**过期**报价（ValidUntil &lt; asOf，得 Rank=0 不参与）；
    /// 非过期者 OrderBy QuotedPrice 升序 → ThenBy LeadDays 升序（null 视为最大、垫底）→ ThenBy SupplierId（稳定）赋 Rank=1,2,3…。
    /// <b>Rank 仅是建议，不自动置 IsSelected（人拍板，见 <see cref="SelectAsync"/>）。</b>
    /// asOf 默认 <see cref="DateTime.Now"/>。RFQ 不存在 → E-PUR-061。
    /// </summary>
    Task<Rfq> RankQuotesAsync(string rfqNo, DateTime? asOf, string? userName);

    /// <summary>
    /// 选定（章06 §4）：按行（LineNo, SupplierId）置选中——**可按行拆给不同供应商，一行一选**（改选同行会清掉该行先前选中）；
    /// 校验该报价存在且**未过期**（ValidUntil &gt;= asOf）。所有询价行都被选中时，RFQ 状态推到 <see cref="RfqStatus.Selected"/>。
    /// RFQ 不存在 → E-PUR-061；选中报价已过期 → E-PUR-068；报价不存在 → E-PUR-069。
    /// </summary>
    Task<Rfq> SelectAsync(string rfqNo, IEnumerable<RfqSelectionDto> selections, string? userName);

    /// <summary>
    /// 回写采购价表（章06 §5）：把每条**选中**报价 Upsert 进 <see cref="SupplierPrice"/>（Source=rfq），让询价成果沉淀进价表、
    /// 下次同物料下单 <c>ResolvePriceAsync</c> 直接命中。Price=QuotedPrice、ValidFrom=今天、ValidTo=报价有效期；按 (供应商,物料,MinQty,ValidFrom) 幂等。
    /// 落选报价不回写。RFQ 不存在 → E-PUR-061。
    /// </summary>
    Task<Rfq> WriteBackPricesAsync(string rfqNo, string? userName);

    /// <summary>
    /// 选中报价转 PO（章06 §6）：按**选中供应商分组**（同供应商的行合一张 PO，不同行可花落不同家）调用 02 章
    /// <c>PurchaseOrderService.CreateAsync</c>；价用询价**成交价**（QuotedPrice）非价表取价；PO.SourceRfqNo 追溯回 RFQ；全部转出后 RFQ→Converted。
    /// RFQ 不存在 → E-PUR-061；无选中报价 → E-PUR-070；选中报价已过期（转 PO 第二道关）→ E-PUR-068。返回新建 PO 单号列表。
    /// </summary>
    Task<List<string>> ConvertToPoAsync(string rfqNo, string? userName);
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

/// <summary>选定入参（章06 §4）：一条 = 给某询价行选定某供应商的报价。</summary>
public class RfqSelectionDto
{
    /// <summary>询价行号（= RfqLine.LineNo）</summary>
    public int LineNo { get; set; }

    /// <summary>选定的供应商 CD（= RfqQuote.SupplierId）</summary>
    public string SupplierId { get; set; } = string.Empty;
}
