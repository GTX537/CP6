using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace CP6.Entity.DomainModels.Wf;

/// <summary>
/// 站内通知持久化（OA Phase D-1 §通知中心）。
/// 每事件产生一行，随引擎 SaveChanges 同一工作单元落库。
/// 收件人=UserId（新待办→处理人；签核完成/驳回→发起人；超时→处理人）。
/// </summary>
// [审计豁免] 站内通知：运行时随事件追加、IsRead/ReadAt 高频翻转，非治理配置/权限授予面。
// 不贴 IAuditable，OawfAuditTests 负测试坐实零审计行。
[Table("Wf_Notification")]
public class Wf_Notification : BaseTenantEntity
{
    /// <summary>收件人 → Sys_User.Id</summary>
    public Guid UserId { get; set; }

    /// <summary>通知类型（WfNotificationType 常量）</summary>
    public int Type { get; set; }

    /// <summary>通知标题（最长 200 字符）</summary>
    [MaxLength(200)]
    public string Title { get; set; } = string.Empty;

    /// <summary>通知正文（nvarchar max，富文本/Markdown 可用）</summary>
    [Column(TypeName = "nvarchar(max)")]
    public string Body { get; set; } = string.Empty;

    /// <summary>关联流程实例 → Wf_FlowInstance.Id（可空：系统级通知无实例）</summary>
    public Guid? InstanceId { get; set; }

    /// <summary>关联待办任务 → Wf_FlowTask.Id（可空：签核完成/驳回类型无具体任务）</summary>
    public Guid? TaskId { get; set; }

    /// <summary>流程定义键（路由用，最长 100 字符；可空）</summary>
    [MaxLength(100)]
    public string? FlowKey { get; set; }

    /// <summary>是否已读（默认 false）</summary>
    public bool IsRead { get; set; } = false;

    /// <summary>标记已读时刻（幂等：已 true 不重置）</summary>
    public DateTime? ReadAt { get; set; }
}
