using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace CP6.Entity.DomainModels.Wf;

/// <summary>填單☆收藏（信箱 L2，umbrella §2.5）。唯一 (TenantId,UserId,FormKey)。</summary>
// [审计豁免] 用户个人偏好：填單☆收藏，非治理配置/权限授予面。不贴 IAuditable，OawfAuditTests 负测试坐实零审计行。
[Table("Wf_FormFavorite")]
public class Wf_FormFavorite : BaseTenantEntity
{
    public Guid UserId { get; set; }
    [MaxLength(100)] public string FormKey { get; set; } = string.Empty;
}
