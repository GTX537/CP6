using CP6.Entity.DomainModels.Wf;

namespace CP6.Core.Services.Wf;

/// <summary>运行期档(展平 ApprovalStage 后的稳定结果)。进 approval 节点时算一次并冻结进 token.StagePlanJson。</summary>
public sealed class RuntimeApprovalStage
{
    public int StageIndex { get; set; }
    public string Kind { get; set; } = ApprovalStageKinds.Fixed;
    public string? StageName { get; set; }
    public string? StageCode { get; set; }
    public ApproverRule Rule { get; set; } = default!;
    public string Countersign { get; set; } = CountersignModes.All;
}

/// <summary>串簽档展平服务。把 node.Stages 展成稳定运行档序列。无 Stages → 单档(节点既有字段),保旧行为等价。
/// managerChain 沿 starter ManagerId 链逐级探,链断或 MaxLevels 止;只定序列/规则/会签,审批人 USER ID 在各档激活时晚解析。</summary>
public interface IApprovalStagePlanner
{
    Task<IReadOnlyList<RuntimeApprovalStage>> BuildAsync(Wf_FlowInstance inst, FlowSchema schema, FlowNode node);
}
