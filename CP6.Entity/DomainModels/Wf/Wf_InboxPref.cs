using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace CP6.Entity.DomainModels.Wf;

/// <summary>信箱显示偏好（umbrella §2.5）。每用户一行，PrefsJson 自由结构。唯一 (TenantId,UserId)。</summary>
// [审计豁免] 用户个人偏好：信箱显示偏好 PrefsJson 自由结构，非治理配置/权限授予面。
// 不贴 IAuditable，OawfAuditTests 负测试坐实零审计行。
[Table("Wf_InboxPref")]
public class Wf_InboxPref : BaseTenantEntity
{
    public Guid UserId { get; set; }
    [Column(TypeName = "nvarchar(max)")] public string PrefsJson { get; set; } = "{}";
}
