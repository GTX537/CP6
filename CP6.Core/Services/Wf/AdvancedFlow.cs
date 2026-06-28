using CP6.Entity.DomainModels.Wf;
using Microsoft.EntityFrameworkCore;

namespace CP6.Core.Services.Wf;

/// <summary>
/// 流程引擎高级动作（OA 章07：退回 / 加签 / 委派）。作为 <see cref="FlowEngine"/> 的 partial，
/// 直接复用其 EnterNodeAsync/LoadSchemaAsync/AddHistory 等私有成员与同一 scoped DbContext。
/// 难点全在<b>动作后的状态清理</b>：退回作废在途待办、前加签挂起原任务，FlowHistory 永远只追加。
/// </summary>
public partial class FlowEngine
{
    /// <summary>单个节点加签层数上限（防无限加签，章07 §7）。</summary>
    private const int MaxAddSignPerNode = 10;

    public async Task<Guid> AddSignAsync(Guid taskId, Guid actorId, Guid addSigneeId, string source, string? comment = null)
    {
        var src = (source ?? string.Empty).Trim().ToLowerInvariant();
        if (src is not ("before" or "after")) throw new InvalidOperationException("加签来源只能是 before 或 after");

        var task = await _db.Wf_FlowTasks.FirstOrDefaultAsync(t => t.Id == taskId)
                   ?? throw new InvalidOperationException("任务不存在");
        if (task.Status != FlowTaskStatus.Pending) throw new InvalidOperationException("只能在待办任务上加签");

        var inst = await _db.Wf_FlowInstances.FirstOrDefaultAsync(i => i.Id == task.InstanceId);
        if (inst is null || inst.Status != FlowInstanceStatus.Running) throw new InvalidOperationException("流程已结束，不能加签");

        // 加签层数上限（本节点现有加签任务数）
        var addSignCount = await _db.Wf_FlowTasks.CountAsync(t =>
            t.InstanceId == inst.Id && t.NodeId == task.NodeId && t.AddSignSource != null);
        if (addSignCount >= MaxAddSignPerNode) throw new InvalidOperationException($"加签层数超上限（{MaxAddSignPerNode}）");

        // 前加签：发起加签的原任务挂起，待加签人审完再激活
        if (src == "before") task.Status = FlowTaskStatus.Suspended;

        var addTask = new Wf_FlowTask
        {
            Id = Guid.NewGuid(),
            InstanceId = inst.Id,
            NodeId = task.NodeId,
            AssigneeId = addSigneeId,
            Status = FlowTaskStatus.Pending,
            Countersign = task.Countersign,   // 同节点会签规则，计入 EvaluateNodeCounts
            AddSignSource = src,
            TokenId = task.TokenId,           // ★ T4：随原任务同 token，会签计票方与原任务同框
        };
        _db.Wf_FlowTasks.Add(addTask);
        AddHistory(inst.Id, task.NodeId, actorId, "addsign", comment ?? $"{(src == "before" ? "前" : "后")}加签 → {addSigneeId}");
        await _notifier.TodoCreatedAsync(addSigneeId, inst.Id, addTask.Id, inst.FlowKey);

        await _db.SaveChangesAsync();
        return addTask.Id;
    }

    public async Task<Guid> SetDelegateAsync(Guid grantorId, Guid delegateId, DateTime validFrom, DateTime validTo,
        string? scope = null, string? remark = null)
    {
        if (grantorId == delegateId) throw new InvalidOperationException("不能委派给自己");
        if (validTo < validFrom) throw new InvalidOperationException("委派失效时间不能早于生效时间");

        var d = new Wf_FlowDelegate
        {
            Id = Guid.NewGuid(),
            GrantorId = grantorId,
            DelegateId = delegateId,
            ValidFrom = validFrom,
            ValidTo = validTo,
            Enable = true,
            Scope = scope,
            Remark = remark,
            Creator = grantorId.ToString(),
        };
        _db.Wf_FlowDelegates.Add(d);
        await _db.SaveChangesAsync();
        return d.Id;
    }

    public async Task TransferAsync(Guid taskId, Guid actorId, Guid toUserId, string? comment = null)
    {
        var task = await _db.Wf_FlowTasks.FirstOrDefaultAsync(t => t.Id == taskId)
                   ?? throw new InvalidOperationException("E-WF-002");
        if (task.Status != FlowTaskStatus.Pending) throw new InvalidOperationException("E-WF-002");

        var inst = await _db.Wf_FlowInstances.FirstOrDefaultAsync(i => i.Id == task.InstanceId);
        if (inst is null || inst.Status != FlowInstanceStatus.Running) throw new InvalidOperationException("E-WF-002");

        // 目标须同租户真实用户、且非当前处理人（同租户由全局过滤器保证；查不到=越租户/不存在）
        var toExists = await _db.Sys_Users.AnyAsync(u => u.Id == toUserId);
        if (!toExists || toUserId == task.AssigneeId) throw new InvalidOperationException("E-WF-002");

        var fromUserId = task.AssigneeId;
        await TransferFormToAsync(inst.Id, task.NodeId, task.TokenId, fromUserId, toUserId, comment);

        task.AssigneeId = toUserId;          // 改派（保 TokenId/NodeId/Countersign 不变）
        AddHistory(inst.Id, task.NodeId, actorId, "transfer", comment);
        await _notifier.TodoCreatedAsync(toUserId, inst.Id, task.Id, inst.FlowKey);
        await _db.SaveChangesAsync();
    }

    // 旧重载保留 → 转发 node(既有调用方零感知)
    public Task SendBackAsync(Guid taskId, Guid actorId, string targetNodeId, string? comment = null)
        => SendBackAsync(taskId, actorId, new SendBackTarget("node", targetNodeId), comment);

    public async Task SendBackAsync(Guid taskId, Guid actorId, SendBackTarget target, string? comment = null)
    {
        var task = await _db.Wf_FlowTasks.FirstOrDefaultAsync(t => t.Id == taskId)
                   ?? throw new InvalidOperationException("任务不存在");
        if (task.Status != FlowTaskStatus.Pending) return;   // 幂等闸门

        var inst = await _db.Wf_FlowInstances.FirstOrDefaultAsync(i => i.Id == task.InstanceId);
        if (inst is null || inst.Status != FlowInstanceStatus.Running) return;
        var schema = await LoadSchemaAsync(inst.FlowKey);

        switch ((target.Kind ?? "node").Trim().ToLowerInvariant())
        {
            case "node":      await SendBackToNodeAsync(inst, schema, task, actorId, target.NodeId, comment); break;
            case "prevstage": await SendBackToPrevStageAsync(inst, schema, task, actorId, comment); break;   // T6
            case "starter":   await SendBackToStarterAsync(inst, task, actorId, comment); break;             // T6
            default: throw new InvalidOperationException("E-WF-012");
        }
        await _db.SaveChangesAsync();
    }

    private async Task SendBackToNodeAsync(Wf_FlowInstance inst, FlowSchema schema, Wf_FlowTask task,
        Guid actorId, string? targetNodeId, string? comment)
    {
        var target = FindNode(schema, targetNodeId ?? "") ?? throw new InvalidOperationException("E-WF-012");
        if (IsType(target, "end") || target.Id == task.NodeId) throw new InvalidOperationException("E-WF-012");
        var tt = (target.Type ?? "approval").Trim().ToLowerInvariant();
        if (tt != "approval" && tt != "start") throw new InvalidOperationException("E-WF-012");
        if (!IsUpstreamReachable(schema, target.Id, task.NodeId)) throw new InvalidOperationException("E-WF-012");
        if (CrossesParallelBlock(schema, target.Id, task.NodeId)) throw new InvalidOperationException("E-WF-012");

        var live = await _db.Wf_FlowTasks
            .Where(t => t.InstanceId == inst.Id && (t.Status == FlowTaskStatus.Pending || t.Status == FlowTaskStatus.Suspended))
            .ToListAsync();
        foreach (var t in live) t.Status = FlowTaskStatus.Cancelled;
        AddHistory(inst.Id, task.NodeId, actorId, "sendback", comment ?? $"退回至 {target.Id}");
        CancelAllActiveTokens(inst.Id);
        VoidPendingFormTos(inst.Id);
        var sbToken = SpawnToken(inst, target, parent: null, fork: null);
        await EnterNodeAsync(inst, schema, target, sbToken);
    }

    // T6 实现 —— 暂置桩,保编译;本任务测试不触达
    private Task SendBackToPrevStageAsync(Wf_FlowInstance inst, FlowSchema schema, Wf_FlowTask task, Guid actorId, string? comment)
        => throw new NotImplementedException("T6: prevStage");
    private Task SendBackToStarterAsync(Wf_FlowInstance inst, Wf_FlowTask task, Guid actorId, string? comment)
        => throw new NotImplementedException("T6: starter");

    /// <summary>target 能否沿边正向到达 current(即 target 是 current 的上游)。</summary>
    private static bool IsUpstreamReachable(FlowSchema schema, string targetId, string currentId)
    {
        var seen = new HashSet<string> { targetId };
        var q = new Queue<string>(); q.Enqueue(targetId);
        while (q.Count > 0)
        {
            var cur = q.Dequeue();
            foreach (var e in schema.Edges.Where(e => e.From == cur))
            {
                if (e.To == currentId) return true;
                if (seen.Add(e.To)) q.Enqueue(e.To);
            }
        }
        return false;
    }

    /// <summary>v1:target→current 路径若经过 parallelSplit/parallelJoin 则视为跨并行块,禁止。</summary>
    private static bool CrossesParallelBlock(FlowSchema schema, string targetId, string currentId)
    {
        var between = NodesBetween(schema, targetId, currentId);
        return between.Any(id => schema.Nodes.Any(n => n.Id == id &&
            ((n.Type ?? "").Equals("parallelSplit", StringComparison.OrdinalIgnoreCase) ||
             (n.Type ?? "").Equals("parallelJoin", StringComparison.OrdinalIgnoreCase))));
    }

    private static HashSet<string> NodesBetween(FlowSchema schema, string fromId, string toId)
    {
        var fwd = new HashSet<string>(); var q = new Queue<string>(); q.Enqueue(fromId);
        while (q.Count > 0) { var c = q.Dequeue(); foreach (var e in schema.Edges.Where(e => e.From == c)) if (fwd.Add(e.To)) q.Enqueue(e.To); }
        fwd.Remove(toId);
        return fwd;
    }
}
