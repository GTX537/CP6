using System.Text.Json;
using CP6.Core.EFDbContext;
using CP6.Entity.DomainModels.Wf;
using Microsoft.EntityFrameworkCore;

namespace CP6.Core.Services.Wf;

/// <summary>
/// 流程引擎状态机（OA 章03 §4/§5/§6）。SubmitAsync 建实例进首节点；ActAsync 办理(幂等)+会签判定+流转。
/// 会签三规则 EvaluateNodeCounts 抽为纯静态便于单测。审批人 → IApproverResolver；条件流转 → ConditionEvaluator。
/// </summary>
public partial class FlowEngine : IFlowEngine
{
    private static readonly JsonSerializerOptions JsonOpts = new() { PropertyNameCaseInsensitive = true };

    private readonly CP6Context _db;
    private readonly IApproverResolver _approver;
    private readonly IWfNotifier _notifier;
    private readonly ApprovalDispatcher _dispatcher;

    public FlowEngine(CP6Context db, IApproverResolver approver, IWfNotifier? notifier = null, ApprovalDispatcher? dispatcher = null)
    {
        _db = db;
        _approver = approver;
        _notifier = notifier ?? new NullWfNotifier();   // 无 SignalR 环境/单测 → 空推送
        _dispatcher = dispatcher ?? new ApprovalDispatcher(Array.Empty<IApprovalCallback>());  // 无业务回调（纯 OA/单测）→ 空分发
    }

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
        await DispatchIfFinishedAsync(inst, starterId, null);   // 极少数"起即终态"（如 start→end）也分发，决策人记发起人
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

        // 前加签人审完 → 激活被挂起的原审批人任务（章07 §3），使其重新可办
        if (approve && task.AddSignSource == "before")
            await ReactivateSuspendedAsync(inst.Id, task.NodeId);

        // 会签判定：取本节点在途/已决任务（排除作废，避免退回重入旧轮任务串台；含刚改的，identity-map 反映未存修改）
        var nodeTasks = await _db.Wf_FlowTasks
            .Where(t => t.InstanceId == inst.Id && t.NodeId == task.NodeId && t.Status != FlowTaskStatus.Cancelled)
            .ToListAsync();
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
        await DispatchIfFinishedAsync(inst, actorId, comment);   // 终态 → 反向回调业务（原子：在最终 SaveChanges 前）
        await _db.SaveChangesAsync();
    }

    /// <summary>
    /// 若实例已达终态（通过/驳回），调终态分发器反向回调业务。<b>必须在最终 SaveChangesAsync 之前调用</b>：
    /// 回调与本引擎共享 scoped DbContext，回调若抛异常则流程终态与业务变更一并不落库（原子，OA2-D5）。
    /// </summary>
    private async Task DispatchIfFinishedAsync(Wf_FlowInstance inst, Guid decidedBy, string? reason)
    {
        if (inst.Status == FlowInstanceStatus.Approved)
            await _dispatcher.OnInstanceFinishedAsync(inst, approved: true, decidedBy, reason: null);
        else if (inst.Status == FlowInstanceStatus.Rejected)
            await _dispatcher.OnInstanceFinishedAsync(inst, approved: false, decidedBy, reason);
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

        // 重入节点（退回/循环）：先作废上一轮遗留任务，避免会签计票串台
        var stale = await _db.Wf_FlowTasks
            .Where(t => t.InstanceId == inst.Id && t.NodeId == node.Id && t.Status != FlowTaskStatus.Cancelled)
            .ToListAsync();
        foreach (var t in stale) t.Status = FlowTaskStatus.Cancelled;

        foreach (var uid in res.ApproverIds.Distinct())
        {
            var task = new Wf_FlowTask
            {
                Id = Guid.NewGuid(),
                InstanceId = inst.Id,
                NodeId = node.Id,
                AssigneeId = uid,
                Status = FlowTaskStatus.Pending,
                Countersign = node.Countersign,
            };
            _db.Wf_FlowTasks.Add(task);
            await _notifier.TodoCreatedAsync(uid, inst.Id, task.Id, inst.FlowKey);   // 推送待办（空实现=no-op）
        }
    }

    // ── 流转：沿出边按序取首个条件为真者；无匹配则兜底结束 ──
    private async Task NextNodeAsync(Wf_FlowInstance inst, FlowSchema schema, FlowNode node)
    {
        foreach (var edge in schema.Edges.Where(e => e.From == node.Id))
        {
            if (!ExpressionEvaluator.Evaluate(edge.Condition, inst.VarsJson)) continue;
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
            if (t.Status is FlowTaskStatus.Pending or FlowTaskStatus.Suspended) t.Status = FlowTaskStatus.Cancelled;
    }

    /// <summary>激活节点下被挂起的任务（前加签人审完后，原审批人任务 Suspended→Pending）。</summary>
    private async Task ReactivateSuspendedAsync(Guid instanceId, string nodeId)
    {
        var suspended = await _db.Wf_FlowTasks
            .Where(t => t.InstanceId == instanceId && t.NodeId == nodeId && t.Status == FlowTaskStatus.Suspended)
            .ToListAsync();
        foreach (var t in suspended) t.Status = FlowTaskStatus.Pending;
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
