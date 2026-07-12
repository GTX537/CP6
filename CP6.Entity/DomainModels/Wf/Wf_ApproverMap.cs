using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace CP6.Entity.DomainModels.Wf;

/// <summary>审批人映射表(②b Menu 数据驱动)。一条=某命名映射下某匹配值对应一个审批目标(用户或角色)。
/// 同 (MapKey,MatchValue) 可多行(多审批人→会签组)。租户隔离走 BaseTenantEntity 全局过滤器。
/// 表名单数 Wf_ApproverMap，对齐全部 Wf_* 实体命名约定(DbSet 复数/[Table] 单数)。</summary>
// [审计纳入] 审批人映射＝数据驱动的权限授予面：MapKey/MatchValue→审批目标(ApproverUserId/ApproverRoleId)。
// 改一行即改变某匹配值由谁审批，等同授予/回收审批权——权限授予面，须字段级留痕。贴 IAuditable。
[Table("Wf_ApproverMap")]
public class Wf_ApproverMap : BaseTenantEntity, IAuditable
{
    [MaxLength(100)] public string MapKey { get; set; } = "";
    [MaxLength(200)] public string MatchValue { get; set; } = "";
    public Guid? ApproverUserId { get; set; }
    public int? ApproverRoleId { get; set; }
    public int OrderNo { get; set; }
    public bool Enable { get; set; } = true;
}
