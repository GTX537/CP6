using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace CP6.Entity.DomainModels.Wf;

/// <summary>
/// 流程待办任务（OA 章03 §3）。一个节点可建多条（会签：多人各一条）。
/// Status!=0 即"已办"，是 ActAsync 幂等闸门的依据。
/// </summary>
// [审计豁免] 高频运行时待办任务：一节点多条(会签)、Status/IsRead/StageIndex 幂等流转，正确性由 FlowEngine
// 引擎测试锁定，非治理配置/权限授予面。不贴 IAuditable，OawfAuditTests 负测试坐实零审计行。
// 复审补记(审批权归属字段级变更 AssigneeId 改写)：两条原地改写路径——
//   ① AdvancedFlow.TransferAsync (AdvancedFlow.cs:94) task.AssigneeId = toUserId 改派；
//   ② WfTimeoutService escalate 分支 (WfTimeoutService.cs:85) task.AssigneeId = up 超时升级。
// 两处均有结构化补偿留痕，非静默改写：①同步调 TransferFormToAsync(FlowEngine.ReadModel.cs:135-155)
// 在 Wf_FlowFormTo 追加新行(ExpectedHandlerId=toUserId)+原行标 Transferred/ActualHandlerId=fromUserId；
// ②同步 AddHistory-style 写 Wf_FlowHistory「超时升级：{old}→{new}」(WfTimeoutService.cs:80-84)。
// 审批权归属变更本身已被下游读模型/日志表结构化记录，故 AssigneeId 字段级 diff 豁免不丢证据，维持豁免。
[Table("Wf_FlowTask")]
public class Wf_FlowTask : BaseTenantEntity
{
    /// <summary>所属流程实例 → Wf_FlowInstance.Id</summary>
    public Guid InstanceId { get; set; }

    /// <summary>所在节点 Id</summary>
    [MaxLength(100)]
    public string NodeId { get; set; } = string.Empty;

    /// <summary>处理人 → Sys_User.Id</summary>
    public Guid AssigneeId { get; set; }

    /// <summary>任务状态：0=待办 1=同意 2=驳回 3=作废(节点已结/退回时清在途) 4=挂起(前加签时原审批人临时挂起)</summary>
    public int Status { get; set; }

    /// <summary>会签规则快照（建任务时从节点复制）：all/any/veto</summary>
    [MaxLength(20)]
    public string? Countersign { get; set; }

    /// <summary>处理意见</summary>
    [MaxLength(1000)]
    public string? Comment { get; set; }

    /// <summary>加签来源（章07 §3）：null=正常节点任务；before=前加签(先于原审批人)；after=后加签(后于原审批人)</summary>
    [MaxLength(20)]
    public string? AddSignSource { get; set; }

    /// <summary>到期时间（章07 §4 超时扫描依据；空=不限时）</summary>
    public DateTime? DueAt { get; set; }

    /// <summary>超时是否已处理（幂等：硬动作 approve/reject/escalate 处理过置 true，扫描不再处理）</summary>
    public bool TimeoutHandled { get; set; }

    /// <summary>所属令牌 → Wf_FlowToken.Id（WFS P1）。会签计票按 (InstanceId,NodeId,TokenId) 隔离。
    /// 可空：旧数据 / 回填前为 null（后续 Task 8 回填补齐）；新建 task 必带。</summary>
    public Guid? TokenId { get; set; }

    /// <summary>串簽运行档序号(WFS 引擎深化)。默认 0=单档/旧数据,语义不变,无需回填。</summary>
    public int StageIndex { get; set; }

    /// <summary>同一运行档的重入轮次(prevStage 退回后 +1)。计票按 (Inst,Node,Token,StageIndex,StageRound) 隔离,
    /// 杜绝退回上一档后旧轮 Approved 任务串入新轮计票。默认 0。</summary>
    public int StageRound { get; set; }

    /// <summary>未處理"未读"标记（信箱 L2）。送签建任务时默认 false；信箱打开详情/标记已读置 true。</summary>
    public bool IsRead { get; set; }

    /// <summary>标记已读时刻（幂等：已 true 不重置）。</summary>
    public DateTime? ReadAt { get; set; }
}
