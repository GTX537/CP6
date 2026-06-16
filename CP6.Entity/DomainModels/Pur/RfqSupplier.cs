using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace CP6.Entity.DomainModels.Pur;

/// <summary>
/// 被邀供应商（RFQ，采购 章06 §2/§3）。"问谁"——复用 <see cref="CP6.Entity.DomainModels.Erp.BusinessPartner"/> 发注先（不新建供应商）。
/// 一询价单 × 一供应商一条；<see cref="InviteStatus"/> 记邀请生命周期（已邀/已报价/拒绝）。
/// </summary>
[Table("Pur_RfqSupplier")]
public class RfqSupplier : BaseBizEntity
{
    /// <summary>所属询价单号</summary>
    [Required, MaxLength(20)]
    public string RfqNo { get; set; } = string.Empty;

    /// <summary>供应商 CD（= BusinessPartner.BpCd，须 SupplierFlg=true）</summary>
    [Required, MaxLength(20)]
    public string SupplierId { get; set; } = string.Empty;

    /// <summary>供应商名称（邀请快照）</summary>
    [MaxLength(100)]
    public string? SupplierName { get; set; }

    /// <summary>邀请状态（<see cref="RfqInviteStatus"/>）：待邀/已邀/已报价/拒绝</summary>
    public RfqInviteStatus InviteStatus { get; set; } = RfqInviteStatus.Pending;
}

/// <summary>询价邀请状态（采购 章06 §3）。</summary>
public enum RfqInviteStatus
{
    /// <summary>待邀（已加入名单，未发出）</summary>
    Pending = 0,
    /// <summary>已邀（已发出邀请，待报价）</summary>
    Invited = 1,
    /// <summary>已报价（收到报价录入）</summary>
    Quoted = 2,
    /// <summary>拒绝（供应商拒报）</summary>
    Declined = 3,
}
