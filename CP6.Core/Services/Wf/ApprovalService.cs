using System.Text.Json;
using CP6.Core.EFDbContext;
using Microsoft.EntityFrameworkCore;

namespace CP6.Core.Services.Wf;

/// <summary>
/// 审批服务实现（OA 章05 §2/§3）。业务侧入口：防重 → 按绑定选 FlowKey → 委托 FlowEngine 起流程。
/// 不碰业务表，formSnapshot 序列化进实例 VarsJson 作条件流转/规则取值源（OA 终态不回查业务）。
/// </summary>
public class ApprovalService : IApprovalService
{
    private readonly CP6Context _db;
    private readonly IFlowEngine _flow;

    public ApprovalService(CP6Context db, IFlowEngine flow)
    {
        _db = db;
        _flow = flow;
    }

    public async Task<Guid> SubmitAsync(string bizType, string bizId, Guid starterId, object? formSnapshot = null)
    {
        if (string.IsNullOrWhiteSpace(bizType)) throw new InvalidOperationException("bizType 必填");
        if (string.IsNullOrWhiteSpace(bizId)) throw new InvalidOperationException("bizId 必填");

        // 防重：同一业务单据不允许并行两条进行中审批
        if (await _db.Wf_FlowInstances.AnyAsync(i =>
                i.BizType == bizType && i.BizId == bizId && i.Status == FlowInstanceStatus.Running))
            throw new InvalidOperationException("该单据已有进行中的审批");

        var binding = await _db.Wf_ApprovalBindings.FirstOrDefaultAsync(b => b.BizType == bizType && b.Enable)
                      ?? throw new InvalidOperationException($"未配置 {bizType} 的审批流程绑定");

        var vars = formSnapshot is null ? "{}" : JsonSerializer.Serialize(formSnapshot);
        return await _flow.SubmitAsync(binding.FlowKey, starterId, vars, bizType, bizId);
    }

    public async Task<ApprovalStatus> GetStatusAsync(string bizType, string bizId)
    {
        var inst = await _db.Wf_FlowInstances
            .Where(i => i.BizType == bizType && i.BizId == bizId)
            .OrderByDescending(i => i.CreateDate)
            .FirstOrDefaultAsync();
        return inst is null ? ApprovalStatus.None : (ApprovalStatus)inst.Status;
    }
}
