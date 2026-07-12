using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace CP6.Entity.DomainModels.Wf;

/// <summary>
/// 传签履历台账（WFS 读模型）。token 每到一个人工关卡落一行：送签时建、处理时更新。
/// 带 TokenId → 并行多分支履历各成一串。与 Wf_FlowHistory（纯追加事件日志）分工互补。
/// </summary>
// [审计豁免] 传签履历台账：运行时读模型，token 每到人工关卡送签建、处理更新(Status/HandledAt 随流转)，
// 由引擎测试锁定，非治理配置/权限授予面。不贴 IAuditable，OawfAuditTests 负测试坐实零审计行。
// 本表是 Wf_FlowTask.AssigneeId 改派(TransferAsync)的结构化审计源：转出行标 Transferred/ActualHandlerId，
// 受让人新起 Pending 行 ExpectedHandlerId=toUserId（见 FlowEngine.ReadModel.cs:135-155 TransferFormToAsync）。
[Table("Wf_FlowFormTo")]
public class Wf_FlowFormTo : BaseTenantEntity
{
    public Guid InstanceId { get; set; }
    public Guid? TokenId { get; set; }
    public int StepSeq { get; set; }

    [MaxLength(100)] public string? FromNodeId { get; set; }
    [MaxLength(100)] public string NodeId { get; set; } = string.Empty;
    [MaxLength(100)] public string? NodeCode { get; set; }
    [MaxLength(200)] public string? NodeName { get; set; }

    public Guid ExpectedHandlerId { get; set; }
    public Guid? ActualHandlerId { get; set; }
    public Guid? OnBehalfOfId { get; set; }

    /// <summary>0=待签 1=同意 2=驳回 3=转交 4=加签 5=跳过 6=作废。见 FlowFormToStatus。</summary>
    public int Status { get; set; }

    /// <summary>串簽运行档序号(timeline/forecast 标号)。旧行 null。</summary>
    public int? StageIndex { get; set; }
    /// <summary>串簽重入轮次。旧行 null。</summary>
    public int? StageRound { get; set; }

    [MaxLength(1000)] public string? Comment { get; set; }
    public DateTime SentAt { get; set; }
    public DateTime? HandledAt { get; set; }
}
