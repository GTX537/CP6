using CP6.Core.EFDbContext;
using CP6.Entity.DomainModels.Wf;
using Microsoft.EntityFrameworkCore;

namespace CP6.Core.Services.Wf;

public partial class FlowEngine
{
    // 供 handler 经 ctx.Engine 复用（InternalsVisibleTo CP6.Tests）
    internal CP6Context Db => _db;
    internal IApproverResolver Approver => _approver;
    internal IWfNotifier Notifier => _notifier;
    internal IApprovalStagePlanner Planner => _planner;

    /// <summary>生一个 Active token 停在 node。parent/fork 串血缘（根皆 null）。不落盘。</summary>
    internal Wf_FlowToken SpawnToken(Wf_FlowInstance inst, FlowNode node, Guid? parent = null, Guid? fork = null)
    {
        var tok = new Wf_FlowToken
        {
            Id = Guid.NewGuid(), InstanceId = inst.Id, NodeId = node.Id,
            Status = FlowTokenStatus.Active, ParentTokenId = parent, ForkId = fork,
            Creator = inst.StarterId.ToString(),
        };
        _db.Wf_FlowTokens.Add(tok);   // TenantId 由 StampTenant 自动盖
        return tok;
    }

    /// <summary>消费 token（正常退场）。带 Active 守卫 → 重放 no-op。</summary>
    internal void ConsumeToken(Wf_FlowToken token)
    {
        if (token.Status != FlowTokenStatus.Active) return;
        token.Status = FlowTokenStatus.Consumed;
    }

    /// <summary>本实例 token 快照：Local（含本回合未落盘的）∪ DB（已落盘的），按引用去重
    /// （EF 身份映射保证同实体同引用）。口径抽自 ParallelJoinNodeHandler.AllTokens，供双 join 动态计票、
    /// 剪枝递归、退回作用域分析共用。</summary>
    internal IReadOnlyList<Wf_FlowToken> SnapshotTokens(Guid instanceId)
        => _db.Wf_FlowTokens.Local.Where(t => t.InstanceId == instanceId)
            .Concat(_db.Wf_FlowTokens.Where(t => t.InstanceId == instanceId).AsEnumerable())
            .Distinct().ToList();

    /// <summary>驳回连坐 / 退回 / 撤回清场：本实例全 Active token → Cancelled。
    /// 与 <see cref="HasActiveToken"/>/VoidPendingFormTos 同款安全合并：先按 Local（变更追踪器权威态）改，
    /// 再补查 DB 中"未被本地追踪"的行（排除已在 Local 的 Id），避免把"DB 仍 Active 但本地已 Consumed"的 token 误翻 Cancelled。
    /// <para>B-T3（P0-5 入队侧）：同时把该实例 <c>Status==Pending</c> 的 <see cref="Wf_ServiceJob"/> 行标
    /// <c>Cancelled</c>（同款 Local + localIds-exclusion 惯用法），让扫描 worker 永不唤醒已终止实例的停泊 token。
    /// <c>Status==Running</c> 的 job 不强杀——由 B-T1 <c>ScanOnceAsync</c> 执行前状态闸（§4.2 P0-5）在 worker 侧处理。</para></summary>
    internal void CancelAllActiveTokens(Guid instanceId)
    {
        // ── token 清场（既有逻辑，字节等价） ──
        foreach (var t in _db.Wf_FlowTokens.Local
            .Where(t => t.InstanceId == instanceId && t.Status == FlowTokenStatus.Active).ToList())
            t.Status = FlowTokenStatus.Cancelled;
        var localIds = _db.Wf_FlowTokens.Local.Where(t => t.InstanceId == instanceId).Select(t => t.Id).ToHashSet();
        foreach (var t in _db.Wf_FlowTokens
            .Where(t => t.InstanceId == instanceId && t.Status == FlowTokenStatus.Active && !localIds.Contains(t.Id)).ToList())
            t.Status = FlowTokenStatus.Cancelled;

        // ── B-T3: Pending 服务任务 job 清场（同款 Local + localIds-exclusion 惯用法） ──
        var now = DateTime.UtcNow;
        foreach (var j in _db.Wf_ServiceJobs.Local
            .Where(j => j.InstanceId == instanceId && j.Status == ServiceJobStatus.Pending).ToList())
        {
            j.Status = ServiceJobStatus.Cancelled;
            j.CompletedAtUtc = now;
        }
        var localJobIds = _db.Wf_ServiceJobs.Local
            .Where(j => j.InstanceId == instanceId).Select(j => j.Id).ToHashSet();
        foreach (var j in _db.Wf_ServiceJobs
            .Where(j => j.InstanceId == instanceId && j.Status == ServiceJobStatus.Pending && !localJobIds.Contains(j.Id)).ToList())
        {
            j.Status = ServiceJobStatus.Cancelled;
            j.CompletedAtUtc = now;
        }
    }

    /// <summary>本 token 的在途/挂起任务 → Cancelled（剪枝/子树清场用）。Local + localIds-exclusion 惯用法。</summary>
    internal void CancelPendingTasksOfToken(Guid instanceId, Guid tokenId)
    {
        foreach (var t in _db.Wf_FlowTasks.Local.Where(t => t.InstanceId == instanceId && t.TokenId == tokenId
            && (t.Status == FlowTaskStatus.Pending || t.Status == FlowTaskStatus.Suspended)).ToList())
            t.Status = FlowTaskStatus.Cancelled;
        var localIds = _db.Wf_FlowTasks.Local.Where(t => t.InstanceId == instanceId).Select(t => t.Id).ToHashSet();
        foreach (var t in _db.Wf_FlowTasks.Where(t => t.InstanceId == instanceId && t.TokenId == tokenId
            && (t.Status == FlowTaskStatus.Pending || t.Status == FlowTaskStatus.Suspended)
            && !localIds.Contains(t.Id)).ToList())
            t.Status = FlowTaskStatus.Cancelled;
    }

    /// <summary>剥离层子树清场（hardening spec §5.2 SameBranch）：root 及其 ParentTokenId 后代闭包内
    /// Active token → Cancelled；这些 token 的 Pending/Suspended 任务 → Cancelled、Pending FormTo → Voided、
    /// Pending ServiceJob → Cancelled。绝不触碰子树外（兄弟分支零扰动）、绝不改 inst.Status。
    /// 闭包正确性：join 续 token 血缘「上弹一层」重挂剥离层同级，故任何在途延续 token 要么在闭包内、
    /// 要么本身就是作用域分析选出的剥离层（侦察结论表第 3 行论证）。</summary>
    internal void CancelTokenSubtree(Guid instanceId, Guid rootTokenId)
    {
        var all = SnapshotTokens(instanceId);
        var subtree = new HashSet<Guid> { rootTokenId };
        bool grew = true;
        while (grew)
        {
            grew = false;
            foreach (var t in all)
                if (t.ParentTokenId is Guid p && subtree.Contains(p) && subtree.Add(t.Id)) grew = true;
        }
        foreach (var t in all)
            if (subtree.Contains(t.Id) && t.Status == FlowTokenStatus.Active)
                t.Status = FlowTokenStatus.Cancelled;
        foreach (var id in subtree)
        {
            CancelPendingTasksOfToken(instanceId, id);
            VoidPendingFormTos(instanceId, tokenId: id);
            CancelPendingServiceJobsOfToken(instanceId, id);
        }
    }

    /// <summary>本 token 的 Pending 服务作业 → Cancelled（镜像 CancelAllActiveTokens 的 B-T3 job 清场，tokenId 过滤）。</summary>
    internal void CancelPendingServiceJobsOfToken(Guid instanceId, Guid tokenId)
    {
        var now = DateTime.UtcNow;
        foreach (var j in _db.Wf_ServiceJobs.Local.Where(j => j.InstanceId == instanceId && j.TokenId == tokenId
            && j.Status == ServiceJobStatus.Pending).ToList())
        { j.Status = ServiceJobStatus.Cancelled; j.CompletedAtUtc = now; }
        var localJobIds = _db.Wf_ServiceJobs.Local.Where(j => j.InstanceId == instanceId).Select(j => j.Id).ToHashSet();
        foreach (var j in _db.Wf_ServiceJobs.Where(j => j.InstanceId == instanceId && j.TokenId == tokenId
            && j.Status == ServiceJobStatus.Pending && !localJobIds.Contains(j.Id)).ToList())
        { j.Status = ServiceJobStatus.Cancelled; j.CompletedAtUtc = now; }
    }

    /// <summary>无 Active token 残留 ⇒ 实例正常通过（置 Approved；dispatch 由调用方在 SaveChanges 前做）。</summary>
    internal void FinishIfDrained(Wf_FlowInstance inst)
    {
        if (HasActiveToken(inst.Id)) return;
        if (inst.Status != FlowInstanceStatus.Running) return;   // 已驳回/撤回，不覆盖
        inst.Status = FlowInstanceStatus.Approved;
    }

    /// <summary>本实例是否仍有 Active token。变更追踪器(Local)对已加载 token 是权威态（含本回合未落盘的 consume/cancel）；
    /// DB 仅补查"未被本地追踪"的 token（如并行兄弟分支上回合落盘的），<b>排除已在 Local 的 Id 避免读到落盘旧值</b>。</summary>
    private bool HasActiveToken(Guid instanceId)
    {
        if (_db.Wf_FlowTokens.Local.Any(t => t.InstanceId == instanceId && t.Status == FlowTokenStatus.Active))
            return true;
        var localIds = _db.Wf_FlowTokens.Local.Where(t => t.InstanceId == instanceId).Select(t => t.Id).ToHashSet();
        return _db.Wf_FlowTokens
            .Any(t => t.InstanceId == instanceId && t.Status == FlowTokenStatus.Active && !localIds.Contains(t.Id));
    }

    /// <summary>token 排他流转：沿出边取首个条件为真者，改 token.NodeId 后进新节点。不消费。
    /// 无后继 → 消费 token + drained 判定（等价旧 NextNodeAsync 兜底结束）。单 token 线性=零差异。
    /// <para>D8 不变量（spec §4.3/§4.4）：成功路径<b>绝不</b>走错误边 —— 过滤 <c>IsError != true</c>。
    /// 既有边 <c>IsError==null</c> → <c>null != true</c> 为真 → 字节等价；仅服务任务重试耗尽经
    /// <see cref="AdvanceAlongErrorEdge"/> 走 <c>IsError==true</c> 的边。</para></summary>
    internal async Task AdvanceToken(Wf_FlowInstance inst, FlowSchema schema, Wf_FlowToken token)
    {
        foreach (var edge in schema.Edges.Where(e => e.From == token.NodeId && e.IsError != true))
        {
            if (!ExpressionEvaluator.Evaluate(edge.Condition, inst.VarsJson)) continue;
            var target = FindNode(schema, edge.To);
            if (target is not null)
            {
                if (edge.CcUsers is { Count: > 0 }) await WriteCcAsync(inst, edge.To, edge.CcUsers, null);
                token.NodeId = target.Id; await EnterNodeAsync(inst, schema, target, token); return;
            }
        }
        ConsumeToken(token);
        AddHistory(inst.Id, token.NodeId, inst.StarterId, "end", "无后继节点，自动结束");
        FinishIfDrained(inst);
    }

    /// <summary>服务任务失败专用流转（spec §4.3/§4.4）：沿本节点 <c>IsError==true</c> 的首条出边推进
    /// （改 token.NodeId 后进目标节点，不消费）；无错误边 → 挂起（Suspend）。
    /// 与 <see cref="AdvanceToken"/> 互补：前者跳错误边、后者只走错误边。错误边 ≤1（spec §6.1 E-WF-017）。</summary>
    internal async Task AdvanceAlongErrorEdge(Wf_FlowInstance inst, FlowSchema schema, Wf_FlowToken token)
    {
        foreach (var edge in schema.Edges.Where(e => e.From == token.NodeId && e.IsError == true))
        {
            var target = FindNode(schema, edge.To);
            if (target is not null)
            {
                if (edge.CcUsers is { Count: > 0 }) await WriteCcAsync(inst, edge.To, edge.CcUsers, null);
                token.NodeId = target.Id; await EnterNodeAsync(inst, schema, target, token); return;
            }
        }
        var node = FindNode(schema, token.NodeId);
        if (node is not null) Suspend(inst, node, "服务任务失败且无错误边");
    }

    /// <summary>approval 超时走失败边（infra ②，spec §3）。作废该节点在途待办（<b>节点级</b>，仿退回口径
    /// <see cref="AdvancedFlow"/> §5.2，不用实例级驳回连坐，绝不误伤兄弟分支）+ 注入 timeoutError 变量 +
    /// <see cref="AdvanceAlongErrorEdge"/> 路由。<b>幂等</b>：任务非 Pending / 实例非 Running / 无 Active token → 零动作
    /// （防超时扫描重投二次推进）。不 SaveChanges——由 <see cref="WfTimeoutService.ScanOnceAsync"/> 尾统一保存。</summary>
    public async Task TimeoutAdvanceErrorEdgeAsync(Guid taskId, Guid actorId, CancellationToken ct = default)
    {
        var task = await _db.Wf_FlowTasks.FirstOrDefaultAsync(t => t.Id == taskId, ct);
        if (task is null || task.Status != FlowTaskStatus.Pending) return;
        var inst = await _db.Wf_FlowInstances.FirstOrDefaultAsync(i => i.Id == task.InstanceId, ct);
        if (inst is null || inst.Status != FlowInstanceStatus.Running) return;
        var schema = Deserialize((await _db.Wf_FlowDefs.FirstAsync(d => d.FlowKey == inst.FlowKey, ct)).SchemaJson);
        var token = await _db.Wf_FlowTokens.FirstOrDefaultAsync(
            t => t.InstanceId == inst.Id && t.NodeId == task.NodeId && t.Status == FlowTokenStatus.Active, ct);
        if (token is null) return;

        // ① 节点级作废在途待办（对齐退回清理口径 AdvancedFlow.cs SameBranch，不用实例级驳回连坐）
        var cur = await _db.Wf_FlowTasks
            .Where(t => t.InstanceId == inst.Id && t.NodeId == task.NodeId && t.Status == FlowTaskStatus.Pending).ToListAsync(ct);
        foreach (var t in cur) t.Status = FlowTaskStatus.Cancelled;
        VoidPendingFormTos(inst.Id, task.NodeId, token.Id, task.StageIndex, task.StageRound, FlowFormToStatus.SentBack);

        // ② 注入错误变量（口径同 serviceTask 失败：结构化 key）
        inst.VarsJson = ServiceVarsHelper.MergeOutputVars(inst.VarsJson, new Dictionary<string, object?>
        {
            ["timeoutError"] = new { nodeId = task.NodeId, dueAt = task.DueAt },
        }).VarsJson;
        AddHistory(inst.Id, task.NodeId, actorId, "timeoutErrorEdge", $"审批超时走失败边（node={task.NodeId}）");

        // ③ 沿错误边路由（无边则 Suspend——但 E-WF-027 静态已保证有边）
        await AdvanceAlongErrorEdge(inst, schema, token);
        // 不 SaveChanges——WfTimeoutService.ScanOnceAsync 尾统一保存
    }
}
