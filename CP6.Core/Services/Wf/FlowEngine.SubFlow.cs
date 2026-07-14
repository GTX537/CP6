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
}
