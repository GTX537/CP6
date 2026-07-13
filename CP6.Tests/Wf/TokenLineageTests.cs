using CP6.Core.Services.Wf;
using CP6.Entity.DomainModels.Wf;

namespace CP6.Tests.Wf;

public class TokenLineageTests
{
    private static readonly Guid InstId = Guid.NewGuid();

    private static Wf_FlowToken Tok(Guid id, string node, Guid? parent = null, Guid? fork = null,
        int status = FlowTokenStatus.Active)
        => new() { Id = id, InstanceId = InstId, NodeId = node, ParentTokenId = parent, ForkId = fork, Status = status };

    [Fact]
    public void FlowTokenStatus_Pruned_Is3()
        => Assert.Equal(3, FlowTokenStatus.Pruned);

    [Fact]
    public void CrossesFork_SelfAndAncestor_True_Miss_False()
    {
        var f1 = Guid.NewGuid(); var f2 = Guid.NewGuid(); var fx = Guid.NewGuid();
        var root  = Tok(Guid.NewGuid(), "split", status: FlowTokenStatus.Consumed);
        var b     = Tok(Guid.NewGuid(), "innerSplit", parent: root.Id, fork: f1, status: FlowTokenStatus.Consumed);
        var inner = Tok(Guid.NewGuid(), "x1", parent: b.Id, fork: f2);
        var all = new[] { root, b, inner };

        Assert.True(TokenLineage.CrossesFork(all, inner, f2));   // 自身 ForkId
        Assert.True(TokenLineage.CrossesFork(all, inner, f1));   // 祖先链穿过外层批次（防提前放行的关键）
        Assert.False(TokenLineage.CrossesFork(all, inner, fx));  // 无关批次
        Assert.False(TokenLineage.CrossesFork(all, root, f1));   // 根不穿任何批次
    }

    [Fact]
    public void ForkParent_ReturnsBatchGenerator()
    {
        var f1 = Guid.NewGuid();
        var root = Tok(Guid.NewGuid(), "split", status: FlowTokenStatus.Consumed);
        var a    = Tok(Guid.NewGuid(), "a", parent: root.Id, fork: f1);
        var all = new[] { root, a };

        var p = TokenLineage.ForkParent(all, a);
        Assert.NotNull(p);
        Assert.Equal(root.Id, p!.Id);
        Assert.Equal("split", p.NodeId);                          // §4.1 定案：父 token.NodeId 即 split 节点
        Assert.Null(TokenLineage.ForkParent(all, root));          // 根无父
    }

    [Fact]
    public void ForkStack_Nested_InnerToOuter()
    {
        var f1 = Guid.NewGuid(); var f2 = Guid.NewGuid();
        var root  = Tok(Guid.NewGuid(), "split", status: FlowTokenStatus.Consumed);
        var b     = Tok(Guid.NewGuid(), "innerSplit", parent: root.Id, fork: f1, status: FlowTokenStatus.Consumed);
        var inner = Tok(Guid.NewGuid(), "x1", parent: b.Id, fork: f2);
        var all = new[] { root, b, inner };

        var stack = TokenLineage.ForkStack(all, inner);
        Assert.Equal(2, stack.Count);
        Assert.Equal((inner.Id, f2, "innerSplit"), (stack[0].BranchToken.Id, stack[0].ForkId, stack[0].SplitNodeId));
        Assert.Equal((b.Id, f1, "split"), (stack[1].BranchToken.Id, stack[1].ForkId, stack[1].SplitNodeId));
    }

    [Fact]
    public void ForkStack_LinearToken_Empty()
    {
        var root = Tok(Guid.NewGuid(), "n1");
        Assert.Empty(TokenLineage.ForkStack(new[] { root }, root));
    }
}
