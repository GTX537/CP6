using CP6.Entity.DomainModels.Wf;

namespace CP6.Core.Services.Wf;

/// <summary>token 血缘辅助（内核 hardening spec §3.3/§4/§5 共用同一口径）。全部纯函数，
/// 输入为 <see cref="FlowEngine.SnapshotTokens"/> 的实例内 token 快照；祖先链走内存 ParentTokenId 上溯
/// （单实例 token 数小，零额外查询）。环路防御：visited 集合。</summary>
internal static class TokenLineage
{
    /// <summary>t 自身 + 沿 ParentTokenId 的全部祖先（自内向外）。</summary>
    public static IEnumerable<Wf_FlowToken> AncestorChain(IReadOnlyList<Wf_FlowToken> all, Wf_FlowToken t)
    {
        var seen = new HashSet<Guid>();
        for (var cur = t; cur is not null && seen.Add(cur.Id);
             cur = cur.ParentTokenId is Guid pid ? all.FirstOrDefault(x => x.Id == pid) : null)
            yield return cur;
    }

    /// <summary>t「穿过」fork 批次 forkId ⇔ t 自身或祖先链上存在 ForkId==forkId 的 token（spec §3.3 定义）。</summary>
    public static bool CrossesFork(IReadOnlyList<Wf_FlowToken> all, Wf_FlowToken t, Guid forkId)
        => AncestorChain(all, t).Any(x => x.ForkId == forkId);

    /// <summary>生成 t.ForkId 批次的 token = Id==t.ParentTokenId 者；其 NodeId 即该批次的 split 节点（§4.1 定案）。
    /// t 无父 → null。</summary>
    public static Wf_FlowToken? ForkParent(IReadOnlyList<Wf_FlowToken> all, Wf_FlowToken t)
        => t.ParentTokenId is Guid pid ? all.FirstOrDefault(x => x.Id == pid) : null;

    /// <summary>t 的 fork 栈（内→外）：祖先链上每个 ForkId 非空的 token 贡献一层
    /// (该层分支代表 token, forkId, split 节点 id)。同 forkId 只取最靠 t 的一个（防御）。</summary>
    public static List<(Wf_FlowToken BranchToken, Guid ForkId, string SplitNodeId)> ForkStack(
        IReadOnlyList<Wf_FlowToken> all, Wf_FlowToken t)
    {
        var stack = new List<(Wf_FlowToken, Guid, string)>();
        var seenForks = new HashSet<Guid>();
        foreach (var tok in AncestorChain(all, t))
        {
            if (tok.ForkId is not Guid f || !seenForks.Add(f)) continue;
            var parent = ForkParent(all, tok);
            if (parent is null) continue;   // 血缘断裂（不完整快照）→ 该层不可判定，跳过
            stack.Add((tok, f, parent.NodeId));
        }
        return stack;
    }
}
