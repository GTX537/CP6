using CP6.Entity.DomainModels.Pur;

namespace CP6.Core.Services.Pur;

/// <summary>
/// 采购订单服务（采购 章02）。建单带出（带价/税码/币种/入账基准/冻结汇率）+ 派生状态机 + 审批送审。
/// </summary>
public interface IPurchaseOrderService
{
    /// <summary>取 PO（含行）。</summary>
    Task<PurchaseOrder?> GetAsync(string poNo);

    /// <summary>列出 PO（可按供应商/状态过滤）。</summary>
    Task<List<PurchaseOrder>> ListAsync(string? supplierId = null, PoStatus? status = null);

    /// <summary>
    /// 建采购订单（章02 §3）：校验供应商 SupplierFlg；带出币种/入账基准/汇率快照；
    /// 行未填单价则按阶梯价解析；行未填税码则取供应商默认税码；算行/头净税合计；三累计锚初始 0。
    /// </summary>
    Task<PurchaseOrder> CreateAsync(PoCreateDto dto, string? userName);

    /// <summary>
    /// 送审（章02 §7）：草稿 → 调审批服务（桩即时通过 / 真实流程进 PendingApproval）→ 回填 ApprovalRef。
    /// </summary>
    Task<PurchaseOrder> SubmitForApprovalAsync(string poNo, string? userName);

    /// <summary>OA 审批通过回调：PendingApproval→Confirmed（幂等；不 SaveChanges，由 OA 引擎统一持久化）。</summary>
    Task ConfirmFromApprovalAsync(string poNo, string decidedBy);

    /// <summary>OA 审批驳回回调：PendingApproval→Draft 可重编重送（幂等；不 SaveChanges）。</summary>
    Task RejectFromApprovalAsync(string poNo, string reason);

    /// <summary>取消（章02 §4）：仅草稿/确认（未发生收货）可取消。</summary>
    Task CancelAsync(string poNo, string? userName);

    /// <summary>
    /// 据三累计锚刷新派生状态（章02 §4）。收货回写、匹配累加后由 GR/匹配服务调用。
    /// </summary>
    Task RefreshStatusAsync(string poNo);
}

/// <summary>建采购订单入参。</summary>
public class PoCreateDto
{
    /// <summary>供应商 CD（= BusinessPartner.BpCd）</summary>
    public string SupplierId { get; set; } = string.Empty;

    /// <summary>类型：1=标准 / 2=外注</summary>
    public int Type { get; set; } = 1;

    /// <summary>订单日期（汇率冻结基准日；缺省取当天）</summary>
    public DateTime? OrderDate { get; set; }

    /// <summary>来源询价单号（RFQ 转 PO）</summary>
    public string? SourceRfqNo { get; set; }

    /// <summary>备注</summary>
    public string? Remarks { get; set; }

    /// <summary>明细行</summary>
    public List<PoLineCreateDto> Lines { get; set; } = new();
}

/// <summary>建采购订单明细行入参。</summary>
public class PoLineCreateDto
{
    /// <summary>物料 CD</summary>
    public string ItemId { get; set; } = string.Empty;

    /// <summary>数量</summary>
    public decimal Qty { get; set; }

    /// <summary>单价（null/0 → 按供应商阶梯价解析；无价挡单）</summary>
    public decimal? UnitPrice { get; set; }

    /// <summary>税码 Id（null → 取供应商默认税码 PurchaseTaxCd）</summary>
    public Guid? TaxCodeId { get; set; }

    /// <summary>要求交期</summary>
    public DateTime? RequiredDate { get; set; }
}
