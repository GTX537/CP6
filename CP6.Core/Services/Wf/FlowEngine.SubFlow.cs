using System.Text.Json.Nodes;
using CP6.Entity.DomainModels.Wf;
using Microsoft.EntityFrameworkCore;

namespace CP6.Core.Services.Wf;

/// <summary>子流程引擎面（spec §3）。partial：与 FlowEngine 共享 scoped DbContext 与内部方法。
/// 铁律：SubmitChildAsync/SubFlowErrorDisposeAsync 不自行 SaveChanges（随调用方外壳收口）；
/// CheckSubFlowGroupAsync/FastPathSubFlowResumeAsync 是提交后复核阶段，自带事务（B-T2/B-T3）。</summary>
public partial class FlowEngine
{
    /// <summary>发起子实例（spec §3.1 第 3 步）。与 <see cref="SubmitAsync"/> 机制同构，差异恰三点：
    /// ① 构造期写回指三列（起即终态子实例的第一段入队钩子依赖 ParentInstanceId 已就位）；
    /// ② 不 SaveChanges（handler 三律——随父动作外壳统一落库，子实例与父停泊同事务原子）；
    /// ③ 目标不存在/停用抛 E-WF-025（保存时校验已拦，运行时兜底防发布后停用）。
    /// 版本口径=发起时刻该 FlowKey 最新已发布版（SubmitAsync 既有口径，spec §3.1）。</summary>
    internal async Task<Guid> SubmitChildAsync(string flowKey, Guid starterId, string varsJson,
        Guid parentInstanceId, Guid parentTokenId, int subIndex)
    {
        var def = await _db.Wf_FlowDefs.FirstOrDefaultAsync(x => x.FlowKey == flowKey && x.Enable)
                  ?? throw new InvalidOperationException($"E-WF-025: 子流程引用不存在或已停用:{flowKey}");
        var schema = Deserialize(def.SchemaJson);
        var first = FirstNode(schema) ?? throw new InvalidOperationException($"E-WF-025: 子流程 {flowKey} 无节点");

        var inst = new Wf_FlowInstance
        {
            Id = Guid.NewGuid(),
            FlowKey = flowKey,
            VarsJson = string.IsNullOrWhiteSpace(varsJson) ? "{}" : varsJson,
            StarterId = starterId,
            Status = FlowInstanceStatus.Running,
            CurrentNode = first.Id,
            Creator = starterId.ToString(),
            ParentInstanceId = parentInstanceId,
            ParentTokenId = parentTokenId,
            SubIndex = subIndex,
        };
        _db.Wf_FlowInstances.Add(inst);
        AddHistory(inst.Id, first.Id, starterId, "submit", null);

        var root = SpawnToken(inst, first, parent: null, fork: null);
        await EnterNodeAsync(inst, schema, first, root);
        await DispatchIfFinishedAsync(inst, starterId, null);   // 起即终态(cs→ce)子实例：第一段入队钩子在此看见回指列
        return inst.Id;
    }

    /// <summary>子流程错误处置（spec §3.2 第 3 步 + D2）：subFlowError 注入父 vars →
    /// 有 IsError 出边走错误边；无则传播父驳回——父 token 在并行支且本层 split 配 prune 时剪枝
    /// （二期 <see cref="TryPruneBranchAsync"/> 分流，本方法零新增剪枝逻辑，语义自动组合），否则整单驳回。
    /// handler 的运行时 E-WF-025（集合非数组/N 超上限）与复核错误路径共用本方法。不 SaveChanges。</summary>
    internal async Task SubFlowErrorDisposeAsync(Wf_FlowInstance inst, FlowSchema schema, FlowNode node,
        Wf_FlowToken token, string? code, int subIndex, Guid? childInstanceId, int? childStatus)
    {
        inst.VarsJson = WriteSubFlowError(inst.VarsJson, node.Id, code, subIndex, childInstanceId, childStatus);
        AddHistory(inst.Id, node.Id, inst.StarterId, "subFlowError",
            code ?? $"child={childInstanceId};status={childStatus};subIndex={subIndex}");

        if (schema.Edges.Any(e => e.From == node.Id && e.IsError == true))
        {
            await AdvanceAlongErrorEdge(inst, schema, token);   // D2：错误边优先
            return;
        }
        // 无错边 → 传播驳回；剪枝分流与 ActOnceAsync 驳回分支同构（二期 B-T2 契约）
        if (token.ForkId is not null && await TryPruneBranchAsync(inst, schema, token, inst.StarterId, "subFlowError"))
            return;
        inst.Status = FlowInstanceStatus.Rejected;
        CancelAllActiveTokens(inst.Id);   // 驳回 = terminate（S-C 级联钩子在其内递归取消子实例）
        VoidPendingFormTos(inst.Id);
    }

    /// <summary>父 vars 顶层直写 subFlowError（非保留前缀，形态仿 <see cref="WriteServiceError"/>）。</summary>
    private static string WriteSubFlowError(string? varsJson, string nodeId, string? code,
        int subIndex, Guid? childInstanceId, int? childStatus)
    {
        JsonObject root;
        if (!string.IsNullOrWhiteSpace(varsJson))
        {
            try   { root = JsonNode.Parse(varsJson)?.AsObject() ?? new JsonObject(); }
            catch { root = new JsonObject(); }
        }
        else root = new JsonObject();

        root["subFlowError"] = new JsonObject
        {
            ["nodeId"]          = nodeId,
            ["code"]            = code,
            ["subIndex"]        = subIndex,
            ["childInstanceId"] = childInstanceId?.ToString(),
            ["childStatus"]     = childStatus,
            ["atUtc"]           = DateTime.UtcNow.ToString("O"),
        };
        return root.ToJsonString();
    }

    /// <summary>第二段复核（spec §3.2，幂等，fast path 与 worker 兜底共用）。恰一次保证：
    /// ① 三重状态闸（token Active + 停在 subFlow 节点 / 父实例 Running）；② 恢复/错误处置动作
    /// 触达父实例行（VarsJson 或 ModifyDate）→ SaveChanges 走 RowVersion 乐观并发，撞版 → 重读 → 闸零动作；
    /// ③ 计票前对子实例组逐行 Reload（「重读已提交数据」——身份映射会让同上下文旧读呈陈旧态，侦察结论 #6）。
    /// 丢唤醒由第一段原子入队闭合：每个子终态各自持凭据各自复核，后提交者必见完整组。</summary>
    internal async Task CheckSubFlowGroupAsync(Guid parentTokenId, CancellationToken ct = default)
    {
        for (int attempt = 0; ; attempt++)
        {
            try
            {
                var token = await _db.Wf_FlowTokens.FirstOrDefaultAsync(t => t.Id == parentTokenId, ct);
                if (token is null || token.Status != FlowTokenStatus.Active) return;          // 停泊状态闸：已恢复/已剪/已取消
                var inst = await _db.Wf_FlowInstances.FirstOrDefaultAsync(i => i.Id == token.InstanceId, ct);
                if (inst is null || inst.Status != FlowInstanceStatus.Running) return;        // 父实例状态闸：级联 Withdrawn 不回注
                var schema = await LoadSchemaAsync(inst.FlowKey);
                var node = FindNode(schema, token.NodeId);
                if (node is null || string.IsNullOrWhiteSpace(node.SubFlowKey)) return;       // token 已离开 subFlow 节点

                var children = await _db.Wf_FlowInstances
                    .Where(i => i.ParentTokenId == parentTokenId)
                    .OrderBy(i => i.SubIndex).ToListAsync(ct);
                if (children.Count == 0) return;                                              // 空集在 handler 已直通，此处组不存在
                foreach (var c in children.Where(c => _db.Entry(c).State == EntityState.Unchanged))
                    await _db.Entry(c).ReloadAsync(ct);                                       // ★ 重读已提交数据（防身份映射陈旧态）

                bool any = string.Equals((node.SubCompletionPolicy ?? SubFlowCompletionPolicy.All).Trim(),
                    SubFlowCompletionPolicy.Any, StringComparison.OrdinalIgnoreCase);
                var approved = children.Where(c => c.Status == FlowInstanceStatus.Approved).ToList();
                var dead     = children.Where(c => c.Status is FlowInstanceStatus.Rejected or FlowInstanceStatus.Withdrawn).ToList();
                var inFlight = children.Where(c => c.Status is FlowInstanceStatus.Running or FlowInstanceStatus.Suspended
                                                             or FlowInstanceStatus.Draft).ToList();

                if (!any)
                {
                    if (dead.Count > 0)
                    {
                        foreach (var c in inFlight) SubFlowCascade.CancelInstanceTree(_db, c);   // all：任一死→级联取消其余在途
                        await SubFlowErrorDisposeAsync(inst, schema, node, token,
                            null, dead[0].SubIndex ?? 0, dead[0].Id, dead[0].Status);
                    }
                    else if (inFlight.Count == 0)
                        await ResumeSubFlowAsync(inst, schema, node, token, approved, aggregate: node.SubCollectionVar != null);
                    else return;   // all 未齐——等下一个子终态的凭据
                }
                else
                {
                    if (approved.Count > 0)
                    {
                        foreach (var c in inFlight) SubFlowCascade.CancelInstanceTree(_db, c);   // any：恢复时级联撤回其余在途
                        await ResumeSubFlowAsync(inst, schema, node, token,
                            new List<Wf_FlowInstance> { approved[0] }, aggregate: false);        // 首个= SubIndex 最小的 Approved（确定性）
                    }
                    else if (inFlight.Count == 0 && dead.Count == children.Count)
                        await SubFlowErrorDisposeAsync(inst, schema, node, token,
                            null, dead[0].SubIndex ?? 0, dead[0].Id, dead[0].Status);
                    else return;   // any 未决
                }

                inst.ModifyDate = DateTime.Now;   // ★ 写触达父行 → RowVersion 乐观并发（恰一次闸，仿 ActOnceAsync Task6/Fix4）
                await DispatchIfFinishedAsync(inst, inst.StarterId, null);   // 错误处置可打出终态；父自身是子实例时递归入队（孙 subFlow）
                await _db.SaveChangesAsync(ct);
                return;
            }
            catch (DbUpdateConcurrencyException) when (attempt < 2)
            {
                foreach (var e in _db.ChangeTracker.Entries().ToList()) await e.ReloadAsync(ct);   // 撞版 → 重读 → 状态闸零动作
            }
        }
    }

    /// <summary>提交后 fast path（spec §3.2 两入口之一）：扫本上下文 Local 中 Pending 的 subFlowResume 凭据
    /// 逐条复核并标 Succeeded（worker 迟到看见已完成 → 状态闸零动作）。外层 for 让「复核推进父 → 父又终态 →
    /// 再入队祖父凭据」的嵌套链同请求收敛，上限与深度守卫同口径。撞 job RowVersion（worker 已抢走）→ 让给 worker。
    /// 对无 subFlow 的请求 = Local 空集 O(1) no-op。</summary>
    internal async Task FastPathSubFlowResumeAsync(CancellationToken ct = default)
    {
        for (int round = 0; round < SubFlowLimits.MaxDepth; round++)
        {
            var jobs = _db.Wf_ServiceJobs.Local
                .Where(j => j.Kind == WfJobKind.SubFlowResume && j.Status == ServiceJobStatus.Pending)
                .ToList();
            if (jobs.Count == 0) return;
            foreach (var job in jobs)
            {
                var payload = SubFlowResumePayload.Parse(job.ActionRefJson);
                if (payload is null) continue;                       // 载荷坏 → 留给 worker 标 Failed（唯一记账处）
                await CheckSubFlowGroupAsync(payload.ParentTokenId, ct);
                job.Status = ServiceJobStatus.Succeeded;             // 凭据已消费（组未齐也算——组齐由各子终态各自凭据保证）
                job.CompletedAtUtc = DateTime.UtcNow;
                try { await _db.SaveChangesAsync(ct); }
                catch (DbUpdateConcurrencyException) { await _db.Entry(job).ReloadAsync(ct); }
            }
        }
    }

    /// <summary>恢复路径（spec §3.2 第 2 步）：SubVarsOutJson 回注父 vars（MergeOutputVars 保留前缀同款拦截）
    /// → 恢复父 token 沿非错误出边推进。</summary>
    private async Task ResumeSubFlowAsync(Wf_FlowInstance inst, FlowSchema schema, FlowNode node, Wf_FlowToken token,
        IReadOnlyList<Wf_FlowInstance> approved, bool aggregate)
    {
        var outVars = SubFlowVarsMapper.BuildOutMerge(node.SubVarsOutJson,
            approved.Select(c => (c.SubIndex ?? 0, c.VarsJson)).ToList(), aggregate);
        if (outVars.Count > 0)
        {
            var merged = ServiceVarsHelper.MergeOutputVars(inst.VarsJson, outVars);
            inst.VarsJson = merged.VarsJson;
        }
        AddHistory(inst.Id, node.Id, inst.StarterId, "subFlowResumed", $"approved={approved.Count}");
        await AdvanceToken(inst, schema, token);   // 沿非错误出边（IsError != true）
    }
}
