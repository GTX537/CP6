using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace CP6.Entity.DomainModels.Pur;

/// <summary>
/// 采购申请头（PR，采购 章05 §2）。采购的**需求入口**——"有人提出要买什么"，与 PO（决定向谁买、买多少钱）分离。
/// 需求可手工录入，也可由<b>缺料反流</b>（复用 Phase9 <see cref="CP6.Entity.DomainModels.Wms.MaterialShortage"/>）、
/// <b>工单 BOM 缺料</b> 自动生成（<see cref="Source"/> 标明来源）。批准后转 PO（章05 §5）。
/// </summary>
/// <remarks>
/// PR 不对供应商产生采购义务，仅"内部要买"的申请；真正对外承诺是 <see cref="PurchaseOrder"/>。
/// 多租户继承 <see cref="BaseBizEntity"/>（TenantId 自动盖、查询全局过滤、软删/乐观锁）。
/// </remarks>
[Table("Pur_PurchaseRequest")]
public class PurchaseRequest : BaseBizEntity
{
    /// <summary>采购申请号（采番 "PR"+yyyyMMdd+流水，同租户唯一）</summary>
    [Required, MaxLength(20)]
    public string PrNo { get; set; } = string.Empty;

    /// <summary>申请人（用户标识，与审计 Creator 并存；自动生成时为系统/触发用户）</summary>
    [MaxLength(50)]
    public string? RequesterId { get; set; }

    /// <summary>申请部门 Id（组织模型 Sys_Dept；自动生成时可空）</summary>
    public Guid? DeptId { get; set; }

    /// <summary>申请日期</summary>
    public DateTime RequestDate { get; set; }

    /// <summary>状态（<see cref="PrStatus"/>）：草稿/已提/已批/驳回/已转PO/关闭</summary>
    public PrStatus Status { get; set; } = PrStatus.Draft;

    /// <summary>来源（<see cref="PrSource"/>）：manual 手工 / shortage 缺料反流 / workorder 工单需求</summary>
    [Required, MaxLength(20)]
    public string Source { get; set; } = PrSource.Manual;

    /// <summary>来源单号（缺料单 Id / 工单号；幂等防重的锚点）</summary>
    [MaxLength(60)]
    public string? SourceRefNo { get; set; }

    /// <summary>审批引用（送审后由审批服务回填）</summary>
    [MaxLength(50)]
    public string? ApprovalRef { get; set; }

    /// <summary>备注</summary>
    [MaxLength(500)]
    public string? Remarks { get; set; }

    /// <summary>明细行（业务键 PrNo 关联，级联）</summary>
    public List<PurchaseRequestLine> Lines { get; set; } = new();
}

/// <summary>采购申请状态（采购 章05 §2/§4）。</summary>
public enum PrStatus
{
    /// <summary>草稿</summary>
    Draft = 0,
    /// <summary>已提交（送审中，待审批）</summary>
    Submitted = 1,
    /// <summary>已批准（可转 PO）</summary>
    Approved = 2,
    /// <summary>驳回（退回申请人）</summary>
    Rejected = 3,
    /// <summary>已转 PO（全部行已转出）</summary>
    Converted = 4,
    /// <summary>关闭</summary>
    Closed = 9,
}

/// <summary>采购申请来源（采购 章05 §3）。"需求驱动"的核心——PR 怎么来的。</summary>
public static class PrSource
{
    /// <summary>手工提报</summary>
    public const string Manual = "manual";
    /// <summary>缺料反流（复用 Phase9 MaterialShortage）</summary>
    public const string Shortage = "shortage";
    /// <summary>工单 BOM 缺料</summary>
    public const string WorkOrder = "workorder";
}
