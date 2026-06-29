using System.Text.Json;
using System.Text.Json.Nodes;
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
    private readonly IReadOnlyDictionary<string, INodeHandler> _handlers;
    private readonly IApprovalStagePlanner _planner;

    public FlowEngine(CP6Context db, IApproverResolver approver, IWfNotifier? notifier = null,
                      ApprovalDispatcher? dispatcher = null, IEnumerable<INodeHandler>? handlers = null,
                      IApprovalStagePlanner? planner = null)
    {
        _db = db;
        _approver = approver;
        _notifier = notifier ?? new NullWfNotifier();   // 无 SignalR 环境/单测 → 空推送
        _dispatcher = dispatcher ?? new ApprovalDispatcher(Array.Empty<IApprovalCallback>());  // 无业务回调（纯 OA/单测）→ 空分发
        _handlers = (handlers ?? DefaultHandlers()).ToDictionary(h => h.Type, StringComparer.OrdinalIgnoreCase);
        _planner = planner ?? new ApprovalStagePlanner(_approver);   // 测试 Engine(db) 不传 → 内部 new,保 Wf 测绿
    }

    // ★ T5：start/approval/end + parallelSplit/parallelJoin 五 handler。
    private static IEnumerable<INodeHandler> DefaultHandlers() => new INodeHandler[]
    {
        new StartNodeHandler(), new ApprovalNodeHandler(), new EndNodeHandler(),
        new ParallelSplitNodeHandler(), new ParallelJoinNodeHandler(),
    };

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

        var root = SpawnToken(inst, first, parent: null, fork: null);
        await EnterNodeAsync(inst, schema, first, root);
        await DispatchIfFinishedAsync(inst, starterId, null);   // 极少数"起即终态"（如 start→end）也分发，决策人记发起人
        await _db.SaveChangesAsync();
        return inst.Id;
    }

    /// <summary>
    /// 就地起草稿：把 Draft 实例推进进流程（spawn 根 token + 进首节点 + 读模型随推进落库）。
    /// 仅发起人可提交；非草稿态/越权 → E-WF-003。幂等性同 SubmitAsync（一次 SaveChanges）。
    /// </summary>
    public async Task StartDraftAsync(Guid instanceId, Guid actorId)
    {
        var inst = await _db.Wf_FlowInstances.FirstOrDefaultAsync(i => i.Id == instanceId)
                   ?? throw new InvalidOperationException("E-WF-003");
        if (inst.StarterId != actorId) throw new InvalidOperationException("E-WF-003");        // 越权提交
        if (inst.Status != FlowInstanceStatus.Draft) throw new InvalidOperationException("E-WF-003"); // 非草稿态

        var schema = await LoadSchemaAsync(inst.FlowKey);
        var first = FirstNode(schema) ?? throw new InvalidOperationException($"流程 {inst.FlowKey} 无节点");

        inst.Status = FlowInstanceStatus.Running;
        inst.CurrentNode = first.Id;
        inst.Modifier = actorId.ToString();
        inst.ModifyDate = DateTime.Now;
        AddHistory(inst.Id, first.Id, actorId, "submit", null);

        var root = SpawnToken(inst, first, parent: null, fork: null);
        await EnterNodeAsync(inst, schema, first, root);
        await DispatchIfFinishedAsync(inst, actorId, null);
        await _db.SaveChangesAsync();
    }

    /// <summary>
    /// 办理外壳（WFS P1 Task 6 并发幂等）：把单次办理委托给 <see cref="ActOnceAsync"/>，
    /// 遇乐观并发冲突（并行兄弟分支近同时办结，join 计数脏读 → <see cref="DbUpdateConcurrencyException"/>）
    /// 则重读全部追踪实体后重试，最多 3 次（attempt 0/1/2）。重试时重读 inst/token/task → 重算 join
    /// 计数，序列化"双 1/2 丢失唤醒"竞态。单线程下首次即成功返回，行为零变化。
    /// </summary>
    public async Task ActAsync(Guid taskId, Guid actorId, bool approve, string? comment = null)
    {
        for (int attempt = 0; ; attempt++)
        {
            try { await ActOnceAsync(taskId, actorId, approve, comment); return; }
            catch (DbUpdateConcurrencyException) when (attempt < 2)
            {
                // 败方重读全部追踪实体（拿到胜方已落库的 token/inst RowVersion）→ 重试重算 join 计数
                foreach (var e in _db.ChangeTracker.Entries().ToList()) await e.ReloadAsync();
            }
        }
    }

    /// <summary>
    /// act-as 办理：actorId（代理人 me）代 onBehalfOf（被代理人 X）办理其待办。
    /// 办理逻辑与 ActAsync 等价（推进/计票），但履历 ActualHandlerId = actorId (me)、OnBehalfOfId = onBehalfOf (X)。
    /// onBehalfOf = null 时行为同 ActAsync（既有路径零改）。授权由控制器 AssertActiveGrant 把关，引擎不查委派。
    /// </summary>
    public async Task ActAsAsync(Guid taskId, Guid actorId, Guid? onBehalfOf, bool approve, string? comment = null)
    {
        for (int attempt = 0; ; attempt++)
        {
            try { await ActOnceAsync(taskId, actorId, approve, comment, onBehalfOf); return; }
            catch (DbUpdateConcurrencyException) when (attempt < 2)
            {
                foreach (var e in _db.ChangeTracker.Entries().ToList()) await e.ReloadAsync();
            }
        }
    }

    private async Task ActOnceAsync(Guid taskId, Guid actorId, bool approve, string? comment = null,
        Guid? onBehalfOf = null)
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
        await UpdateFormToOnHandleAsync(task, actorId, approve, comment, onBehalfOf);   // ★ T9：更新传签履历办结状态；act-as 时 actorId=实办人，onBehalfOf=被代理人

        // ★ T10：办结时存一份该关卡表单快照（与送签快照同 StepSeq，形成"入→出"两条留痕）
        var doneTok = await _db.Wf_FlowTokens.FirstOrDefaultAsync(t => t.Id == task.TokenId);
        if (doneTok is not null)
        {
            var seq = await _db.Wf_FlowFormTos
                .Where(f => f.InstanceId == inst.Id && f.NodeId == task.NodeId && f.TokenId == task.TokenId)
                .Select(f => (int?)f.StepSeq).MaxAsync() ?? NextStepSeq(inst.Id);
            var snapNode = FindNode(await LoadSchemaAsync(inst.FlowKey), task.NodeId);
            if (snapNode is not null) WriteSnapshot(inst, snapNode, doneTok, seq);
        }

        // 前加签人审完 → 激活被挂起的原审批人任务（章07 §3），使其重新可办
        if (approve && task.AddSignSource == "before")
            await ReactivateSuspendedAsync(inst.Id, task.NodeId);

        // 会签判定：取本 token 本节点在途/已决任务（排除作废，避免退回重入旧轮任务串台；含刚改的，identity-map 反映未存修改）
        var nodeTasks = await _db.Wf_FlowTasks
            .Where(t => t.InstanceId == inst.Id && t.NodeId == task.NodeId
                        && t.TokenId == task.TokenId
                        && t.StageIndex == task.StageIndex && t.StageRound == task.StageRound
                        && t.Status != FlowTaskStatus.Cancelled)
            .ToListAsync();
        int approved = nodeTasks.Count(t => t.Status == FlowTaskStatus.Approved);
        int rejected = nodeTasks.Count(t => t.Status == FlowTaskStatus.Rejected);
        var (decided, passed) = EvaluateNodeCounts(approved, rejected, nodeTasks.Count, task.Countersign);

        // ★ Task6/Fix4：写触达 inst 行 → UPDATE 带 WHERE RowVersion=@orig。置于 decided 判定之前，
        // 使"停泊（!decided 早退）"与"推进/驳回"两条 mutating 路径都参与 RowVersion 乐观并发，序列化
        // 并行会签的非终票（杜绝"双双读到对方仍 Pending → 双双停泊 → 丢失唤醒"令实例永卡 Running）。
        // 幂等 = 仅刷时间戳，单线程行为零变化；败方抛 DbUpdateConcurrencyException → ActAsync 重试重算。
        inst.ModifyDate = DateTime.Now;

        if (!decided)
        {
            await _db.SaveChangesAsync();   // 等其他会签人
            return;
        }

        CancelPendingTasks(nodeTasks);   // 节点已决，作废本节点其余在途
        if (passed)
        {
            var schema = await LoadSchemaAsync(inst.FlowKey);
            var tok = await _db.Wf_FlowTokens.FirstOrDefaultAsync(t => t.Id == task.TokenId);
            var plan = (tok is null || string.IsNullOrEmpty(tok.StagePlanJson))
                ? null
                : JsonSerializer.Deserialize<List<RuntimeApprovalStage>>(tok.StagePlanJson, JsonOpts);

            if (plan is null)
            {
                // 单档:与今天逐字等价(无 stage 过滤 + 直接 AdvanceToken)
                SkipPendingFormTos(inst.Id, task.NodeId, task.TokenId);
                if (tok is not null) await AdvanceToken(inst, schema, tok);
            }
            else
            {
                SkipPendingFormTos(inst.Id, task.NodeId, task.TokenId, task.StageIndex, task.StageRound);
                int k1 = task.StageIndex + 1;
                if (k1 < plan.Count)
                {
                    var node = FindNode(schema, task.NodeId)!;
                    await EnterStageAsync(inst, schema, node, tok!, plan, k1);   // 同节点同 token 建下档
                }
                else
                {
                    await AdvanceToken(inst, schema, tok!);                       // 末档过 → 去下一节点
                }
            }
        }
        else
        {
            inst.Status = FlowInstanceStatus.Rejected;
            CancelAllActiveTokens(inst.Id);   // ★ 驳回 = terminate，兄弟分支连坐
            VoidPendingFormTos(inst.Id);      // ★ T9：驳回连坐，全 Pending 传签履历行 → 作废
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
        {
            await _dispatcher.OnInstanceFinishedAsync(inst, approved: true, decidedBy, reason: null);
            await _notifier.FlowApprovedAsync(inst.StarterId, inst.Id, inst.FlowKey);   // ★ D-1 N-T5
        }
        else if (inst.Status == FlowInstanceStatus.Rejected)
        {
            await _dispatcher.OnInstanceFinishedAsync(inst, approved: false, decidedBy, reason);
            await _notifier.FlowRejectedAsync(inst.StarterId, inst.Id, inst.FlowKey, reason);   // ★ D-1 N-T5
        }
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

    // ── 进入节点：按 node.Type 多态分发到 INodeHandler（token 为操作主语）。兼容保留 CurrentNode 代表节点 ──
    internal async Task EnterNodeAsync(Wf_FlowInstance inst, FlowSchema schema, FlowNode node, Wf_FlowToken token)
    {
        inst.CurrentNode = node.Id;   // 兼容：保留代表节点
        var type = (node.Type ?? "approval").Trim().ToLowerInvariant();
        if (!_handlers.TryGetValue(type, out var handler))
            throw new InvalidOperationException($"未知节点类型：{node.Type}（节点 {node.Id}）");
        await handler.OnEnterAsync(new NodeContext { Inst = inst, Schema = schema, Node = node, Token = token, Engine = this });
    }

    /// <summary>节点到期时间（章07 §4）：配齐 TimeoutHours + TimeoutAction 才限时，否则不限时。</summary>
    internal static DateTime? NodeDueAt(FlowNode node)
        => node.TimeoutHours is int h && h > 0 && !string.IsNullOrWhiteSpace(node.TimeoutAction)
            ? DateTime.Now.AddHours(h)
            : null;

    /// <summary>委派替换（章07 §5）：原审批人处有效委派期 → 返回 (代理人, 被代理人)；否则 (原人, null)。</summary>
    internal async Task<(Guid assignee, Guid? delegatedFrom)> ResolveActualAssigneeAsync(Guid approverId)
    {
        var now = DateTime.Now;
        var d = await _db.Wf_FlowDelegates
            .Where(x => x.GrantorId == approverId && x.Enable && x.ValidFrom <= now && x.ValidTo >= now)
            .OrderByDescending(x => x.CreateDate)
            .FirstOrDefaultAsync();
        return d is null ? (approverId, null) : (d.DelegateId, approverId);
    }

    // ── NextNodeAsync 已退役：token 排他流转改由 AdvanceToken（FlowEngine.Tokens.cs）承担，等价旧兜底结束 ──

    internal void Suspend(Wf_FlowInstance inst, FlowNode node, string reason)
    {
        inst.Status = FlowInstanceStatus.Suspended;
        AddHistory(inst.Id, node.Id, inst.StarterId, "suspend", reason);
    }

    /// <summary>
    /// 异步服务任务成功恢复（spec §4.4，<b>幂等 P0-2</b>）：重载 inst/schema/token；
    /// 幂等闸 = token 已消费/取消（非 Active）<b>或</b> 已离开服务节点（NodeId != nodeId）→ 直接 no-op
    /// （防 worker 崩溃重投 / 重试导致二次推进）。否则合并 outputVars（经 <see cref="ServiceVarsHelper.MergeOutputVars"/>，
    /// 保留前缀 wf./sys./_internal. 被拦截，记 serviceVars 履历）→ <see cref="AdvanceToken"/> 沿成功边推进
    /// → 终态分发 → SaveChanges。整体包乐观并发重试×3（仿 <see cref="ActAsync"/>）。
    /// </summary>
    internal async Task ResumeServiceTokenAsync(Guid instanceId, Guid tokenId, string nodeId,
        Dictionary<string, object?>? outputVars)
    {
        for (int attempt = 0; ; attempt++)
        {
            try
            {
                var inst = await _db.Wf_FlowInstances.FirstOrDefaultAsync(i => i.Id == instanceId);
                if (inst is null) return;
                var token = await _db.Wf_FlowTokens.FirstOrDefaultAsync(t => t.Id == tokenId);
                // ★ P0-2 幂等闸：已恢复（消费/取消）或已离开该服务节点 → 不二次推进
                if (token is null || token.Status != FlowTokenStatus.Active || token.NodeId != nodeId) return;

                var schema = await LoadSchemaAsync(inst.FlowKey);

                if (outputVars is { Count: > 0 })
                {
                    var res = ServiceVarsHelper.MergeOutputVars(inst.VarsJson, outputVars);
                    inst.VarsJson = res.VarsJson;
                    AddHistory(inst.Id, nodeId, inst.StarterId, "serviceVars",
                        $"merged: [{string.Join(",", res.MergedKeys)}]; skipped: [{string.Join(",", res.SkippedKeys)}]");
                }

                await AdvanceToken(inst, schema, token);   // 沿成功边（跳 IsError）
                await DispatchIfFinishedAsync(inst, inst.StarterId, null);
                await _db.SaveChangesAsync();
                return;
            }
            catch (DbUpdateConcurrencyException) when (attempt < 2)
            {
                foreach (var e in _db.ChangeTracker.Entries().ToList()) await e.ReloadAsync();
            }
        }
    }

    /// <summary>
    /// 异步服务任务失败耗尽路由（spec §4.3，<b>幂等</b>）：重载 inst/schema/token；幂等闸同
    /// <see cref="ResumeServiceTokenAsync"/>（token 已离开 → no-op）。先把标准错误变量 <c>wf.serviceError</c>
    /// <b>直写</b>进 inst.VarsJson（保留命名空间，<b>不</b>经 MergeOutputVars 的 wf.* 拦截）；
    /// 节点有 IsError 出边 → <see cref="AdvanceAlongErrorEdge"/>，否则 <see cref="Suspend"/>。整体重试×3。
    /// </summary>
    internal async Task FailServiceTokenAsync(Guid instanceId, Guid tokenId, string nodeId, string? reason)
    {
        for (int attempt = 0; ; attempt++)
        {
            try
            {
                var inst = await _db.Wf_FlowInstances.FirstOrDefaultAsync(i => i.Id == instanceId);
                if (inst is null) return;
                var token = await _db.Wf_FlowTokens.FirstOrDefaultAsync(t => t.Id == tokenId);
                if (token is null || token.Status != FlowTokenStatus.Active || token.NodeId != nodeId) return;

                var schema = await LoadSchemaAsync(inst.FlowKey);
                var node = FindNode(schema, nodeId);
                if (node is null) return;

                // ★ P1-4：标准错误变量直写 wf.serviceError（受控保留路径，不走 MergeOutputVars 的 wf.* 拦截）
                inst.VarsJson = WriteServiceError(inst.VarsJson, nodeId, reason);

                if (schema.Edges.Any(e => e.From == nodeId && e.IsError == true))
                    await AdvanceAlongErrorEdge(inst, schema, token);   // 走错误边
                else
                    Suspend(inst, node, "服务任务失败:" + reason);

                await DispatchIfFinishedAsync(inst, inst.StarterId, reason);
                await _db.SaveChangesAsync();
                return;
            }
            catch (DbUpdateConcurrencyException) when (attempt < 2)
            {
                foreach (var e in _db.ChangeTracker.Entries().ToList()) await e.ReloadAsync();
            }
        }
    }

    /// <summary>把标准错误变量 <c>wf.serviceError{nodeId,message,failedAtUtc}</c> 直接写进 VarsJson（spec §4.3/P1-4）。
    /// 直写保留 <c>wf.</c> 命名空间（区别于 OutputVars 合并的 wf.* 拦截）。jobId/kind 在此层未知 → 省略。</summary>
    private static string WriteServiceError(string? varsJson, string nodeId, string? reason)
    {
        JsonObject root;
        if (!string.IsNullOrWhiteSpace(varsJson))
        {
            try   { root = JsonNode.Parse(varsJson)?.AsObject() ?? new JsonObject(); }
            catch { root = new JsonObject(); }
        }
        else root = new JsonObject();

        var wf = root["wf"] as JsonObject;
        if (wf is null) { wf = new JsonObject(); root["wf"] = wf; }
        wf["serviceError"] = new JsonObject
        {
            ["nodeId"]      = nodeId,
            ["message"]     = reason,
            ["failedAtUtc"] = DateTime.UtcNow.ToString("O"),
        };
        return root.ToJsonString();
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

    internal void AddHistory(Guid instanceId, string nodeId, Guid actorId, string action, string? comment)
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

    internal static FlowNode? FindNode(FlowSchema s, string id) => s.Nodes.FirstOrDefault(n => n.Id == id);

    internal static bool IsType(FlowNode n, string type) => string.Equals(n.Type, type, StringComparison.OrdinalIgnoreCase);

    internal static ApproverRule? BuildRule(FlowNode n)
    {
        if (string.IsNullOrWhiteSpace(n.ApproverStrategy)) return null;
        if (!Enum.TryParse<ApproverStrategy>(n.ApproverStrategy, ignoreCase: true, out var strat)) return null;
        return new ApproverRule(strat, n.ApproverLevels, n.ApproverRoleId, n.ApproverUserId)
        {
            FieldName = n.ApproverFieldName,
            MapKey    = n.ApproverMapKey,
            When      = n.ApproverWhen,
            Filter    = n.ApproverFilter,
            Members   = n.ApproverMembers?.Select(MapSpec).ToList(),
        };
    }

    /// <summary>设计期叶 spec → 运行期叶 ApproverRule(无 Members)。</summary>
    internal static ApproverRule MapSpec(ApproverSpec s)
    {
        Enum.TryParse<ApproverStrategy>(s.Strategy, ignoreCase: true, out var strat);
        return new ApproverRule(strat, s.ApproverLevels, s.ApproverRoleId, s.ApproverUserId)
        {
            FieldName = s.FieldName, MapKey = s.MapKey, When = s.When, Filter = s.Filter,
        };
    }
}
