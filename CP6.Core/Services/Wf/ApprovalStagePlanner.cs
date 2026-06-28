using CP6.Entity.DomainModels.Wf;

namespace CP6.Core.Services.Wf;

public sealed class ApprovalStagePlanner : IApprovalStagePlanner
{
    private readonly IApproverResolver _approver;
    public ApprovalStagePlanner(IApproverResolver approver) => _approver = approver;

    public async Task<IReadOnlyList<RuntimeApprovalStage>> BuildAsync(Wf_FlowInstance inst, FlowSchema schema, FlowNode node)
    {
        var result = new List<RuntimeApprovalStage>();

        // 单档兼容:无 Stages → 用节点既有字段一档(等价旧 EnterNode approval)
        if (node.Stages is null || node.Stages.Count == 0)
        {
            var rule = FlowEngine.BuildRule(node);   // 既有静态:从节点四字段建 ApproverRule(可能 null=无审批人)
            result.Add(new RuntimeApprovalStage
            {
                StageIndex = 0, Kind = ApprovalStageKinds.Fixed,
                Rule = rule ?? new ApproverRule(ApproverStrategy.Specified, null, null, null),
                Countersign = node.Countersign,
            });
            return result;
        }

        int idx = 0;
        foreach (var st in node.Stages)
        {
            var kind = (st.Kind ?? ApprovalStageKinds.Fixed).Trim();
            if (string.Equals(kind, ApprovalStageKinds.ManagerChain, StringComparison.OrdinalIgnoreCase))
            {
                int max = st.MaxLevels is int m && m >= 1 ? m : 1;
                Guid? prevResolved = null;
                for (int j = 1; j <= max; j++)
                {
                    var probe = await _approver.ResolveAsync(
                        new ApproverRule(ApproverStrategy.DirectManager, j, null, null),
                        new ApproverResolveContext { StarterUserId = inst.StarterId });
                    if (!probe.Resolved) break;                       // 无任何上级 → 链断
                    var resolvedId = probe.ApproverIds[0];
                    if (resolvedId == prevResolved) break;             // 链顶已被前档覆盖 → 停止展开
                    prevResolved = resolvedId;
                    result.Add(new RuntimeApprovalStage
                    {
                        StageIndex = idx++, Kind = ApprovalStageKinds.ManagerChain,
                        StageName = st.Name, StageCode = st.Code,
                        Rule = new ApproverRule(ApproverStrategy.DirectManager, j, null, null),
                        Countersign = string.IsNullOrWhiteSpace(st.Countersign) ? CountersignModes.All : st.Countersign,
                    });
                }
            }
            else // fixed
            {
                Enum.TryParse<ApproverStrategy>(st.ApproverStrategy, ignoreCase: true, out var strat);
                result.Add(new RuntimeApprovalStage
                {
                    StageIndex = idx++, Kind = ApprovalStageKinds.Fixed,
                    StageName = st.Name, StageCode = st.Code,
                    Rule = new ApproverRule(strat, st.ApproverLevels, st.ApproverRoleId, st.ApproverUserId),
                    Countersign = string.IsNullOrWhiteSpace(st.Countersign) ? CountersignModes.All : st.Countersign,
                });
            }
        }
        return result;
    }
}
