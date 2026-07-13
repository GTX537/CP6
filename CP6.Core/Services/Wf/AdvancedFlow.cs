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

        // hardening C-T3 三规则（spec §5）：作用域判定取代旧 CrossesParallelBlock 一刀切禁令。
        // 先校验后写：SiblingBranch 拒绝发生在任何状态突变之前。旧数据无 token 维度 → 现行为全清场。
        // 作用域先于上游可达闸：兄弟支目标天然不在 current 上游（IsUpstreamReachable=false），
        // 须让 SiblingBranch → E-WF-019 抢先命中，再由上游闸兜住 BeforeSplit/SameBranch 的伪目标（E-WF-012）。
        var all = SnapshotTokens(inst.Id);
        var curTok = task.TokenId is Guid tid ? all.FirstOrDefault(t => t.Id == tid) : null;
        var (scope, strip) = curTok is null
            ? (SendBackScope.BeforeSplit, (Wf_FlowToken?)null)
            : SendBackScopeAnalyzer.Analyze(schema, all, curTok, task.NodeId, target.Id);
        if (scope == SendBackScope.SiblingBranch) throw new InvalidOperationException("E-WF-019");
        if (!IsUpstreamReachable(schema, target.Id, task.NodeId)) throw new InvalidOperationException("E-WF-012");

        if (scope == SendBackScope.SameBranch && strip is not null)
        {
            // 分支内剥离：只清剥离层子树（含内层 fork 兄弟），外层兄弟分支零扰动；
            // 重生 token 接管剥离层血缘 → 外层 join 认亲不破坏（spec §5.2 ★）
            CancelTokenSubtree(inst.Id, strip.Id);
            AddHistory(inst.Id, task.NodeId, actorId, "sendback", comment ?? $"退回至 {target.Id}");
            var reborn = SpawnToken(inst, target, parent: strip.ParentTokenId, fork: strip.ForkId);
            await EnterNodeAsync(inst, schema, target, reborn);
            return;
        }

        // BeforeSplit（含线性流 fork 栈为空）：既有全清场路径，逐字保留
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

    private async Task SendBackToPrevStageAsync(Wf_FlowInstance inst, FlowSchema schema, Wf_FlowTask task,
        Guid actorId, string? comment)
    {
        if (task.StageIndex <= 0) throw new InvalidOperationException("E-WF-012");   // 第 0 档无上一档
        var node = FindNode(schema, task.NodeId) ?? throw new InvalidOperationException("E-WF-012");

        // ① 本档本轮全部在途/挂起任务 → Cancelled
        var cur = await _db.Wf_FlowTasks.Where(t => t.InstanceId == inst.Id && t.NodeId == task.NodeId
            && t.TokenId == task.TokenId && t.StageIndex == task.StageIndex && t.StageRound == task.StageRound
            && (t.Status == FlowTaskStatus.Pending || t.Status == FlowTaskStatus.Suspended)).ToListAsync();
        foreach (var t in cur) t.Status = FlowTaskStatus.Cancelled;
        // ② 本档本轮 Pending 履历 → SentBack(非 Voided)
        VoidPendingFormTos(inst.Id, task.NodeId, task.TokenId, task.StageIndex, task.StageRound, FlowFormToStatus.SentBack);
        AddHistory(inst.Id, task.NodeId, actorId, "sendback", comment ?? $"退回上一档(档{task.StageIndex}→{task.StageIndex - 1})");

        // ③ 读冻结计划,重建上一档(StageRound 由 EnterStageAsync 自推导 +1),token 不 terminate
        var tok = await _db.Wf_FlowTokens.FirstAsync(t => t.Id == task.TokenId);
        var plan = System.Text.Json.JsonSerializer.Deserialize<List<RuntimeApprovalStage>>(tok.StagePlanJson!, JsonOpts)!;
        await EnterStageAsync(inst, schema, node, tok, plan, task.StageIndex - 1);
    }

    private async Task SendBackToStarterAsync(Wf_FlowInstance inst, Wf_FlowTask task, Guid actorId, string? comment)
    {
        var live = await _db.Wf_FlowTasks.Where(t => t.InstanceId == inst.Id
            && (t.Status == FlowTaskStatus.Pending || t.Status == FlowTaskStatus.Suspended)).ToListAsync();
        foreach (var t in live) t.Status = FlowTaskStatus.Cancelled;
        AddHistory(inst.Id, task.NodeId, actorId, "sendback", comment ?? "退回发起人重填");
        CancelAllActiveTokens(inst.Id);
        VoidPendingFormTos(inst.Id);                              // 全实例 Pending → Voided
        inst.Status = FlowInstanceStatus.Draft;                  // 回草稿,发起人改后 StartDraftAsync 从头跑
        inst.ModifyDate = DateTime.Now;
    }

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
}
