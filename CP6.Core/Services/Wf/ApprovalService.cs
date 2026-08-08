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

    private sealed record ConditionalBinding(string When, string FlowKey);

    public async Task<Guid> SubmitAsync(string bizType, string bizId, Guid starterId,
        object? formSnapshot = null, Guid? instanceId = null)
    {
        if (string.IsNullOrWhiteSpace(bizType)) throw new InvalidOperationException("bizType 必填");
        if (string.IsNullOrWhiteSpace(bizId)) throw new InvalidOperationException("bizId 必填");
        if (starterId == Guid.Empty) throw new InvalidOperationException("E-PUR-057");

        var binding = await _db.Wf_ApprovalBindings.FirstOrDefaultAsync(b => b.BizType == bizType && b.Enable)
                      ?? throw new InvalidOperationException("E-WF-031");
        var active = await _db.Wf_FlowInstances.AsNoTracking().FirstOrDefaultAsync(i =>
            i.BizType == bizType && i.BizId == bizId &&
            (i.Status == FlowInstanceStatus.Running || i.Status == FlowInstanceStatus.Suspended));
        if (active != null) throw new InvalidOperationException($"E-PUR-058:{active.Id}");

        var vars = formSnapshot is null ? "{}" : JsonSerializer.Serialize(formSnapshot,
            new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase });
        var selectedFlowKey = await SelectFlowAsync(binding.FlowKey, binding.ConditionJson, vars);
        return await _flow.SubmitAsync(selectedFlowKey, starterId, vars, bizType, bizId, instanceId);
    }

    private async Task<string> SelectFlowAsync(string fallbackFlowKey, string? conditionJson, string varsJson)
    {
        await AssertAvailableAsync(fallbackFlowKey);
        if (string.IsNullOrWhiteSpace(conditionJson)) return fallbackFlowKey;

        List<ConditionalBinding> rules;
        try
        {
            rules = JsonSerializer.Deserialize<List<ConditionalBinding>>(conditionJson,
                new JsonSerializerOptions { PropertyNameCaseInsensitive = true })
                ?? throw new JsonException();
        }
        catch (JsonException)
        {
            throw new InvalidOperationException("E-WF-032");
        }

        var vars = ExpressionEvaluator.ParseVars(varsJson);
        foreach (var rule in rules)
        {
            if (string.IsNullOrWhiteSpace(rule.When) || string.IsNullOrWhiteSpace(rule.FlowKey))
                throw new InvalidOperationException("E-WF-032");
            if (!ExpressionEvaluator.TryEvaluate(rule.When, vars, out var matches))
                throw new InvalidOperationException("E-WF-033");
            if (!matches) continue;
            await AssertAvailableAsync(rule.FlowKey);
            return rule.FlowKey;
        }
        return fallbackFlowKey;
    }

    private async Task AssertAvailableAsync(string flowKey)
    {
        var available = await (
            from head in _db.Wf_FlowDefs
            where head.FlowKey == flowKey && head.Enable
            join version in _db.Wf_FlowDefVersions on head.Id equals version.FlowDefId
            where version.Status == CP6.Entity.DomainModels.Wf.WfDefinitionVersionStatus.Published
            select version.Id).AnyAsync();
        if (!available) throw new InvalidOperationException("E-WF-034");
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
