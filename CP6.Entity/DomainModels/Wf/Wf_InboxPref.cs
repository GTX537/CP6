using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace CP6.Entity.DomainModels.Wf;

/// <summary>信箱显示偏好（umbrella §2.5）。每用户一行，PrefsJson 自由结构。唯一 (TenantId,UserId)。</summary>
[Table("Wf_InboxPref")]
public class Wf_InboxPref : BaseTenantEntity
{
    public Guid UserId { get; set; }
    [Column(TypeName = "nvarchar(max)")] public string PrefsJson { get; set; } = "{}";
}
