using System.ComponentModel.DataAnnotations;

namespace CP6.Entity.DomainModels.Wf;

/// <summary>审批人映射表(②b Menu 数据驱动)。一条=某命名映射下某匹配值对应一个审批目标(用户或角色)。
/// 同 (MapKey,MatchValue) 可多行(多审批人→会签组)。租户隔离走 BaseTenantEntity 全局过滤器。</summary>
public class Wf_ApproverMap : BaseTenantEntity
{
    [MaxLength(100)] public string MapKey { get; set; } = "";
    [MaxLength(200)] public string MatchValue { get; set; } = "";
    public Guid? ApproverUserId { get; set; }
    public int? ApproverRoleId { get; set; }
    public int OrderNo { get; set; }
    public bool Enable { get; set; } = true;
}
