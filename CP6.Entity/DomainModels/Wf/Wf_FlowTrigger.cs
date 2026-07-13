using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace CP6.Entity.DomainModels.Wf;

/// <summary>流程触发器（timer/event/message 三型，spec §2.1）。配置挂流程级（D5），不进设计器 schema。</summary>
[Table("Wf_FlowTrigger")]
public class Wf_FlowTrigger : BaseTenantEntity
{
    /// <summary>目标流程（对齐 SubmitAsync 口径）</summary>
    [MaxLength(200)] public string FlowKey { get; set; } = "";

    /// <summary>WfTriggerType: Timer=0 / Event=1 / Message=2</summary>
    public int TriggerType { get; set; }

    /// <summary>分型配置（spec §2.3）：timer={cron,varsJson} / event={varsMap} / message={varsSchema}</summary>
    [Column(TypeName = "nvarchar(max)")]
    public string ConfigJson { get; set; } = "{}";

    public bool Enabled { get; set; }

    /// <summary>event 专用（提列可索引；格式 "{SourceModule}|{HookName}"；ConfigJson 不再重复存）</summary>
    [MaxLength(200)] public string? EventKey { get; set; }

    /// <summary>名义发起人（D6，必填）——审计与 starter.* 审批人解析都依赖它</summary>
    public Guid StarterUserId { get; set; }

    /// <summary>timer 专用：下次到期（扫描键，UTC）</summary>
    public DateTime? NextDueUtc { get; set; }

    public DateTime? LastFiredUtc { get; set; }

    /// <summary>message 专用：SHA-256 hex（明文只在创建/重置响应显示一次）</summary>
    [MaxLength(64)] public string? ApiKeyHash { get; set; }

    /// <summary>乐观并发（多实例 worker 抢占）</summary>
    [Timestamp] public byte[]? RowVersion { get; set; }
}
