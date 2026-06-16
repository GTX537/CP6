using CP6.Entity.DomainModels.Pur;

namespace CP6.Core.Services.Pur;

/// <summary>
/// 采购申请服务（采购 章05 §4/§5）。手工建单（CRUD）+ 送审（经审批委托，桩即批）+ PR→PO 转换。
/// 转换照章05 §5：按建议供应商 <see cref="PurchaseRequestLine.SuggestSupplierId"/> 分组拆单，
/// 复用 Plan1 <see cref="IPurchaseOrderService.CreateAsync"/> 建 PO 并回填 <see cref="PurchaseRequestLine.ConvertedPoNo"/>；
/// 未定供应商的行留待 RFQ 选定，不在此转出。生成逻辑见 <see cref="IPrGenerationService"/>。
/// </summary>
public interface IPurchaseRequestService
{
    /// <summary>取 PR（含行）。</summary>
    Task<PurchaseRequest?> GetAsync(string prNo);

    /// <summary>列出 PR（可按状态/来源过滤）。</summary>
    Task<List<PurchaseRequest>> ListAsync(PrStatus? status = null, string? source = null);

    /// <summary>
    /// 手工建采购申请（章05 §4）：Source=manual；采番 PrNo；状态草稿；至少一行（物料必填、数量&gt;0）。
    /// </summary>
    Task<PurchaseRequest> CreateAsync(PrCreateDto dto, string? userName);

    /// <summary>
    /// 送审（章05 §4）：草稿 → 调审批委托（桩即时通过，BizType=PUR_PR）→ 回填 ApprovalRef + 置已批。
    /// </summary>
    Task<PurchaseRequest> SubmitForApprovalAsync(string prNo, string? userName);

    /// <summary>
    /// 转 PO（章05 §5）：仅已批 PR。取可转行（有效 ∧ 有建议供应商 ∧ 未转出），按建议供应商分组，
    /// 每组复用 <see cref="IPurchaseOrderService.CreateAsync"/> 建一张 PO（行带物料/数量/估价作单价/交期），
    /// 回填各行 ConvertedPoNo。无建议供应商的行留待 RFQ（不转）。全行转出 → Converted；否则保持 Approved。
    /// </summary>
    /// <returns>新建的 PO 号列表（按供应商一供应商一张）。</returns>
    Task<List<string>> ConvertToPoAsync(string prNo, string? userName);
}

/// <summary>建采购申请入参。</summary>
public class PrCreateDto
{
    /// <summary>申请人（用户标识；缺省取当前用户）</summary>
    public string? RequesterId { get; set; }

    /// <summary>申请部门 Id</summary>
    public Guid? DeptId { get; set; }

    /// <summary>申请日期（缺省取当天）</summary>
    public DateTime? RequestDate { get; set; }

    /// <summary>备注</summary>
    public string? Remarks { get; set; }

    /// <summary>明细行</summary>
    public List<PrLineCreateDto> Lines { get; set; } = new();
}

/// <summary>建采购申请明细行入参。</summary>
public class PrLineCreateDto
{
    /// <summary>物料 CD</summary>
    public string ItemId { get; set; } = string.Empty;

    /// <summary>申请数量</summary>
    public decimal Qty { get; set; }

    /// <summary>单位 CD</summary>
    public string? UnitCd { get; set; }

    /// <summary>要求交期</summary>
    public DateTime? RequiredDate { get; set; }

    /// <summary>估价（原币；供审批参考；转 PO 时作单价带出）</summary>
    public decimal? EstPrice { get; set; }

    /// <summary>建议供应商 CD（= BusinessPartner.BpCd；无则该行走 RFQ 不转）</summary>
    public string? SuggestSupplierId { get; set; }
}
