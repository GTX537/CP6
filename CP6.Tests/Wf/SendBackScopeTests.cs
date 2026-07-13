using CP6.Core.Services.Wf;
using CP6.Entity.DomainModels.Wf;

namespace CP6.Tests.Wf;

/// <summary>退回作用域纯函数（hardening spec §5.1）。schema + 内存 token 直测，不动库。</summary>
public class SendBackScopeTests
{
    private static readonly Guid InstId = Guid.NewGuid();

    private static Wf_FlowToken Tok(string node, Guid? parent = null, Guid? fork = null,
        int status = FlowTokenStatus.Active)
        => new() { Id = Guid.NewGuid(), InstanceId = InstId, NodeId = node,
                   ParentTokenId = parent, ForkId = fork, Status = status };

    // s → n0 → split → ( a1 → a2 , b1 ) → join → end
    private static FlowSchema ParallelSchema() => new()
    {
        Start = "s",
        Nodes =
        {
            new FlowNode { Id = "s", Type = "start" },
            new FlowNode { Id = "n0", Type = "approval", ApproverStrategy = "Specified", ApproverUserId = Guid.NewGuid() },
            new FlowNode { Id = "split", Type = "parallelSplit" },
            new FlowNode { Id = "a1", Type = "approval", ApproverStrategy = "Specified", ApproverUserId = Guid.NewGuid() },
            new FlowNode { Id = "a2", Type = "approval", ApproverStrategy = "Specified", ApproverUserId = Guid.NewGuid() },
            new FlowNode { Id = "b1", Type = "approval", ApproverStrategy = "Specified", ApproverUserId = Guid.NewGuid() },
            new FlowNode { Id = "join", Type = "parallelJoin" },
            new FlowNode { Id = "end", Type = "end" },
        },
        Edges =
        {
            new FlowEdge { From = "s", To = "n0" }, new FlowEdge { From = "n0", To = "split" },
            new FlowEdge { From = "split", To = "a1" }, new FlowEdge { From = "a1", To = "a2" },
            new FlowEdge { From = "split", To = "b1" },
            new FlowEdge { From = "a2", To = "join" }, new FlowEdge { From = "b1", To = "join" },
            new FlowEdge { From = "join", To = "end" },
        },
    };

    [Fact]
    public void LinearToken_NoForkStack_BeforeSplit()
    {
        var schema = ParallelSchema();
        var root = Tok("n0");
        var (scope, strip) = SendBackScopeAnalyzer.Analyze(schema, new[] { root }, root, "n0", "s");
        Assert.Equal(SendBackScope.BeforeSplit, scope);
        Assert.Null(strip);
    }

    [Fact]
    public void SameBranch_TargetUpstreamInOwnBranch()
    {
        var schema = ParallelSchema();
        var f = Guid.NewGuid();
        var root = Tok("split", status: FlowTokenStatus.Consumed);
        var a = Tok("a2", parent: root.Id, fork: f);
        var b = Tok("b1", parent: root.Id, fork: f);
        var all = new[] { root, a, b };

        var (scope, strip) = SendBackScopeAnalyzer.Analyze(schema, all, a, "a2", "a1");
        Assert.Equal(SendBackScope.SameBranch, scope);
        Assert.Equal(a.Id, strip!.Id);                                     // 剥离层 = 本层分支代表 token
    }

    [Fact]
    public void BeforeSplit_TargetUpstreamOfSplit()
    {
        var schema = ParallelSchema();
        var f = Guid.NewGuid();
        var root = Tok("split", status: FlowTokenStatus.Consumed);
        var a = Tok("a2", parent: root.Id, fork: f);
        var all = new[] { root, a };

        var (scope, strip) = SendBackScopeAnalyzer.Analyze(schema, all, a, "a2", "n0");
        Assert.Equal(SendBackScope.BeforeSplit, scope);
        Assert.Null(strip);
    }

    [Fact]
    public void SiblingBranch_TargetInSiblingDomain()
    {
        var schema = ParallelSchema();
        var f = Guid.NewGuid();
        var root = Tok("split", status: FlowTokenStatus.Consumed);
        var a = Tok("a2", parent: root.Id, fork: f);
        var all = new[] { root, a };

        var (scope, _) = SendBackScopeAnalyzer.Analyze(schema, all, a, "a2", "b1");
        Assert.Equal(SendBackScope.SiblingBranch, scope);
    }

    [Fact]
    public void Nested_TargetBetweenOuterSplitAndInnerSplit_StripIsOuterLayer()
    {
        // s → outer → ( h1 → inner → (x1,x2) → ij , b1 ) → oj → end；current 在 x1，target = h1（内层 split 之前、外层支内）
        var schema = new FlowSchema
        {
            Start = "s",
            Nodes =
            {
                new FlowNode { Id = "s", Type = "start" },
                new FlowNode { Id = "outer", Type = "parallelSplit" },
                new FlowNode { Id = "h1", Type = "approval", ApproverStrategy = "Specified", ApproverUserId = Guid.NewGuid() },
                new FlowNode { Id = "inner", Type = "parallelSplit" },
                new FlowNode { Id = "x1", Type = "approval", ApproverStrategy = "Specified", ApproverUserId = Guid.NewGuid() },
                new FlowNode { Id = "x2", Type = "approval", ApproverStrategy = "Specified", ApproverUserId = Guid.NewGuid() },
                new FlowNode { Id = "ij", Type = "parallelJoin" },
                new FlowNode { Id = "b1", Type = "approval", ApproverStrategy = "Specified", ApproverUserId = Guid.NewGuid() },
                new FlowNode { Id = "oj", Type = "parallelJoin" },
                new FlowNode { Id = "end", Type = "end" },
            },
            Edges =
            {
                new FlowEdge { From = "s", To = "outer" },
                new FlowEdge { From = "outer", To = "h1" }, new FlowEdge { From = "h1", To = "inner" },
                new FlowEdge { From = "inner", To = "x1" }, new FlowEdge { From = "inner", To = "x2" },
                new FlowEdge { From = "x1", To = "ij" }, new FlowEdge { From = "x2", To = "ij" },
                new FlowEdge { From = "ij", To = "oj" },
                new FlowEdge { From = "outer", To = "b1" }, new FlowEdge { From = "b1", To = "oj" },
                new FlowEdge { From = "oj", To = "end" },
            },
        };
        var fo = Guid.NewGuid(); var fi = Guid.NewGuid();
        var root = Tok("outer", status: FlowTokenStatus.Consumed);
        var h = Tok("inner", parent: root.Id, fork: fo, status: FlowTokenStatus.Consumed);  // 外层支代表，已进内层 split
        var x = Tok("x1", parent: h.Id, fork: fi);
        var all = new[] { root, h, x };

        var (scope, strip) = SendBackScopeAnalyzer.Analyze(schema, all, x, "x1", "h1");
        Assert.Equal(SendBackScope.SameBranch, scope);
        Assert.Equal(h.Id, strip!.Id);                                     // ★ 剥离层是外层支代表 token（spec §5.2 对称规则）

        // 而 target 在内层同支内时剥离层是内层
        var (scope2, strip2) = SendBackScopeAnalyzer.Analyze(schema, all, x, "x1", "x1");
        // 自退回在调用方被 E-WF-012 拦，这里直接验证内层域命中路径：target=x1 属 x 的最内层域
        Assert.Equal(SendBackScope.SameBranch, scope2);
        Assert.Equal(x.Id, strip2!.Id);
    }

    [Fact]
    public void UnresolvablePairing_Throws_E_WF_012()
    {
        // split 无公共 join（一支直通 end）→ 结构不可判定 → 保守拒绝
        var schema = new FlowSchema
        {
            Start = "s",
            Nodes =
            {
                new FlowNode { Id = "s", Type = "start" },
                new FlowNode { Id = "split", Type = "parallelSplit" },
                new FlowNode { Id = "a", Type = "approval", ApproverStrategy = "Specified", ApproverUserId = Guid.NewGuid() },
                new FlowNode { Id = "b", Type = "approval", ApproverStrategy = "Specified", ApproverUserId = Guid.NewGuid() },
                new FlowNode { Id = "end", Type = "end" },
            },
            Edges =
            {
                new FlowEdge { From = "s", To = "split" },
                new FlowEdge { From = "split", To = "a" }, new FlowEdge { From = "split", To = "b" },
                new FlowEdge { From = "a", To = "end" }, new FlowEdge { From = "b", To = "end" },
            },
        };
        var f = Guid.NewGuid();
        var root = Tok("split", status: FlowTokenStatus.Consumed);
        var a = Tok("a", parent: root.Id, fork: f);
        var ex = Assert.Throws<InvalidOperationException>(
            () => SendBackScopeAnalyzer.Analyze(schema, new[] { root, a }, a, "a", "s"));
        Assert.Contains("E-WF-012", ex.Message);
    }
}
