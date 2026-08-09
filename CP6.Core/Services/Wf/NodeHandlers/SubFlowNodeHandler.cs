using System.Text.Json.Nodes;
using CP6.Entity.DomainModels.Wf;
using Microsoft.EntityFrameworkCore;

namespace CP6.Core.Services.Wf;

/// <summary>子流程 call-activity 节点（spec §3.1，第 9 个 handler）：解析集合展开 N 子实例
/// （<see cref="FlowEngine.SubmitChildAsync"/>，回指三列构造期写入），父 token 停泊（不 Advance 不 Consume，
/// 与 serviceTask async 停泊同形态）。N=0 空集直通；N 超上限/集合非数组 → 运行时 E-WF-025 错误处置；
/// 深度守卫 ≥8 → E-WF-026（保存时 DFS 的运行时兜底——环检测是保存时快照，后续发布可能引入新环）。
/// 停泊重入幂等：(ParentTokenId,SubIndex) 槽已存在 → 跳过（Local ∪ DB 先查 + UX_Wf_FlowInstance_SubSlot 双保险）。
/// handler 不 SaveChanges（引擎外壳收口）。</summary>
internal sealed class SubFlowNodeHandler : INodeHandler
{
    private readonly int _maxInstances;

    /// <param name="maxInstances">多实例 N 上限；DI 注册处读 app 配置 Wfs:SubFlowMaxInstances，缺省 100。</param>
    public SubFlowNodeHandler(int? maxInstances = null)
        => _maxInstances = maxInstances ?? SubFlowLimits.DefaultMaxInstances;

    public string Type => "subFlow";

    public async Task OnEnterAsync(NodeContext ctx)
    {
        var eng = ctx.Engine; var inst = ctx.Inst; var schema = ctx.Schema; var node = ctx.Node; var token = ctx.Token;

        // ① 防御式配置复检（E-WF-025；保存时校验已拦，坏 schema 直发兜底）
        if (string.IsNullOrWhiteSpace(node.SubFlowKey))
            throw new InvalidOperationException("E-WF-025: subFlow 节点缺 SubFlowKey");
        var policy = (node.SubCompletionPolicy ?? SubFlowCompletionPolicy.All).Trim().ToLowerInvariant();
        if (policy != SubFlowCompletionPolicy.All && policy != SubFlowCompletionPolicy.Any)
            throw new InvalidOperationException("E-WF-025: SubCompletionPolicy 非法");
        if (!SubFlowVarsMapper.TryParseMap(node.SubVarsInJson, out _) || !SubFlowVarsMapper.TryParseMap(node.SubVarsOutJson, out _))
            throw new InvalidOperationException("E-WF-025: 变量映射 JSON 非法");

        // ② 深度守卫（E-WF-026）：沿 ParentInstanceId 链上溯计数（spec §3.1）。
        //    Local ∪ DB 惯用法：同请求递归起子的祖先实例尚未 SaveChanges（三律②不落盘），只在变更
        //    追踪器里可见，纯 DB 查询会漏它们令链在首层断裂；故先查 Local（含未落盘 Add）再补 DB
        //    （worker 跨事务的已持久化深链）——与本 handler ④ 的槽位防重、引擎 HasActiveToken 同款口径。
        int depth = 0;
        var pid = inst.ParentInstanceId;
        while (pid is Guid p)
        {
            if (++depth >= SubFlowLimits.MaxDepth)
                throw new InvalidOperationException("E-WF-026: 子流程嵌套深度超限");
            var localParent = eng.Db.Wf_FlowInstances.Local.FirstOrDefault(i => i.Id == p);
            pid = localParent is not null
                ? localParent.ParentInstanceId
                : await eng.Db.Wf_FlowInstances.Where(i => i.Id == p)
                    .Select(i => i.ParentInstanceId).FirstOrDefaultAsync();
        }

        // ③ 集合解析（spec §3.1 第 2 步）
        JsonArray? coll = null;
        if (!string.IsNullOrWhiteSpace(node.SubCollectionVar))
        {
            var raw = SubFlowVarsMapper.ResolveNode("$." + node.SubCollectionVar, inst.VarsJson);
            if (raw is not JsonArray ja)
            {
                await eng.SubFlowErrorDisposeAsync(inst, schema, node, token, "E-WF-025", -1, null, null);   // 集合非数组
                return;
            }
            coll = ja;
            if (coll.Count == 0)
            {
                // N=0 空集完成：与完成策略无关，直接沿非错误出边前进、不回注（spec §3.1）
                eng.AddHistory(inst.Id, node.Id, inst.StarterId, "subFlowEmptyCollection", null);
                await eng.AdvanceToken(inst, schema, token);
                return;
            }
            if (coll.Count > _maxInstances)
            {
                await eng.SubFlowErrorDisposeAsync(inst, schema, node, token, "E-WF-025", -1, null, null);   // N 超上限
                return;
            }
        }
        int n = coll?.Count ?? 1;
        var childVersionId = await eng.ResolvePinnedSubFlowVersionAsync(inst, node.Id, node.SubFlowKey!);

        // ④ 逐 i 起子实例（停泊重入幂等：槽已存在跳过——Local ∪ DB 惯用法 + filtered unique 双保险）
        var childIds = new List<Guid>();
        for (int i = 0; i < n; i++)
        {
            int idx = i;
            bool exists = eng.Db.Wf_FlowInstances.Local.Any(x => x.ParentTokenId == token.Id && x.SubIndex == idx)
                || await eng.Db.Wf_FlowInstances.AnyAsync(x => x.ParentTokenId == token.Id && x.SubIndex == idx);
            if (exists) continue;
            var childVars = SubFlowVarsMapper.BuildChildVars(node.SubVarsInJson, inst.VarsJson,
                coll?[i], coll is null ? null : i);
            childIds.Add(await eng.SubmitChildAsync(childVersionId, inst.StarterId, childVars, inst.Id, token.Id, i));
        }
        eng.AddHistory(inst.Id, node.Id, inst.StarterId, "subFlowStarted",
            $"n={n}; children=[{string.Join(",", childIds)}]");
        // ⑤ 父 token 停泊：不 Advance、不 Consume（子实例本身就是停泊凭据；唤醒走两段式回注 §3.2）
    }
}
