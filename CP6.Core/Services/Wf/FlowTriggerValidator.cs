// CP6.Core/Services/Wf/FlowTriggerValidator.cs（E-T1 最小版；F-T1 以 TDD 扩成 spec §5 全量校验）
using CP6.Core.EFDbContext;

namespace CP6.Core.Services.Wf;

public static class FlowTriggerValidator
{
    public static Task ValidateAsync(CP6Context db, FlowTriggerSaveReq req, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(req.FlowKey)) throw new InvalidOperationException("E-WF-023: FlowKey 必填");
        if (req.TriggerType is < WfTriggerType.Timer or > WfTriggerType.Message)
            throw new InvalidOperationException("E-WF-022: 触发器类型非法");
        if (req.StarterUserId == Guid.Empty) throw new InvalidOperationException("E-WF-022: StarterUserId 必填");
        return Task.CompletedTask;
    }
}
