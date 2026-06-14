using System.Text.Json;
using CP6.Core.EFDbContext;
using CP6.Entity.DomainModels.Wf;
using Microsoft.EntityFrameworkCore;

namespace CP6.Core.Services.Wf;

/// <summary>
/// 流程引擎状态机（OA 章03 §4/§5/§6）。SubmitAsync 建实例进首节点；ActAsync 办理(幂等)+会签判定+流转。
/// 会签三规则 EvaluateNodeCounts 抽为纯静态便于单测。审批人 → IApproverResolver；条件流转 → ConditionEvaluator。
/// </summary>
public class FlowEngine : IFlowEngine
{
    private static readonly JsonSerializerOptions JsonOpts = new() { PropertyNameCaseInsensitive = true };

    private readonly CP6Context _db;
    private readonly IApproverResolver _approver;
    public FlowEngine(CP6Context db, IApproverResolver approver) { _db = db; _approver = approver; }

    public async Task<Guid> SubmitAsync(string flowKey, Guid starterId, string varsJson, string? bizType = null, string? bizId = null)
    {
        var def = await _db.Wf_FlowDefs.FirstOrDefaultAsync(x => x.FlowKey == flowKey && x.Enable)
                  ?? throw new InvalidOperationException($"流程定义不存在或已停用：{flowKey}");
        var schema = Deserialize(def.SchemaJson);
        var first = FirstNode(schema) ?? throw new InvalidOperationException($"流程 {flowKey} 无节点");

        var inst = new Wf_FlowInstance
        {
            Id = Guid.NewGuid(),
            FlowKey = flowKey,
            BizType = bizType,
            BizId = bizId,
            VarsJson = string.IsNullOrWhiteSpace(varsJson) ? "{}" : varsJson,
            StarterId = starterId,
            Status = FlowInstanceStatus.Running,
            CurrentNode = first.Id,
            Creator = starterId.ToString(),
        };
        _db.Wf_FlowInstances.Add(inst);
        AddHistory(inst.Id, first.Id, starterId, "submit", null);

        await EnterNodeAsync(inst, schema, first);
        await _db.SaveChangesAsync();
        return inst.Id;
    }

    public async Task ActAsync(Guid taskId, Guid actorId, bool approve, string? comment = null)
    {
        var task = await _db.Wf_FlowTasks.FirstOrDefaultAsync(t => t.Id == taskId)
                   ?? throw new InvalidOperationException("任务不存在");
        if (task.Status != FlowTaskStatus.Pending) return;   // 幂等闸门：已办无效

        var inst = await _db.Wf_FlowInstances.FirstOrDefaultAsync(i => i.Id == task.InstanceId);
        if (inst is null || inst.Status != FlowInstanceStatus.Running) return;   // 实例已结束/挂起

        task.Status = approve ? FlowTaskStatus.Approved : FlowTaskStatus.Rejected;
        task.Comment = comment;
        task.Modifier = actorId.ToString();
        task.ModifyDate = DateTime.Now;
        AddHistory(inst.Id, task.NodeId, actorId, approve ? "approve" : "reject", comment);

        // 会签判定：取本节点全部任务（含刚改的，identity-map 反映未存修改）
        var nodeTasks = await _db.Wf_FlowTasks.Where(t => t.InstanceId == inst.Id && t.NodeId == task.NodeId).ToListAsync();
        int approved = nodeTasks.Count(t => t.Status == FlowTaskStatus.Approved);
        int rejected = nodeTasks.Count(t => t.Status == FlowTaskStatus.Rejected);
        var (decided, passed) = EvaluateNodeCounts(approved, rejected, nodeTasks.Count, task.Countersign);

        if (!decided)
        {
            await _db.SaveChangesAsync();   // 等其他会签人
            return;
        }

        CancelPendingTasks(nodeTasks);   // 节点已决，作废本节点其余在途
        if (passed)
        {
            var schema = await LoadSchemaAsync(inst.FlowKey);
            var node = FindNode(schema, task.NodeId);
            if (node is not null) await NextNodeAsync(inst, schema, node);
        }
        else
        {
            inst.Status = FlowInstanceStatus.Rejected;
        }
        await _db.SaveChangesAsync();
    }

    /// <summary>会签三规则（纯函数）。返回 (是否已决, 是否通过)。</summary>
    public static (bool decided, bool passed) EvaluateNodeCounts(int approved, int rejected, int total, string? countersign)
    {
        switch ((countersign ?? "all").Trim().ToLowerInvariant())
        {
            case "any":   // 或签：任一同意即过；全驳才否
                if (approved > 0) return (true, true);
                if (rejected >= total) return (true, false);
                return (false, false);
            case "veto":  // 一票否决：任一反对即死；全同意才过
            case "all":   // 会签：全同意才过；任一驳回即否
            default:
                if (rejected > 0) return (true, false);
                if (approved >= total) return (true, true);
                return (false, false);
        }
    }

    // ── 进入节点：end→通过；start→直穿；approval→算审批人建待办（缺位挂起） ──
    private async Task EnterNodeAsync(Wf_FlowInstance inst, FlowSchema schema, FlowNode node)
    {
        inst.CurrentNode = node.Id;

        if (IsType(node, "end"))
        {
            inst.Status = FlowInstanceStatus.Approved;
            AddHistory(inst.Id, node.Id, inst.StarterId, "end", null);
            return;
        }
        if (IsType(node, "start"))
        {
            await NextNodeAsync(inst, schema, node);
            return;
        }

        var rule = BuildRule(node);
        if (rule is null) { Suspend(inst, node, "节点未配置审批人"); return; }

        var res = await _approver.ResolveAsync(rule, new ApproverResolveContext { StarterUserId = inst.StarterId });
        if (!res.Resolved) { Suspend(inst, node, res.UnresolvedReason ?? "审批人无法解析"); return; }

        foreach (var uid in res.ApproverIds.Distinct())
        {
            _db.Wf_FlowTasks.Add(new Wf_FlowTask
            {
                Id = Guid.NewGuid(),
                InstanceId = inst.Id,
                NodeId = node.Id,
                AssigneeId = uid,
                Status = FlowTaskStatus.Pending,
                Countersign = node.Countersign,
            });
        }
    }

    // ── 流转：沿出边按序取首个条件为真者；无匹配则兜底结束 ──
    private async Task NextNodeAsync(Wf_FlowInstance inst, FlowSchema schema, FlowNode node)
    {
        foreach (var edge in schema.Edges.Where(e => e.From == node.Id))
        {
            if (!ConditionEvaluator.Evaluate(edge.Condition, inst.VarsJson)) continue;
            var target = FindNode(schema, edge.To);
            if (target is not null) { await EnterNodeAsync(inst, schema, target); return; }
        }
        inst.Status = FlowInstanceStatus.Approved;
        AddHistory(inst.Id, node.Id, inst.StarterId, "end", "无后继节点，自动结束");
    }

    private void Suspend(Wf_FlowInstance inst, FlowNode node, string reason)
    {
        inst.Status = FlowInstanceStatus.Suspended;
        AddHistory(inst.Id, node.Id, inst.StarterId, "suspend", reason);
    }

    private static void CancelPendingTasks(IEnumerable<Wf_FlowTask> tasks)
    {
        foreach (var t in tasks)
            if (t.Status == FlowTaskStatus.Pending) t.Status = FlowTaskStatus.Cancelled;
    }

    private void AddHistory(Guid instanceId, string nodeId, Guid actorId, string action, string? comment)
        => _db.Wf_FlowHistories.Add(new Wf_FlowHistory
        {
            Id = Guid.NewGuid(),
            InstanceId = instanceId,
            NodeId = nodeId,
            ActorId = actorId,
            Action = action,
            Comment = comment,
        });

    private async Task<FlowSchema> LoadSchemaAsync(string flowKey)
    {
        var def = await _db.Wf_FlowDefs.FirstOrDefaultAsync(x => x.FlowKey == flowKey)
                  ?? throw new InvalidOperationException($"流程定义不存在：{flowKey}");
        return Deserialize(def.SchemaJson);
    }

    private static FlowSchema Deserialize(string json)
        => JsonSerializer.Deserialize<FlowSchema>(json, JsonOpts) ?? new FlowSchema();

    private static FlowNode? FirstNode(FlowSchema s)
        => !string.IsNullOrEmpty(s.Start) ? FindNode(s, s.Start) : s.Nodes.FirstOrDefault();

    private static FlowNode? FindNode(FlowSchema s, string id) => s.Nodes.FirstOrDefault(n => n.Id == id);

    private static bool IsType(FlowNode n, string type) => string.Equals(n.Type, type, StringComparison.OrdinalIgnoreCase);

    private static ApproverRule? BuildRule(FlowNode n)
    {
        if (string.IsNullOrWhiteSpace(n.ApproverStrategy)) return null;
        if (!Enum.TryParse<ApproverStrategy>(n.ApproverStrategy, ignoreCase: true, out var strat)) return null;
        return new ApproverRule(strat, n.ApproverLevels, n.ApproverRoleId, n.ApproverUserId);
    }
}
